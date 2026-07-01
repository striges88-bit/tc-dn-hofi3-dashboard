using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CryptoIndicatorApp.Memory;

internal sealed class CuratedRetainImporter
{
    private readonly string _projectRoot;

    public CuratedRetainImporter(string projectRoot)
    {
        _projectRoot = projectRoot;
    }

    public async Task<RetainImportBatch> BuildImportAsync(string inputReportPath, string commitSpec)
    {
        var commitSha = await GitCommitMemoryIndexer.ResolveCommitAsync(_projectRoot, commitSpec);
        var treeSha = await GitCommitMemoryIndexer.ReadTreeAsync(_projectRoot, commitSha);
        var blockingReasons = new List<string>();
        var items = new List<RetainedMemoryItem>();
        var retainedAt = DateTimeOffset.UtcNow.ToString("O");

        if (!File.Exists(inputReportPath))
        {
            AddBlockingReason(blockingReasons, "missing_curated_retain_report");
            return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(inputReportPath));
        var root = document.RootElement;
        if (ReadBool(root, "external_retain_enabled")
            || ReadBool(root, "codex_auto_retain_enabled")
            || ReadBool(root, "cloud_enabled")
            || ReadBool(root, "calls_hindsight")
            || ReadBool(root, "calls_codex_retain")
            || ReadBool(root, "installs_hooks")
            || ReadBool(root, "runs_refresh_all")
            || ReadBool(root, "rebuilds_memory"))
        {
            AddBlockingReason(blockingReasons, "unsafe_input_report_flags");
        }

        if (!root.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
        {
            AddBlockingReason(blockingReasons, "missing_files_in_input_report");
            return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
        }

        foreach (var fileElement in filesElement.EnumerateArray())
        {
            var sourcePath = ReadString(fileElement, "path");
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                sourcePath = ReadString(fileElement, "source_path");
            }

            sourcePath = NormalizePath(sourcePath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                AddBlockingReason(blockingReasons, "invalid_sources_in_input_report");
                continue;
            }

            if (TestDeniedRetainPath(sourcePath))
            {
                AddBlockingReason(blockingReasons, "denied_sources_in_input_report");
                continue;
            }

            if (!TestAllowlistedRetainPath(sourcePath))
            {
                AddBlockingReason(blockingReasons, "invalid_sources_in_input_report");
                continue;
            }

            var redactionStatus = ReadString(fileElement, "redaction_status");
            var findingCount = ReadInt(fileElement, "finding_count");
            if (!redactionStatus.Equals("candidate", StringComparison.OrdinalIgnoreCase) || findingCount > 0)
            {
                AddBlockingReason(blockingReasons, "redaction_review_required");
                continue;
            }

            var reportedHash = ReadString(fileElement, "hash");
            if (string.IsNullOrWhiteSpace(reportedHash))
            {
                reportedHash = ReadString(fileElement, "source_hash");
            }

            if (string.IsNullOrWhiteSpace(reportedHash))
            {
                AddBlockingReason(blockingReasons, "missing_source_hash");
                continue;
            }

            var sourceBlobSha = GitCommitMemoryIndexer.ReadBlobSha(_projectRoot, commitSha, sourcePath);
            if (string.IsNullOrWhiteSpace(sourceBlobSha))
            {
                AddBlockingReason(blockingReasons, "missing_source_in_commit");
                continue;
            }

            var bytes = await GitCommitMemoryIndexer.ReadBlobBytesAsync(_projectRoot, sourceBlobSha);
            var sourceHash = Sha256(bytes);
            if (!sourceHash.Equals(reportedHash, StringComparison.OrdinalIgnoreCase))
            {
                AddBlockingReason(blockingReasons, "stale_source_metadata");
                continue;
            }

            var text = Encoding.UTF8.GetString(bytes);
            items.Add(new RetainedMemoryItem(
                $"retained.local.{Slug(sourcePath)}.{commitSha[..12]}",
                sourcePath,
                sourceHash,
                sourceBlobSha,
                commitSha,
                treeSha,
                "local-sqlite",
                "candidate",
                retainedAt,
                text));
        }

        return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
    }

    private static void AddBlockingReason(List<string> reasons, string reason)
    {
        if (!reasons.Contains(reason, StringComparer.Ordinal))
        {
            reasons.Add(reason);
        }
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        return 0;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool TestAllowlistedRetainPath(string relativePath)
    {
        var path = NormalizePath(relativePath);
        return path.Equals("AGENTS.md", StringComparison.Ordinal)
            || path.Equals("TC-DN-HOFI3.md", StringComparison.Ordinal)
            || path.Equals("docs/formulas.md", StringComparison.Ordinal)
            || path.Equals("tasks/lessons.md", StringComparison.Ordinal)
            || (path.StartsWith("docs/decisions/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.Ordinal))
            || (path.StartsWith("docs/memory/", StringComparison.Ordinal)
                && !path.StartsWith("docs/memory/generated/", StringComparison.Ordinal)
                && path.EndsWith(".md", StringComparison.Ordinal));
    }

    private static bool TestDeniedRetainPath(string relativePath)
    {
        var lower = NormalizePath(relativePath).ToLowerInvariant();
        foreach (var prefix in new[] { ".git/", ".hindsight/", ".gbrain/", ".graphify/", ".mem0/", ".graphiti/", "recordings/", "data/", "docs/memory/generated/", "secrets/" })
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        var segments = lower.Split('/');
        if (segments.Any(segment => segment is "bin" or "obj" or "publish"))
        {
            return true;
        }

        var fileName = Path.GetFileName(lower);
        return fileName.Equals(".env", StringComparison.Ordinal)
            || fileName.StartsWith(".env.", StringComparison.Ordinal)
            || lower.EndsWith(".jsonl", StringComparison.Ordinal)
            || lower.Contains("secret", StringComparison.Ordinal)
            || lower.Contains("credential", StringComparison.Ordinal)
            || lower.Contains("api-key", StringComparison.Ordinal)
            || lower.Contains("apikey", StringComparison.Ordinal)
            || lower.Contains("token", StringComparison.Ordinal)
            || lower.Contains("local-proxy", StringComparison.Ordinal)
            || lower.Contains("local_proxy", StringComparison.Ordinal)
            || lower.Contains("raw-experiment", StringComparison.Ordinal)
            || lower.Contains("raw_experiment", StringComparison.Ordinal)
            || lower.Contains("experiment-dump", StringComparison.Ordinal)
            || lower.Contains("experiment_dump", StringComparison.Ordinal)
            || lower.Contains("raw-dump", StringComparison.Ordinal)
            || lower.Contains("raw_dump", StringComparison.Ordinal)
            || lower.Contains("shadowsocks", StringComparison.Ordinal)
            || lower.Contains("ss-local", StringComparison.Ordinal);
    }

    private static string NormalizePath(string value)
    {
        return (value ?? string.Empty).Replace('\\', '/').Trim();
    }

    private string ToRepoPath(string path)
    {
        var root = Path.GetFullPath(_projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return fullPath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        return Regex.Replace(builder.ToString(), "-+", "-").Trim('-');
    }
}
