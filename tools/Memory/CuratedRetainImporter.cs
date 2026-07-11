using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CryptoIndicatorApp.Memory;

internal sealed class CuratedRetainImporter
{
    private static readonly string[] RequiredFalseSafetyFlags =
    [
        "external_retain_enabled",
        "codex_auto_retain_enabled",
        "cloud_enabled",
        "calls_hindsight",
        "calls_codex_retain",
        "installs_hooks",
        "runs_refresh_all",
        "rebuilds_memory",
        "imports_denylist",
    ];

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
        if (RequiredFalseSafetyFlags.Any(propertyName => ReadBool(root, propertyName)))
        {
            AddBlockingReason(blockingReasons, "unsafe_input_report_flags");
            return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
        }

        var reportStatus = ReadString(root, "status");
        var reportReasonsPresent = root.TryGetProperty("blocking_reasons", out var reportReasons);
        var reportReasonsMalformed = reportReasonsPresent && reportReasons.ValueKind != JsonValueKind.Array;
        var reportHasBlockingReasons = false;
        if (reportReasonsPresent && !reportReasonsMalformed)
        {
            foreach (var reason in reportReasons.EnumerateArray())
            {
                if (reason.ValueKind != JsonValueKind.String)
                {
                    reportReasonsMalformed = true;
                    break;
                }

                reportHasBlockingReasons |= !string.IsNullOrWhiteSpace(reason.GetString());
            }
        }

        if (reportStatus.Equals("blocked", StringComparison.OrdinalIgnoreCase)
            || reportStatus.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || reportHasBlockingReasons)
        {
            AddBlockingReason(blockingReasons, "input_report_blocked");
            return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
        }

        var reportSchemaVersion = ReadInt(root, "schema_version");
        var reportMode = ReadString(root, "mode");
        var reportGenerator = ReadString(root, "generator");
        var isDryRunContract = reportSchemaVersion == 1
            && reportMode.Equals("dry-run", StringComparison.OrdinalIgnoreCase)
            && (reportStatus.Equals("ready_for_review", StringComparison.OrdinalIgnoreCase)
                || reportStatus.Equals("review_required", StringComparison.OrdinalIgnoreCase));
        var isRedactedSubsetContract = reportSchemaVersion == 2
            && reportMode.Equals("redacted-subset", StringComparison.OrdinalIgnoreCase)
            && reportStatus.Equals("ready_for_import", StringComparison.OrdinalIgnoreCase);
        if (!isDryRunContract && !isRedactedSubsetContract)
        {
            AddBlockingReason(blockingReasons, "unsupported_input_report_contract");
            return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
        }

        var expectedGenerator = isDryRunContract
            ? "scripts/curated-retain-dry-run.ps1"
            : "scripts/curated-retain-redacted-subset.ps1";
        if (!reportGenerator.Equals(expectedGenerator, StringComparison.Ordinal)
            || !HasRequiredSafetyMetadata(root)
            || reportReasonsMalformed
            || (isRedactedSubsetContract && !reportReasonsPresent))
        {
            AddBlockingReason(blockingReasons, "incomplete_input_report_contract");
            return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
        }

        if (reportStatus.Equals("review_required", StringComparison.OrdinalIgnoreCase))
        {
            AddBlockingReason(blockingReasons, "input_report_blocked");
        }

        if (!root.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
        {
            AddBlockingReason(blockingReasons, "missing_files_in_input_report");
            return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
        }

        foreach (var fileElement in filesElement.EnumerateArray())
        {
            if (fileElement.ValueKind != JsonValueKind.Object)
            {
                AddBlockingReason(blockingReasons, "incomplete_input_report_contract");
                continue;
            }

            var sourcePath = ReadString(fileElement, "path");
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                sourcePath = ReadString(fileElement, "source_path");
            }

            sourcePath = NormalizePath(sourcePath);
            if (string.IsNullOrWhiteSpace(sourcePath) || !IsSafeRelativeRepoPath(sourcePath))
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
            if (!TryReadInt(fileElement, "finding_count", out var findingCount) || findingCount < 0)
            {
                AddBlockingReason(blockingReasons, "incomplete_input_report_contract");
                continue;
            }

            var isCandidate = redactionStatus.Equals("candidate", StringComparison.OrdinalIgnoreCase);
            var isRedacted = redactionStatus.Equals("redacted", StringComparison.OrdinalIgnoreCase);
            if ((!isCandidate && !isRedacted) || findingCount > 0)
            {
                AddBlockingReason(blockingReasons, "redaction_review_required");
                continue;
            }

            var redactedText = ReadString(fileElement, "redacted_text");
            if (isRedacted && string.IsNullOrWhiteSpace(redactedText))
            {
                AddBlockingReason(blockingReasons, "missing_redacted_text");
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

            var text = isRedacted ? redactedText : Encoding.UTF8.GetString(bytes);
            items.Add(new RetainedMemoryItem(
                $"retained.local.{Slug(sourcePath)}.{commitSha[..12]}",
                sourcePath,
                sourceHash,
                sourceBlobSha,
                commitSha,
                treeSha,
                "local-sqlite",
                isRedacted ? "redacted" : "candidate",
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

    private static bool HasRequiredSafetyMetadata(JsonElement root)
    {
        return RequiredFalseSafetyFlags.All(propertyName => HasBooleanValue(root, propertyName, expected: false))
            && HasBooleanValue(root, "output_is_generated", expected: true)
            && HasBooleanValue(root, "output_should_be_ignored", expected: true)
            && HasBooleanValue(root, "writes_report_only", expected: true);
    }

    private static bool HasBooleanValue(JsonElement element, string propertyName, bool expected)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean() == expected;
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

    private static bool TryReadInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
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

    private static bool IsSafeRelativeRepoPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        return NormalizePath(relativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment is not "." and not "..");
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
