using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CryptoIndicatorApp.Memory;

public sealed class ProjectMemoryIndexer
{
    private static readonly string[] ExcludedPrefixes =
    [
        ".git/",
        ".dotnet/",
        ".dotnet-home/",
        ".nuget/",
        ".superpowers/",
        ".tools/",
        ".hindsight/",
        ".gbrain/",
        ".graphify/",
        ".mem0/",
        ".graphiti/",
        "recordings/",
        "data/",
        "docs/memory/generated/",
    ];

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".sln",
        ".xaml",
        ".json",
        ".md",
        ".ps1",
        ".editorconfig",
        ".gitignore",
        ".gitattributes",
    };

    private readonly string _projectRoot;

    public ProjectMemoryIndexer(string projectRoot)
    {
        _projectRoot = projectRoot;
    }

    public async Task<ProjectMemorySnapshot> BuildSnapshotAsync()
    {
        var indexedAt = DateTimeOffset.UtcNow.ToString("O");
        var files = new List<IndexedFile>();
        var documents = new List<SearchDocument>();
        var rules = new List<RuleRecord>();
        var adrs = new List<AdrRecord>();
        var formulas = new List<FormulaVersionRecord>();
        var symbols = new List<SymbolRecord>();
        var events = new List<EventRecord>();
        var relations = new List<RelationRecord>();

        foreach (var file in Directory.EnumerateFiles(_projectRoot, "*", SearchOption.AllDirectories)
                     .Select(path => new FileInfo(path))
                     .Where(ShouldIndexFile)
                     .OrderBy(file => GetRelativePath(file.FullName), StringComparer.Ordinal))
        {
            var relativePath = GetRelativePath(file.FullName);
            var hash = await HashFileAsync(file.FullName);
            var text = await File.ReadAllTextAsync(file.FullName);
            files.Add(new IndexedFile(relativePath, hash, file.Length));
            if (!IsStructuredMemoryFile(relativePath))
            {
                AddChunks(documents, relativePath, hash, text);
            }

            AddSpecializedRecords(relativePath, hash, text, documents, rules, adrs, formulas, symbols, events, relations);
        }

        return new ProjectMemorySnapshot(
            files,
            documents,
            rules,
            adrs,
            formulas,
            symbols,
            events,
            relations,
            MemorySnapshotMetadata.ForWorkingTree(indexedAt));
    }

    private void AddSpecializedRecords(
        string path,
        string hash,
        string text,
        List<SearchDocument> documents,
        List<RuleRecord> rules,
        List<AdrRecord> adrs,
        List<FormulaVersionRecord> formulas,
        List<SymbolRecord> symbols,
        List<EventRecord> events,
        List<RelationRecord> relations)
    {
        if (path.Equals("docs/formulas.md", StringComparison.OrdinalIgnoreCase))
        {
            var owner = text.Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("Owner:", StringComparison.OrdinalIgnoreCase))
                ?.Split(':', 2)[1]
                .Trim();
            var formula = new FormulaVersionRecord("formula_version.tc-dn-hofi3.current", "current", owner, text, path, hash);
            formulas.Add(formula);
            documents.Add(ToSearchDocument(formula.Id, "formula_version", formula.Status, "Current TC-DN-HOFI3 OFI formula", formula.Text, path, hash));
        }

        if (path.StartsWith("docs/decisions/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var title = ExtractTitle(text, Path.GetFileNameWithoutExtension(path));
            var adr = new AdrRecord($"adr.{Path.GetFileNameWithoutExtension(path)}", "current", title, text, path, hash);
            adrs.Add(adr);
            documents.Add(ToSearchDocument(adr.Id, "adr", adr.Status, adr.Title, adr.Text, path, hash));
        }

        if (path.Equals("docs/memory/rules.md", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var rule in ParseRules(text, path, hash))
            {
                rules.Add(rule);
                documents.Add(ToSearchDocument(rule.Id, "rule", rule.Status, rule.Text, rule.Text, path, hash));
            }
        }

        if (path.Equals("docs/memory/symbols.md", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var symbol in ParseSymbols(text, path, hash))
            {
                symbols.Add(symbol);
                documents.Add(ToSearchDocument($"symbol.{symbol.Symbol}", "symbol", "current", symbol.Symbol, symbol.Symbol, path, hash));
            }
        }

        if (path.Equals("docs/memory/tests.md", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var symbolEvent in ParseTestSymbolReferences(text, path, hash))
            {
                events.Add(symbolEvent);
                documents.Add(ToSearchDocument(symbolEvent.Id, "event", "current", symbolEvent.Text, symbolEvent.Text, path, hash));
            }
        }

        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && IsExchangeAdapterImpactFile(path, text))
        {
            var relation = new RelationRecord(
                $"relation.exchange-adapter.{Slug(path)}",
                "module.infrastructure.exchange-adapter",
                "touches",
                path,
                "exchange adapter touched modules",
                path,
                hash);
            relations.Add(relation);
            documents.Add(ToSearchDocument(relation.Id, "relation", "current", "Exchange adapter touched modules", relation.Text, path, hash));
        }
    }

    private static IEnumerable<RuleRecord> ParseRules(string text, string path, string hash)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line[2..].Split('|').Select(part => part.Trim()).ToArray();
            if (parts.Length < 4)
            {
                continue;
            }

            yield return new RuleRecord(parts[0], parts[1], string.IsNullOrWhiteSpace(parts[2]) ? null : parts[2], parts[3], path, hash);
        }
    }

    private static IEnumerable<SymbolRecord> ParseSymbols(string text, string path, string hash)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var symbol = rawLine.Trim().TrimStart('-').Trim();
            if (Regex.IsMatch(symbol, "^[A-Z0-9]{3,20}$"))
            {
                yield return new SymbolRecord(symbol, path, hash);
            }
        }
    }

    private static IEnumerable<EventRecord> ParseTestSymbolReferences(string text, string path, string hash)
    {
        foreach (Match match in Regex.Matches(text, "requires_symbol=([A-Z0-9]{3,20})", RegexOptions.IgnoreCase))
        {
            var symbol = match.Groups[1].Value.ToUpperInvariant();
            yield return new EventRecord($"event.test-symbol-reference.{symbol}", "test_symbol_reference", symbol, match.Value, path, hash);
        }
    }

    private static void AddChunks(List<SearchDocument> documents, string path, string hash, string text)
    {
        const int chunkSize = 4000;
        for (var offset = 0; offset < text.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, text.Length - offset);
            var chunk = text.Substring(offset, length);
            var ordinal = offset / chunkSize;
            documents.Add(ToSearchDocument($"chunk.{Slug(path)}.{ordinal}", "chunk", "current", path, chunk, path, hash));
        }
    }

    private bool ShouldIndexFile(FileInfo file)
    {
        var relativePath = GetRelativePath(file.FullName);
        return ShouldIndexRelativePath(relativePath);
    }

    internal static bool ShouldIndexRelativePath(string relativePath)
    {
        relativePath = relativePath.Replace('\\', '/');
        foreach (var prefix in ExcludedPrefixes)
        {
            if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var segments = relativePath.Split('/');
        if (segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("publish", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var fileName = Path.GetFileName(relativePath);
        var extension = Path.GetExtension(relativePath);

        if (fileName.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("api-key", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("apikey", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("token", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("local-proxy", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("shadowsocks", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AllowedExtensions.Contains(extension);
    }

    private static bool IsStructuredMemoryFile(string path)
    {
        return path.Equals("docs/memory/rules.md", StringComparison.OrdinalIgnoreCase)
            || path.Equals("docs/memory/symbols.md", StringComparison.OrdinalIgnoreCase)
            || path.Equals("docs/memory/tests.md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExchangeAdapterImpactFile(string path, string text)
    {
        return path.StartsWith("CryptoIndicatorApp.Infrastructure/Binance/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("ExchangeAdapter", StringComparison.OrdinalIgnoreCase)
            || text.Contains("exchange adapter", StringComparison.OrdinalIgnoreCase);
    }

    private string GetRelativePath(string path)
    {
        var relativePath = Path.GetRelativePath(_projectRoot, path);
        return relativePath.Replace('\\', '/');
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ExtractTitle(string text, string fallback)
    {
        var title = text.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(title) ? fallback : title[2..].Trim();
    }

    private static SearchDocument ToSearchDocument(
        string id,
        string type,
        string status,
        string title,
        string body,
        string sourcePath,
        string sourceHash)
    {
        return new SearchDocument(id, type, NormalizeStatus(status), title, body, sourcePath, sourceHash, 0.95, null, null);
    }

    private static string NormalizeStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "current" => "current",
            "proposed" => "proposed",
            "superseded" => "superseded",
            "failed" => "failed",
            "historical" => "superseded",
            _ => "current"
        };
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
