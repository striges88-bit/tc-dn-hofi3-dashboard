using System.Diagnostics;
using System.Text.Json;

namespace CryptoIndicatorApp.Memory.Tests;

public sealed class MemoryCliTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string DotnetPath = File.Exists(Path.Combine(RepositoryRoot, ".dotnet", "dotnet.exe"))
        ? Path.Combine(RepositoryRoot, ".dotnet", "dotnet.exe")
        : "dotnet";

    [Fact]
    public void RefreshBuildsSqliteFtsStoreWithoutIndexingDeniedSources()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.WriteStandardMemoryFiles();
        fixture.Write("recordings/live.jsonl", "{}\n");
        fixture.Write("docs/memory/generated/project-memory-index.json", "{}\n");
        fixture.Write(".hindsight/tc-dn-hofi3.env", "OPENAI_API_KEY=secret\n");
        fixture.Write("bin/Debug/net8.0/build-output.md", "# Build\n");

        using var refresh = RunMemoryCli(
            "refresh",
            "--project-root",
            fixture.Root,
            "--db",
            fixture.DatabasePath,
            "--json");

        Assert.Equal(0, refresh.ExitCode);
        Assert.True(File.Exists(fixture.DatabasePath));

        using var document = JsonDocument.Parse(refresh.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("sqlite-fts5", root.GetProperty("canonical_store").GetString());
        Assert.Equal("lancedb-active-local-spike", root.GetProperty("semantic_sidecar").GetString());
        Assert.Equal("historical-failed", root.GetProperty("hindsight_status").GetString());

        AssertJsonArrayContainsAll(
            root.GetProperty("tables"),
            "files",
            "symbols",
            "chunks",
            "rules",
            "adr",
            "formula_versions",
            "metrics",
            "experiments",
            "events",
            "relations",
            "sources",
            "todos",
            "search_documents",
            "search_documents_fts",
            "query_log");

        var indexedPaths = root.GetProperty("indexed_paths")
            .EnumerateArray()
            .Select(path => path.GetString()!)
            .ToArray();

        Assert.Contains("docs/formulas.md", indexedPaths);
        Assert.Contains("AGENTS.md", indexedPaths);
        Assert.DoesNotContain(indexedPaths, path => path.StartsWith("recordings/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(indexedPaths, path => path.StartsWith("docs/memory/generated/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(indexedPaths, path => path.StartsWith(".hindsight/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(indexedPaths, path => path.Contains("/bin/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SearchFindsCurrentFactsAndDoesNotReturnSupersededRules()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.WriteStandardMemoryFiles();
        fixture.Refresh();

        using var formula = fixture.RunMemoryCli("search", "--query", "actual OFI formula", "--json");
        Assert.Equal(0, formula.ExitCode);
        using var formulaJson = JsonDocument.Parse(formula.StandardOutput);
        var formulaResults = formulaJson.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.NotEmpty(formulaResults);
        Assert.Equal("formula_version", formulaResults[0].GetProperty("type").GetString());
        Assert.Equal("current", formulaResults[0].GetProperty("status").GetString());
        Assert.Equal("docs/formulas.md", formulaResults[0].GetProperty("source_path").GetString());

        using var funding = fixture.RunMemoryCli("search", "--query", "why funding-source changed", "--json");
        Assert.Equal(0, funding.ExitCode);
        using var fundingJson = JsonDocument.Parse(funding.StandardOutput);
        var fundingResults = fundingJson.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.NotEmpty(fundingResults);
        Assert.Equal("adr", fundingResults[0].GetProperty("type").GetString());
        Assert.Equal("docs/decisions/0003-funding-source.md", fundingResults[0].GetProperty("source_path").GetString());

        using var adapter = fixture.RunMemoryCli("search", "--query", "exchange adapter touched modules", "--json");
        Assert.Equal(0, adapter.ExitCode);
        using var adapterJson = JsonDocument.Parse(adapter.StandardOutput);
        Assert.Contains(
            adapterJson.RootElement.GetProperty("results").EnumerateArray(),
            result => result.GetProperty("source_path").GetString() == "CryptoIndicatorApp.Infrastructure/Binance/ExchangeAdapter.cs");

        using var superseded = fixture.RunMemoryCli("search", "--query", "legacy superseded-only phrase", "--json");
        Assert.Equal(0, superseded.ExitCode);
        using var supersededJson = JsonDocument.Parse(superseded.StandardOutput);
        Assert.Empty(supersededJson.RootElement.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public void ExplainUsesSqliteQueryPlanAndWritesQueryLog()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.WriteStandardMemoryFiles();
        fixture.Refresh();

        using var explain = fixture.RunMemoryCli("explain", "--query", "actual OFI formula", "--json");
        Assert.Equal(0, explain.ExitCode);
        using var document = JsonDocument.Parse(explain.StandardOutput);
        var root = document.RootElement;

        Assert.Equal("EXPLAIN QUERY PLAN", root.GetProperty("diagnostic").GetString());
        Assert.True(root.GetProperty("duration_ms").GetDecimal() >= 0);
        Assert.True(root.GetProperty("query_log_rows").GetInt32() >= 1);
        Assert.DoesNotContain("pg_stat_statements", explain.StandardOutput, StringComparison.OrdinalIgnoreCase);

        var planText = string.Join(
            "\n",
            root.GetProperty("explain_plan").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("search_documents_fts", planText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaleCheckReportsMissingSourcesFormulaOwnersRuleScopesAndUnknownSymbols()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.WriteStandardMemoryFiles(includeFormulaOwner: false);
        fixture.Write("docs/memory/rules.md", "- rule.scope-missing | current |  | active scope missing\n");
        fixture.Write("docs/memory/tests.md", "- test-symbol-reference | requires_symbol=MISSINGUSDT\n");
        fixture.Refresh();
        File.Delete(Path.Combine(fixture.Root, "docs", "decisions", "0003-funding-source.md"));

        using var stale = fixture.RunMemoryCli("stale-check", "--json");
        Assert.Equal(2, stale.ExitCode);
        using var document = JsonDocument.Parse(stale.StandardOutput);
        var issueCodes = document.RootElement.GetProperty("issues")
            .EnumerateArray()
            .Select(issue => issue.GetProperty("code").GetString())
            .ToArray();

        Assert.Contains("missing_source", issueCodes);
        Assert.Contains("formula_missing_owner", issueCodes);
        Assert.Contains("rule_missing_active_scope", issueCodes);
        Assert.Contains("unknown_symbol_reference", issueCodes);
    }

    private static CliResult RunMemoryCli(params string[] arguments)
    {
        var projectPath = Path.Combine(RepositoryRoot, "tools", "Memory", "CryptoIndicatorApp.Memory.csproj");
        var startInfo = new ProcessStartInfo
        {
            FileName = DotnetPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        Assert.True(process!.WaitForExit(TimeSpan.FromSeconds(120)), "memory CLI timed out.");
        return new CliResult(process.ExitCode, process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd());
    }

    private static void AssertJsonArrayContainsAll(JsonElement array, params string[] expectedValues)
    {
        var actualValues = array.EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expectedValue in expectedValues)
        {
            Assert.Contains(expectedValue, actualValues);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CryptoIndicatorApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError) : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class MemoryProjectFixture : IDisposable
    {
        private MemoryProjectFixture(string root)
        {
            Root = root;
            DatabasePath = Path.Combine(Root, "docs", "memory", "generated", "project-memory.sqlite");
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public static MemoryProjectFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "tc-dn-hofi3-memory-cli-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new MemoryProjectFixture(root);
        }

        public void WriteStandardMemoryFiles(bool includeFormulaOwner = true)
        {
            Write("CryptoIndicatorApp.sln", string.Empty);
            Write("AGENTS.md", "# Agents\n\nDo not use REST in the hot path.\n");
            Write("docs/memory/contract.md", "# Contract\n\nSQLite FTS5 is canonical local memory. Hindsight is historical failed.\n");
            Write(
                "docs/memory/README.md",
                "# Memory\n\n"
                + string.Join(' ', Enumerable.Repeat("actual OFI formula generic chunk", 20))
                + "\n");
            Write("docs/memory/symbols.md", "- BTCUSDT\n");
            Write(
                "docs/formulas.md",
                "# Formulas\n\n"
                + "The actual OFI formula is TC-DN-HOFI3 current formula_version.\n"
                + (includeFormulaOwner ? "Owner: docs/formulas.md\n" : string.Empty));
            Write(
                "docs/decisions/0003-funding-source.md",
                "# 0003: Funding Source Changed\n\n"
                + "Decision: funding-source changed because mark/funding data is slow context, not hot-path entry input.\n");
            Write(
                "docs/memory/rules.md",
                "- rule.current-hot-path | current | project | current hot path rule\n"
                + "- rule.legacy-superseded | superseded | project | legacy superseded-only phrase\n");
            Write(
                "CryptoIndicatorApp.Infrastructure/Binance/ExchangeAdapter.cs",
                "namespace CryptoIndicatorApp.Infrastructure.Binance;\n"
                + "public sealed class ExchangeAdapter { public string Role => \"exchange adapter touched modules\"; }\n");
        }

        public void Refresh()
        {
            using var refresh = RunMemoryCli("refresh", "--project-root", Root, "--db", DatabasePath, "--json");
            Assert.Equal(0, refresh.ExitCode);
        }

        public CliResult RunMemoryCli(params string[] arguments)
        {
            var args = new List<string>(arguments);
            if (!args.Contains("--project-root", StringComparer.Ordinal))
            {
                args.Add("--project-root");
                args.Add(Root);
            }

            if (!args.Contains("--db", StringComparer.Ordinal))
            {
                args.Add("--db");
                args.Add(DatabasePath);
            }

            return MemoryCliTests.RunMemoryCli(args.ToArray());
        }

        public void Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
