using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CryptoIndicatorApp.Memory;

internal static class CuratedRetainSourcePolicy
{
    private static readonly string[] DeniedPrefixes =
    [
        ".git/",
        ".hindsight/",
        ".gbrain/",
        ".graphify/",
        ".mem0/",
        ".graphiti/",
        "recordings/",
        "data/",
        "docs/memory/generated/",
        "secrets/",
    ];

    public static bool IsCanonicalRepoPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !path.Equals(path.Trim(), StringComparison.Ordinal)
            || path.Contains('\\')
            || Path.IsPathRooted(path)
            || Regex.IsMatch(path, "^[A-Za-z]:", RegexOptions.CultureInvariant))
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.None);
        return segments.Length > 0
            && segments.All(segment => !string.IsNullOrEmpty(segment) && segment is not "." and not "..");
    }

    public static bool IsAllowlisted(string path)
    {
        return path.Equals("AGENTS.md", StringComparison.Ordinal)
            || path.Equals("TC-DN-HOFI3.md", StringComparison.Ordinal)
            || path.Equals("docs/formulas.md", StringComparison.Ordinal)
            || path.Equals("tasks/lessons.md", StringComparison.Ordinal)
            || (path.StartsWith("docs/decisions/", StringComparison.Ordinal)
                && path.EndsWith(".md", StringComparison.Ordinal))
            || (path.StartsWith("docs/memory/", StringComparison.Ordinal)
                && !path.StartsWith("docs/memory/generated/", StringComparison.Ordinal)
                && path.EndsWith(".md", StringComparison.Ordinal));
    }

    public static bool IsDenied(string path)
    {
        var lower = path.ToLowerInvariant();
        if (DeniedPrefixes.Any(prefix => lower.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return true;
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
            || lower.Contains("proxy-local", StringComparison.Ordinal)
            || lower.Contains("proxy_local", StringComparison.Ordinal)
            || lower.Contains("raw-experiment", StringComparison.Ordinal)
            || lower.Contains("raw_experiment", StringComparison.Ordinal)
            || lower.Contains("experiment-dump", StringComparison.Ordinal)
            || lower.Contains("experiment_dump", StringComparison.Ordinal)
            || lower.Contains("raw-dump", StringComparison.Ordinal)
            || lower.Contains("raw_dump", StringComparison.Ordinal)
            || lower.Contains("shadowsocks", StringComparison.Ordinal)
            || lower.Contains("ss-local", StringComparison.Ordinal);
    }
}

internal sealed record CuratedRetainFinding(
    string Type,
    string Severity,
    bool PolicyReference,
    string SourcePath,
    int Line,
    string Rule);

internal sealed record CuratedRetainScannedFile(
    string Path,
    string Hash,
    long SizeBytes,
    string RedactionStatus,
    int FindingCount);

internal sealed record CuratedRetainScanResult(
    int SchemaVersion,
    string Scanner,
    string Status,
    IReadOnlyList<CuratedRetainScannedFile> Files,
    IReadOnlyList<CuratedRetainFinding> Findings);

internal sealed class CuratedRetainScanner
{
    public const string ScannerId = "CryptoIndicatorApp.Memory.CuratedRetainScanner/v1";

    private static readonly Regex SecretMarkerPattern = CreateRegex(
        @"(OPENAI_API_KEY|BINANCE_API_KEY|API[_ -]?KEY|\bSECRETS?\b|\bTOKENS?\b(?!-)|\bCREDENTIALS?\b|\bPASSWORDS?\b|sk-[A-Za-z0-9_-]{8,})");

    private static readonly Regex SecretValuePattern = CreateRegex(
        @"(sk-[A-Za-z0-9_-]{8,}|(OPENAI_API_KEY|BINANCE_API_KEY|API[_ -]?KEY|\bSECRETS?\b|\bTOKENS?\b(?!-)|\bCREDENTIALS?\b|\bPASSWORDS?\b)\s*[:=]\s*[""']?[A-Za-z0-9_./+=-]{8,})");

    private static readonly Regex PolicyReferencePattern = CreateRegex(
        @"(do not|must not|never|not retained|not retain|not store|excluded|denylist|disabled|redaction|policy|forbidden|forbid)");

    private static readonly (string Type, string Rule, Regex Pattern)[] ReferenceRules =
    [
        ("env_reference", ".env marker", CreateRegex(@"(^|[\s`'""])\.env($|[\s`'"".])|env contents|env file")),
        ("absolute_local_path", "machine-local absolute path", CreateRegex(@"([A-Z]:\\Users\\|C:\\Users\\|/Users/|/home/)")),
        ("local_proxy_detail", "local proxy detail", CreateRegex(@"(local proxy|local-proxy|local_proxy|shadowsocks|ss-local|socks5|127\.0\.0\.1:\d+|localhost:\d+)")),
        ("raw_jsonl_or_dump", "raw recording or dump reference", CreateRegex(@"(raw JSONL|JSONL dump|raw dump|raw experiment|experiment dump|recordings/.*\.jsonl)")),
        ("generated_export_reference", "generated export reference", CreateRegex(@"(docs/memory/generated/|generated export|generated exports|memory export)")),
    ];

    public async Task<CuratedRetainScanResult> ScanProjectAsync(string projectRoot)
    {
        var files = new List<CuratedRetainScannedFile>();
        var findings = new List<CuratedRetainFinding>();

        foreach (var sourcePath in EnumerateSourcePaths(projectRoot))
        {
            var fullPath = Path.Combine(projectRoot, sourcePath.Replace('/', Path.DirectorySeparatorChar));
            var bytes = await File.ReadAllBytesAsync(fullPath);
            var fileFindings = ScanBytes(sourcePath, bytes);
            findings.AddRange(fileFindings);
            files.Add(new CuratedRetainScannedFile(
                sourcePath,
                Sha256(bytes),
                bytes.LongLength,
                fileFindings.Count == 0 ? "candidate" : "review_required",
                fileFindings.Count));
        }

        return new CuratedRetainScanResult(
            1,
            ScannerId,
            "scanned",
            files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray(),
            findings
                .OrderBy(finding => finding.SourcePath, StringComparer.Ordinal)
                .ThenBy(finding => finding.Line)
                .ThenBy(finding => finding.Type, StringComparer.Ordinal)
                .ToArray());
    }

    public IReadOnlyList<CuratedRetainFinding> ScanBytes(string sourcePath, byte[] bytes)
    {
        if (!CuratedRetainSourcePolicy.IsCanonicalRepoPath(sourcePath)
            || !CuratedRetainSourcePolicy.IsAllowlisted(sourcePath)
            || CuratedRetainSourcePolicy.IsDenied(sourcePath))
        {
            throw new InvalidOperationException($"Cannot scan non-curated source path: {sourcePath}");
        }

        if (!TryDecodeUtf8(bytes, out var text))
        {
            throw new DecoderFallbackException("Curated retain sources must be valid UTF-8.");
        }

        var lines = NormalizeLineEndings(text).Split('\n');
        var findings = new List<CuratedRetainFinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;
            var policyReference = PolicyReferencePattern.IsMatch(line);

            if (SecretMarkerPattern.IsMatch(line))
            {
                AddFinding(
                    findings,
                    seen,
                    new CuratedRetainFinding(
                        "secret_reference",
                        policyReference ? "info" : SecretValuePattern.IsMatch(line) ? "critical" : "review",
                        policyReference,
                        sourcePath,
                        lineNumber,
                        "secret/token/api key marker"));
            }

            foreach (var rule in ReferenceRules)
            {
                if (!rule.Pattern.IsMatch(line))
                {
                    continue;
                }

                AddFinding(
                    findings,
                    seen,
                    new CuratedRetainFinding(
                        rule.Type,
                        policyReference ? "info" : "review",
                        policyReference,
                        sourcePath,
                        lineNumber,
                        rule.Rule));
            }
        }

        return findings;
    }

    public bool IsReviewedRedactionOf(
        byte[] sourceBytes,
        string redactedText,
        IReadOnlyList<CuratedRetainFinding> findings)
    {
        if (findings.Count == 0)
        {
            return false;
        }

        if (!TryDecodeUtf8(sourceBytes, out var sourceText))
        {
            return false;
        }

        var sourceLines = SplitExactLines(sourceText);
        var redactedLines = SplitExactLines(redactedText);
        if (sourceLines.Count != redactedLines.Count
            || findings.Any(finding => finding.Line < 1 || finding.Line > sourceLines.Count))
        {
            return false;
        }

        var findingsByLine = findings
            .GroupBy(finding => finding.Line)
            .ToDictionary(
                group => group.Key,
                group => group.Select(finding => finding.Type).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());

        for (var index = 0; index < sourceLines.Count; index++)
        {
            var lineNumber = index + 1;
            var sourceLine = sourceLines[index];
            var redactedLine = redactedLines[index];
            if (!sourceLine.Separator.Equals(redactedLine.Separator, StringComparison.Ordinal))
            {
                return false;
            }

            if (!findingsByLine.TryGetValue(lineNumber, out var types))
            {
                if (!sourceLine.Content.Equals(redactedLine.Content, StringComparison.Ordinal))
                {
                    return false;
                }

                continue;
            }

            var bom = lineNumber == 1 && sourceLine.Content.StartsWith('\uFEFF') ? "\uFEFF" : string.Empty;
            var expectedMarker = $"{bom}[REDACTED:{string.Join(',', types)}]";
            if (!redactedLine.Content.Equals(expectedMarker, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryDecodeUtf8(byte[] bytes, out string text)
    {
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static IReadOnlyList<ExactLine> SplitExactLines(string text)
    {
        var lines = new List<ExactLine>();
        var contentStart = 0;

        for (var index = 0; index < text.Length; index++)
        {
            string separator;
            if (text[index] == '\r')
            {
                separator = index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
            }
            else if (text[index] == '\n')
            {
                separator = "\n";
            }
            else
            {
                continue;
            }

            lines.Add(new ExactLine(text[contentStart..index], separator));
            if (separator.Length == 2)
            {
                index++;
            }

            contentStart = index + 1;
        }

        lines.Add(new ExactLine(text[contentStart..], string.Empty));
        return lines;
    }

    private readonly record struct ExactLine(string Content, string Separator);

    private static IEnumerable<string> EnumerateSourcePaths(string projectRoot)
    {
        var required = new[] { "AGENTS.md", "TC-DN-HOFI3.md", "docs/formulas.md", "tasks/lessons.md" };
        foreach (var sourcePath in required)
        {
            var fullPath = Path.Combine(projectRoot, sourcePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"Required curated retain source is missing: {sourcePath}");
            }

            yield return sourcePath;
        }

        foreach (var directoryPath in new[] { "docs/decisions", "docs/memory" })
        {
            var fullDirectoryPath = Path.Combine(projectRoot, directoryPath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullDirectoryPath))
            {
                continue;
            }

            foreach (var fullPath in Directory.EnumerateFiles(fullDirectoryPath, "*.md", SearchOption.TopDirectoryOnly))
            {
                var sourcePath = Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
                if (CuratedRetainSourcePolicy.IsCanonicalRepoPath(sourcePath)
                    && CuratedRetainSourcePolicy.IsAllowlisted(sourcePath)
                    && !CuratedRetainSourcePolicy.IsDenied(sourcePath))
                {
                    yield return sourcePath;
                }
            }
        }
    }

    private static void AddFinding(
        List<CuratedRetainFinding> findings,
        HashSet<string> seen,
        CuratedRetainFinding finding)
    {
        var key = $"{finding.Type}|{finding.SourcePath}|{finding.Line}|{finding.Rule}";
        if (seen.Add(key))
        {
            findings.Add(finding);
        }
    }

    private static Regex CreateRegex(string pattern)
    {
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
