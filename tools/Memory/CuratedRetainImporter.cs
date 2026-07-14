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
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        var retainedAt = DateTimeOffset.UtcNow.ToString("O");
        var scanner = new CuratedRetainScanner();

        if (!File.Exists(inputReportPath))
        {
            AddBlockingReason(blockingReasons, "missing_curated_retain_report");
            return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
        }

        using var document = await TryReadReportAsync(inputReportPath, blockingReasons);
        if (document is null)
        {
            return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
        }

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
        var isRedactedSubsetContract = reportSchemaVersion == 2
            && reportMode.Equals("redacted-subset", StringComparison.OrdinalIgnoreCase)
            && reportStatus.Equals("ready_for_import", StringComparison.OrdinalIgnoreCase);
        if (!isRedactedSubsetContract)
        {
            AddBlockingReason(blockingReasons, "unsupported_input_report_contract");
            return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
        }

        const string expectedGenerator = "scripts/curated-retain-redacted-subset.ps1";
        if (!reportGenerator.Equals(expectedGenerator, StringComparison.Ordinal)
            || !HasRequiredSafetyMetadata(root)
            || (isRedactedSubsetContract && !HasRequiredSubsetContentMetadata(root))
            || reportReasonsMalformed
            || !reportReasonsPresent)
        {
            AddBlockingReason(blockingReasons, "incomplete_input_report_contract");
            return new RetainImportBatch(ToRepoPath(inputReportPath), commitSha, treeSha, blockingReasons, items);
        }

        if (!root.TryGetProperty("findings", out var findingsElement)
            || findingsElement.ValueKind != JsonValueKind.Array
            || findingsElement.GetArrayLength() != 0)
        {
            AddBlockingReason(blockingReasons, "invalid_input_report_findings");
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

        var hasRedactedPayload = false;
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            if (!CuratedRetainSourcePolicy.IsCanonicalRepoPath(sourcePath))
            {
                AddBlockingReason(blockingReasons, "invalid_sources_in_input_report");
                continue;
            }

            if (CuratedRetainSourcePolicy.IsDenied(sourcePath))
            {
                AddBlockingReason(blockingReasons, "denied_sources_in_input_report");
                continue;
            }

            if (!CuratedRetainSourcePolicy.IsAllowlisted(sourcePath))
            {
                AddBlockingReason(blockingReasons, "invalid_sources_in_input_report");
                continue;
            }

            if (!sourcePaths.Add(sourcePath))
            {
                AddBlockingReason(blockingReasons, "duplicate_sources_in_input_report");
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

            if (isRedactedSubsetContract
                && !HasValidSubsetFileContentMetadata(fileElement, isCandidate, isRedacted, redactedText))
            {
                AddBlockingReason(blockingReasons, "incomplete_input_report_contract");
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

            if (!TryReadLong(fileElement, "size_bytes", out var reportedSize) || reportedSize < 0)
            {
                AddBlockingReason(blockingReasons, "incomplete_input_report_contract");
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
            if (bytes.LongLength != reportedSize
                || !sourceHash.Equals(reportedHash, StringComparison.OrdinalIgnoreCase))
            {
                AddBlockingReason(blockingReasons, "stale_source_metadata");
                continue;
            }

            IReadOnlyList<CuratedRetainFinding> scannedFindings;
            try
            {
                scannedFindings = scanner.ScanBytes(sourcePath, bytes);
            }
            catch (DecoderFallbackException)
            {
                AddBlockingReason(blockingReasons, "invalid_source_encoding");
                continue;
            }

            if (isCandidate && scannedFindings.Count != 0)
            {
                AddBlockingReason(blockingReasons, "redaction_review_required");
                continue;
            }

            if (isRedacted
                && (!TryReadInt(fileElement, "original_finding_count", out var originalFindingCount)
                    || originalFindingCount != scannedFindings.Count
                    || !scanner.IsReviewedRedactionOf(bytes, redactedText, scannedFindings)))
            {
                AddBlockingReason(blockingReasons, "invalid_redacted_text_derivation");
                continue;
            }

            var text = isRedacted ? redactedText : Encoding.UTF8.GetString(bytes);
            hasRedactedPayload |= isRedacted;
            var pathHash = Sha256(Encoding.UTF8.GetBytes(sourcePath));
            var itemId = $"retained.local.{Slug(sourcePath)}.{pathHash}.{commitSha[..12]}";
            if (!itemIds.Add(itemId))
            {
                AddBlockingReason(blockingReasons, "duplicate_retained_ids");
                continue;
            }

            items.Add(new RetainedMemoryItem(
                itemId,
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

        if (isRedactedSubsetContract
            && (!HasBooleanValue(root, "source_derived_text_included", hasRedactedPayload)
                || !HasBooleanValue(root, "redacted_text_included", hasRedactedPayload)
                || !HasBooleanValue(root, "candidate_text_included", expected: false)
                || !HasBooleanValue(root, "raw_source_text_included", expected: false)))
        {
            AddBlockingReason(blockingReasons, "incomplete_input_report_contract");
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

    private static async Task<JsonDocument?> TryReadReportAsync(string path, List<string> blockingReasons)
    {
        try
        {
            return JsonDocument.Parse(await File.ReadAllTextAsync(path));
        }
        catch (JsonException)
        {
            AddBlockingReason(blockingReasons, "invalid_input_report_json");
            return null;
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

    private static bool HasRequiredSubsetContentMetadata(JsonElement root)
    {
        return HasBooleanProperty(root, "raw_source_text_included")
            && HasBooleanProperty(root, "source_derived_text_included")
            && HasBooleanProperty(root, "candidate_text_included")
            && HasBooleanProperty(root, "redacted_text_included");
    }

    private static bool HasValidSubsetFileContentMetadata(
        JsonElement file,
        bool isCandidate,
        bool isRedacted,
        string redactedText)
    {
        if (!TryReadInt(file, "original_finding_count", out var originalFindingCount)
            || originalFindingCount < 0
            || !HasBooleanValue(file, "raw_source_text_included", expected: false)
            || !HasBooleanValue(file, "candidate_text_included", expected: false))
        {
            return false;
        }

        var contentKind = ReadString(file, "content_kind");
        var redactedHash = ReadString(file, "redacted_hash");
        if (isCandidate)
        {
            return originalFindingCount == 0
                && contentKind.Equals("commit-source-reference", StringComparison.Ordinal)
                && HasBooleanValue(file, "source_derived_text_included", expected: false)
                && HasBooleanValue(file, "redacted_text_included", expected: false)
                && string.IsNullOrEmpty(redactedText)
                && string.IsNullOrEmpty(redactedHash);
        }

        return isRedacted
            && originalFindingCount > 0
            && contentKind.Equals("reviewed-redacted-text", StringComparison.Ordinal)
            && HasBooleanValue(file, "source_derived_text_included", expected: true)
            && HasBooleanValue(file, "redacted_text_included", expected: true)
            && Regex.IsMatch(redactedHash, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)
            && redactedHash.Equals(Sha256(Encoding.UTF8.GetBytes(redactedText)), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasBooleanProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False;
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

    private static bool TryReadLong(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
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
