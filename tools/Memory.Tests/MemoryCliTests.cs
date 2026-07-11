using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
        Assert.Equal("lancedb-fastembed-local-candidate", root.GetProperty("semantic_sidecar").GetString());
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
    public void RefreshFromCommitIndexesGitTreeMetadataAndIgnoresWorkingTreeChanges()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.WriteStandardMemoryFiles();
        fixture.Write(
            "docs/formulas.md",
            "# Formulas\n\n"
            + "The committedonlyphrase OFI formula phrase is the current formula_version.\n"
            + "Owner: docs/formulas.md\n");
        fixture.InitializeGitRepository();

        var head = fixture.RunGit("rev-parse", "HEAD").Trim();
        var tree = fixture.RunGit("rev-parse", "HEAD^{tree}").Trim();

        fixture.Write(
            "docs/formulas.md",
            "# Formulas\n\n"
            + "The uncommittedonlyphrase OFI formula phrase must not enter commit-addressed memory.\n"
            + "Owner: docs/formulas.md\n");

        using var refresh = fixture.RunMemoryCli("refresh-from-commit", "--commit", "HEAD", "--json");
        Assert.Equal(0, refresh.ExitCode);
        using var refreshJson = JsonDocument.Parse(refresh.StandardOutput);
        var refreshRoot = refreshJson.RootElement;

        Assert.Equal("git-commit", refreshRoot.GetProperty("refresh_source").GetString());
        Assert.Equal(head, refreshRoot.GetProperty("commit_sha").GetString());
        Assert.Equal(tree, refreshRoot.GetProperty("tree_sha").GetString());
        Assert.False(string.IsNullOrWhiteSpace(refreshRoot.GetProperty("indexed_at").GetString()));
        Assert.True(refreshRoot.GetProperty("source_blob_sha_count").GetInt32() > 0);

        using var committed = fixture.RunMemoryCli("search", "--query", "committedonlyphrase", "--json");
        Assert.Equal(0, committed.ExitCode);
        using var committedJson = JsonDocument.Parse(committed.StandardOutput);
        Assert.NotEmpty(committedJson.RootElement.GetProperty("results").EnumerateArray());

        using var uncommitted = fixture.RunMemoryCli("search", "--query", "uncommittedonlyphrase", "--json");
        Assert.Equal(0, uncommitted.ExitCode);
        using var uncommittedJson = JsonDocument.Parse(uncommitted.StandardOutput);
        Assert.Empty(uncommittedJson.RootElement.GetProperty("results").EnumerateArray());

        using var status = fixture.RunMemoryCli("status", "--json");
        Assert.Equal(0, status.ExitCode);
        using var statusJson = JsonDocument.Parse(status.StandardOutput);
        var statusRoot = statusJson.RootElement;
        Assert.Equal(head, statusRoot.GetProperty("head").GetString());
        Assert.Equal(head, statusRoot.GetProperty("indexed_commit").GetString());
        Assert.False(statusRoot.GetProperty("needs_refresh").GetBoolean());
        Assert.True(statusRoot.GetProperty("working_tree_dirty").GetBoolean());
    }

    [Fact]
    public void StatusReportsNeedsRefreshWhenMarkerExistsAndRefreshFromCommitClearsMarker()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.WriteStandardMemoryFiles();
        fixture.InitializeGitRepository();

        using var refresh = fixture.RunMemoryCli("refresh-from-commit", "--commit", "HEAD", "--json");
        Assert.Equal(0, refresh.ExitCode);

        fixture.Write("docs/memory/generated/memory-needs-refresh.marker.json", "{\"reason\":\"post-commit\"}\n");

        using var staleStatus = fixture.RunMemoryCli("status", "--json");
        Assert.Equal(0, staleStatus.ExitCode);
        using var staleJson = JsonDocument.Parse(staleStatus.StandardOutput);
        Assert.True(staleJson.RootElement.GetProperty("marker_exists").GetBoolean());
        Assert.True(staleJson.RootElement.GetProperty("needs_refresh").GetBoolean());

        using var secondRefresh = fixture.RunMemoryCli("refresh-from-commit", "--commit", "HEAD", "--json");
        Assert.Equal(0, secondRefresh.ExitCode);

        using var freshStatus = fixture.RunMemoryCli("status", "--json");
        Assert.Equal(0, freshStatus.ExitCode);
        using var freshJson = JsonDocument.Parse(freshStatus.StandardOutput);
        Assert.False(freshJson.RootElement.GetProperty("marker_exists").GetBoolean());
        Assert.False(freshJson.RootElement.GetProperty("needs_refresh").GetBoolean());
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
    public void RefreshExtractsCSharpSymbolsRelationsEventsTodosAndExperiments()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.WriteStandardMemoryFiles();
        fixture.Write(
            "CryptoIndicatorApp.Infrastructure/Binance/DepthTracker.cs",
            """
            namespace CryptoIndicatorApp.Infrastructure.Binance;

            public sealed class DepthTracker
            {
                // TODO memorycodedepthtodo: tighten depth gap telemetry.
                public int CalculateDepthSkew(int bid, int ask) => bid - ask;
            }

            // memory: experiment_outcome=failed | memorycodedepth_experiment rejected raw stream import
            """);
        fixture.Write(
            "CryptoIndicatorApp.Infrastructure.Tests/DepthTrackerTests.cs",
            """
            namespace CryptoIndicatorApp.Infrastructure.Tests;

            public sealed class DepthTrackerTests
            {
                // memory: requires_symbol=CryptoIndicatorApp.Infrastructure.Binance.DepthTracker
                public void Helper_before_fact_should_not_be_test_event()
                {
                }

                [Fact]
                public void Index_depth_pipeline_records_symbol_reference()
                {
                }
            }
            """);
        fixture.Refresh();

        AssertFirstSearchHit(fixture, "DepthTracker", "symbol", "CryptoIndicatorApp.Infrastructure/Binance/DepthTracker.cs");
        AssertSearchContains(fixture, "CalculateDepthSkew", "symbol", "CryptoIndicatorApp.Infrastructure/Binance/DepthTracker.cs");
        AssertSearchContains(fixture, "owns DepthTracker", "relation", "CryptoIndicatorApp.Infrastructure/Binance/DepthTracker.cs");
        AssertSearchContains(fixture, "Index_depth_pipeline_records_symbol_reference", "event", "CryptoIndicatorApp.Infrastructure.Tests/DepthTrackerTests.cs");
        AssertSearchDoesNotContain(fixture, "Helper_before_fact_should_not_be_test_event", "event", "CryptoIndicatorApp.Infrastructure.Tests/DepthTrackerTests.cs");
        AssertSearchContains(fixture, "memorycodedepthtodo", "todo", "CryptoIndicatorApp.Infrastructure/Binance/DepthTracker.cs");
        AssertSearchContains(fixture, "memorycodedepth_experiment", "experiment", "CryptoIndicatorApp.Infrastructure/Binance/DepthTracker.cs");
    }

    [Fact]
    public void StaleCheckUsesCSharpSymbolsForCodeTestReferences()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.WriteStandardMemoryFiles();
        fixture.Write(
            "CryptoIndicatorApp.Infrastructure/Binance/DepthTracker.cs",
            """
            namespace CryptoIndicatorApp.Infrastructure.Binance;

            public sealed class DepthTracker
            {
            }
            """);
        fixture.Write(
            "CryptoIndicatorApp.Infrastructure.Tests/DepthTrackerTests.cs",
            """
            namespace CryptoIndicatorApp.Infrastructure.Tests;

            public sealed class DepthTrackerTests
            {
                // memory: requires_symbol=CryptoIndicatorApp.Infrastructure.Binance.DepthTracker
                // memory: requires_symbol=CryptoIndicatorApp.Infrastructure.Binance.MissingDepthTracker
                [Fact]
                public void References_depth_tracker_symbols()
                {
                }
            }
            """);
        fixture.Refresh();

        using var stale = fixture.RunMemoryCli("stale-check", "--json");
        Assert.Equal(2, stale.ExitCode);
        using var document = JsonDocument.Parse(stale.StandardOutput);
        var issues = document.RootElement.GetProperty("issues").EnumerateArray().ToArray();

        Assert.Contains(
            issues,
            issue => issue.GetProperty("code").GetString() == "unknown_symbol_reference"
                && issue.GetProperty("id").GetString()!.Contains("missingdepthtracker", StringComparison.OrdinalIgnoreCase)
                && issue.GetProperty("source_path").GetString() == "CryptoIndicatorApp.Infrastructure.Tests/DepthTrackerTests.cs");
        Assert.DoesNotContain(
            issues,
            issue => issue.GetProperty("code").GetString() == "unknown_symbol_reference"
                && issue.GetProperty("id").GetString()!.Contains("depthtracker", StringComparison.OrdinalIgnoreCase)
                && !issue.GetProperty("id").GetString()!.Contains("missingdepthtracker", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RefreshIgnoresCSharpSyntaxAndMemoryMarkersInsideStringLiterals()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.WriteStandardMemoryFiles();
        fixture.Write(
            "CryptoIndicatorApp.Infrastructure.Tests/StringFixtureTests.cs",
            """"
            namespace CryptoIndicatorApp.Infrastructure.Tests;

            public sealed class StringFixtureTests
            {
                private const string FixtureSource = """
                    namespace CryptoIndicatorApp.Infrastructure.Binance;

                    public sealed class GhostDepthTracker
                    {
                    }

                    // memory: requires_symbol=CryptoIndicatorApp.Infrastructure.Binance.MissingGhostDepthTracker
                    """;

                [Fact]
                public void Real_test_method_is_still_indexed()
                {
                }
            }
            """");
        fixture.Refresh();

        AssertSearchContains(fixture, "Real_test_method_is_still_indexed", "event", "CryptoIndicatorApp.Infrastructure.Tests/StringFixtureTests.cs");
        AssertSearchDoesNotContain(fixture, "GhostDepthTracker", "symbol", "CryptoIndicatorApp.Infrastructure.Tests/StringFixtureTests.cs");
        AssertSearchDoesNotContain(fixture, "MissingGhostDepthTracker", "event", "CryptoIndicatorApp.Infrastructure.Tests/StringFixtureTests.cs");

        using var stale = fixture.RunMemoryCli("stale-check", "--json");
        Assert.Equal(0, stale.ExitCode);
        Assert.DoesNotContain("MissingGhostDepthTracker", stale.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshSupportsOverloadedCSharpMethodsWithDistinctSymbolIds()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.WriteStandardMemoryFiles();
        fixture.Write(
            "CryptoIndicatorApp.Desktop/Rendering/OverloadedChartGeometryBuilder.cs",
            """
            namespace CryptoIndicatorApp.Desktop.Rendering;

            public static class OverloadedChartGeometryBuilder
            {
                public static int BuildPoints(int width)
                {
                    return BuildPoints(width, 1);
                }

                public static int BuildPoints(int width, int height)
                {
                    return width + height;
                }
            }
            """);
        fixture.Refresh();

        using var result = fixture.RunMemoryCli("search", "--query", "OverloadedChartGeometryBuilder BuildPoints", "--json");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var symbols = document.RootElement.GetProperty("results")
            .EnumerateArray()
            .Where(hit => hit.GetProperty("type").GetString() == "symbol")
            .Select(hit => hit.GetProperty("id").GetString())
            .Where(id => id is not null && id.Contains("buildpoints", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Contains(symbols, id => id!.EndsWith("buildpoints-int", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, id => id!.EndsWith("buildpoints-int-int", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RefreshScopesNestedCSharpTypesByContainingType()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.WriteStandardMemoryFiles();
        fixture.Write(
            "CryptoIndicatorApp.Infrastructure.Tests/FirstFixtureTests.cs",
            """
            namespace CryptoIndicatorApp.Infrastructure.Tests;

            public sealed class FirstFixtureTests
            {
                private sealed class TemporaryProjectFixture
                {
                }
            }
            """);
        fixture.Write(
            "CryptoIndicatorApp.Infrastructure.Tests/SecondFixtureTests.cs",
            """
            namespace CryptoIndicatorApp.Infrastructure.Tests;

            public sealed class SecondFixtureTests
            {
                private sealed class TemporaryProjectFixture
                {
                }
            }
            """);
        fixture.Refresh();

        using var result = fixture.RunMemoryCli("search", "--query", "TemporaryProjectFixture", "--json");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var symbols = document.RootElement.GetProperty("results")
            .EnumerateArray()
            .Where(hit => hit.GetProperty("type").GetString() == "symbol")
            .Select(hit => hit.GetProperty("id").GetString())
            .Where(id => id is not null && id.Contains("temporaryprojectfixture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Contains(symbols, id => id!.Contains("firstfixturetests-temporaryprojectfixture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(symbols, id => id!.Contains("secondfixturetests-temporaryprojectfixture", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void RetainImportUsesGitCommitTreeAndSearchFindsImportedLocalItem()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        var committedText = "# Clean Retain Source\n\ncommittedonlyretain phrase for local sqlite import.\n";
        fixture.Write("docs/memory/clean-retain.md", committedText);
        fixture.InitializeGitRepository();

        var head = fixture.RunGit("rev-parse", "HEAD").Trim();
        var tree = fixture.RunGit("rev-parse", "HEAD^{tree}").Trim();
        var reportPath = fixture.WriteCuratedRetainReport(
            new MemoryProjectFixture.RetainReportFile("docs/memory/clean-retain.md", HashText(committedText), committedText.Length, "candidate", 0));

        fixture.Write("docs/memory/clean-retain.md", "# Clean Retain Source\n\ndirtyonlyretain phrase must not import.\n");

        using var import = fixture.RunMemoryCli("retain-import", "--input-report", reportPath, "--commit", "HEAD", "--json");
        Assert.Equal(0, import.ExitCode);
        using var importJson = JsonDocument.Parse(import.StandardOutput);
        var importRoot = importJson.RootElement;

        Assert.Equal("imported", importRoot.GetProperty("status").GetString());
        Assert.Equal(1, importRoot.GetProperty("imported_count").GetInt32());
        Assert.Equal(head, importRoot.GetProperty("commit_sha").GetString());
        Assert.Equal(tree, importRoot.GetProperty("tree_sha").GetString());
        Assert.False(importRoot.GetProperty("external_retain_enabled").GetBoolean());
        Assert.False(importRoot.GetProperty("calls_codex_retain").GetBoolean());
        Assert.False(importRoot.GetProperty("calls_hindsight").GetBoolean());
        Assert.False(importRoot.GetProperty("installs_hooks").GetBoolean());
        Assert.False(importRoot.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(importRoot.GetProperty("rebuilds_memory").GetBoolean());
        Assert.Equal("docs/memory/clean-retain.md", importRoot.GetProperty("items")[0].GetProperty("source_path").GetString());

        using var committedSearch = fixture.RunMemoryCli("retain-search", "--query", "committedonlyretain", "--json");
        Assert.Equal(0, committedSearch.ExitCode);
        using var committedSearchJson = JsonDocument.Parse(committedSearch.StandardOutput);
        var committedResults = committedSearchJson.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.Single(committedResults);
        Assert.Equal("docs/memory/clean-retain.md", committedResults[0].GetProperty("source_path").GetString());
        Assert.Equal(head, committedResults[0].GetProperty("commit_sha").GetString());

        using var dirtySearch = fixture.RunMemoryCli("retain-search", "--query", "dirtyonlyretain", "--json");
        Assert.Equal(0, dirtySearch.ExitCode);
        using var dirtySearchJson = JsonDocument.Parse(dirtySearch.StandardOutput);
        Assert.Empty(dirtySearchJson.RootElement.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public void RetainImportBlocksDenylistAndRedactionReviewSources()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        var reviewText = "# Review Source\n\nThis text still needs redaction review.\n";
        var rawText = "{\"raw\":true}\n";
        fixture.Write("docs/memory/review-required.md", reviewText);
        fixture.Write("recordings/live.jsonl", rawText);
        fixture.InitializeGitRepository();
        var reportPath = fixture.WriteCuratedRetainReport(
            new MemoryProjectFixture.RetainReportFile("docs/memory/review-required.md", HashText(reviewText), reviewText.Length, "review_required", 1),
            new MemoryProjectFixture.RetainReportFile("recordings/live.jsonl", HashText(rawText), rawText.Length, "candidate", 0));

        using var import = fixture.RunMemoryCli("retain-import", "--input-report", reportPath, "--commit", "HEAD", "--json");
        Assert.Equal(2, import.ExitCode);
        using var importJson = JsonDocument.Parse(import.StandardOutput);
        var root = importJson.RootElement;

        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("imported_count").GetInt32());
        Assert.Contains("redaction_review_required", root.GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()));
        Assert.Contains("denied_sources_in_input_report", root.GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()));
        Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("calls_codex_retain").GetBoolean());
        Assert.False(root.GetProperty("calls_hindsight").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());
    }

    [Fact]
    public void RetainExportDeleteLifecycleProvesImportedItemCanBeRemoved()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        var retainedText = "# Lifecycle Retain Source\n\nlifecycleonlyretain phrase for export and delete proof.\n";
        fixture.Write("docs/memory/lifecycle-retain.md", retainedText);
        fixture.InitializeGitRepository();
        var reportPath = fixture.WriteCuratedRetainReport(
            new MemoryProjectFixture.RetainReportFile("docs/memory/lifecycle-retain.md", HashText(retainedText), retainedText.Length, "candidate", 0));

        using var import = fixture.RunMemoryCli("retain-import", "--input-report", reportPath, "--commit", "HEAD", "--json");
        Assert.Equal(0, import.ExitCode);

        using var searchBeforeDelete = fixture.RunMemoryCli("retain-search", "--query", "lifecycleonlyretain", "--json");
        Assert.Equal(0, searchBeforeDelete.ExitCode);
        using (var searchBeforeDeleteJson = JsonDocument.Parse(searchBeforeDelete.StandardOutput))
        {
            Assert.Single(searchBeforeDeleteJson.RootElement.GetProperty("results").EnumerateArray());
        }

        var exportPath = Path.Combine(fixture.Root, "docs", "memory", "generated", "curated-retain-export-report.json");
        using var export = fixture.RunMemoryCli("retain-export", "--output", exportPath, "--json");
        Assert.Equal(0, export.ExitCode);
        Assert.True(File.Exists(exportPath), $"Missing export report: {exportPath}");
        using (var exportJson = JsonDocument.Parse(export.StandardOutput))
        {
            var root = exportJson.RootElement;
            Assert.Equal("exported", root.GetProperty("status").GetString());
            Assert.Equal(1, root.GetProperty("exported_count").GetInt32());
            Assert.True(root.GetProperty("source_content_included").GetBoolean());
            Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
            Assert.False(root.GetProperty("calls_codex_retain").GetBoolean());
            Assert.False(root.GetProperty("calls_hindsight").GetBoolean());
            Assert.False(root.GetProperty("installs_hooks").GetBoolean());
            Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
            var item = root.GetProperty("items")[0];
            Assert.Equal("docs/memory/lifecycle-retain.md", item.GetProperty("source_path").GetString());
            Assert.Contains("lifecycleonlyretain", item.GetProperty("text").GetString(), StringComparison.Ordinal);
        }

        using var delete = fixture.RunMemoryCli("retain-delete", "--source-path", "docs/memory/lifecycle-retain.md", "--json");
        Assert.Equal(0, delete.ExitCode);
        using (var deleteJson = JsonDocument.Parse(delete.StandardOutput))
        {
            var root = deleteJson.RootElement;
            Assert.Equal("deleted", root.GetProperty("status").GetString());
            Assert.Equal(1, root.GetProperty("deleted_count").GetInt32());
            Assert.False(root.GetProperty("removes_files").GetBoolean());
            Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
            Assert.False(root.GetProperty("calls_codex_retain").GetBoolean());
            Assert.False(root.GetProperty("calls_hindsight").GetBoolean());
            Assert.False(root.GetProperty("installs_hooks").GetBoolean());
            Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        }

        Assert.True(File.Exists(Path.Combine(fixture.Root, "docs", "memory", "lifecycle-retain.md")), "Retain delete must not remove source files.");

        using var searchAfterDelete = fixture.RunMemoryCli("retain-search", "--query", "lifecycleonlyretain", "--json");
        Assert.Equal(0, searchAfterDelete.ExitCode);
        using var searchAfterDeleteJson = JsonDocument.Parse(searchAfterDelete.StandardOutput);
        Assert.Empty(searchAfterDeleteJson.RootElement.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public void RetainImportUsesReviewedRedactedTextAndLifecycleDeletesIt()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        var sourceText = "# Reviewed Retain Source\n\nOPENAI_API_KEY=sk-testtoken1234567890 originalonlyretain phrase must never be retained.\n";
        var redactedText = "# Reviewed Retain Source\n\n[REDACTED:secret_reference]\n\nredactedonlyretain phrase is safe for local sqlite retain.\n";
        fixture.Write("docs/memory/reviewed-retain.md", sourceText);
        fixture.InitializeGitRepository();
        var reportPath = fixture.WriteCuratedRetainReport(
            new MemoryProjectFixture.RetainReportFile(
                "docs/memory/reviewed-retain.md",
                HashText(sourceText),
                sourceText.Length,
                "redacted",
                0,
                redactedText));

        using var import = fixture.RunMemoryCli("retain-import", "--input-report", reportPath, "--commit", "HEAD", "--json");
        Assert.Equal(0, import.ExitCode);
        using (var importJson = JsonDocument.Parse(import.StandardOutput))
        {
            var root = importJson.RootElement;
            Assert.Equal("imported", root.GetProperty("status").GetString());
            Assert.Equal(1, root.GetProperty("imported_count").GetInt32());
            Assert.Equal("redacted", root.GetProperty("items")[0].GetProperty("redaction_status").GetString());
            Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
            Assert.False(root.GetProperty("calls_codex_retain").GetBoolean());
            Assert.False(root.GetProperty("calls_hindsight").GetBoolean());
        }

        using var redactedSearch = fixture.RunMemoryCli("retain-search", "--query", "redactedonlyretain", "--json");
        Assert.Equal(0, redactedSearch.ExitCode);
        using (var redactedSearchJson = JsonDocument.Parse(redactedSearch.StandardOutput))
        {
            Assert.Single(redactedSearchJson.RootElement.GetProperty("results").EnumerateArray());
        }

        using var rawSearch = fixture.RunMemoryCli("retain-search", "--query", "originalonlyretain", "--json");
        Assert.Equal(0, rawSearch.ExitCode);
        using (var rawSearchJson = JsonDocument.Parse(rawSearch.StandardOutput))
        {
            Assert.Empty(rawSearchJson.RootElement.GetProperty("results").EnumerateArray());
        }

        var exportPath = Path.Combine(fixture.Root, "docs", "memory", "generated", "curated-retain-export-report.json");
        using var export = fixture.RunMemoryCli("retain-export", "--output", exportPath, "--json");
        Assert.Equal(0, export.ExitCode);
        using (var exportJson = JsonDocument.Parse(export.StandardOutput))
        {
            var text = exportJson.RootElement.GetProperty("items")[0].GetProperty("text").GetString();
            Assert.Contains("redactedonlyretain", text, StringComparison.Ordinal);
            Assert.DoesNotContain("originalonlyretain", text, StringComparison.Ordinal);
            Assert.DoesNotContain("OPENAI_API_KEY", text, StringComparison.Ordinal);
        }

        using var delete = fixture.RunMemoryCli("retain-delete", "--source-path", "docs/memory/reviewed-retain.md", "--json");
        Assert.Equal(0, delete.ExitCode);
        Assert.True(File.Exists(Path.Combine(fixture.Root, "docs", "memory", "reviewed-retain.md")), "Retain delete must not remove source files.");

        using var searchAfterDelete = fixture.RunMemoryCli("retain-search", "--query", "redactedonlyretain", "--json");
        Assert.Equal(0, searchAfterDelete.ExitCode);
        using var searchAfterDeleteJson = JsonDocument.Parse(searchAfterDelete.StandardOutput);
        Assert.Empty(searchAfterDeleteJson.RootElement.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public void RetainImportRejectsBlockedInputReportEvenWhenFileMetadataLooksSafe()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        const string sourceText = "# Blocked retain source\n\nblockedreportonly phrase must never be retained.\n";
        fixture.Write("docs/memory/blocked-retain.md", sourceText);
        fixture.InitializeGitRepository();
        var reportPath = fixture.WriteCuratedRetainReport(
            new MemoryProjectFixture.RetainReportFile(
                "docs/memory/blocked-retain.md",
                HashText(sourceText),
                sourceText.Length,
                "candidate",
                0));
        var reportText = File.ReadAllText(reportPath)
            .Replace(
                "\"status\":\"ready_for_review\"",
                "\"status\":\"blocked\",\"blocking_reasons\":[\"invalid_finding_line\"]",
                StringComparison.Ordinal);
        File.WriteAllText(reportPath, reportText);

        using var import = fixture.RunMemoryCli("retain-import", "--input-report", reportPath, "--commit", "HEAD", "--json");
        Assert.Equal(2, import.ExitCode);
        using (var importJson = JsonDocument.Parse(import.StandardOutput))
        {
            var root = importJson.RootElement;
            Assert.Equal("blocked", root.GetProperty("status").GetString());
            Assert.Equal(0, root.GetProperty("imported_count").GetInt32());
            Assert.Contains(
                root.GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()),
                reason => reason == "input_report_blocked");
        }

        using var search = fixture.RunMemoryCli("retain-search", "--query", "blockedreportonly", "--json");
        Assert.Equal(0, search.ExitCode);
        using var searchJson = JsonDocument.Parse(search.StandardOutput);
        Assert.Empty(searchJson.RootElement.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public void RetainImportRejectsUnknownInputReportContract()
    {
        using var fixture = MemoryProjectFixture.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        const string sourceText = "# Unknown retain report\n\nunknownreportonly phrase must never be retained.\n";
        fixture.Write("docs/memory/unknown-retain.md", sourceText);
        fixture.InitializeGitRepository();
        var reportPath = fixture.WriteCuratedRetainReport(
            new MemoryProjectFixture.RetainReportFile(
                "docs/memory/unknown-retain.md",
                HashText(sourceText),
                sourceText.Length,
                "candidate",
                0));
        var reportText = File.ReadAllText(reportPath)
            .Replace("\"status\":\"ready_for_review\"", "\"status\":\"unknown\"", StringComparison.Ordinal);
        File.WriteAllText(reportPath, reportText);

        using var import = fixture.RunMemoryCli("retain-import", "--input-report", reportPath, "--commit", "HEAD", "--json");
        Assert.Equal(2, import.ExitCode);
        using var importJson = JsonDocument.Parse(import.StandardOutput);
        var root = importJson.RootElement;
        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("imported_count").GetInt32());
        Assert.Contains(
            root.GetProperty("blocking_reasons").EnumerateArray().Select(reason => reason.GetString()),
            reason => reason == "unsupported_input_report_contract");
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
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            Assert.Fail("memory CLI timed out.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        return new CliResult(process.ExitCode, stdout, stderr);
    }

    private static void AssertFirstSearchHit(MemoryProjectFixture fixture, string query, string expectedType, string expectedSourcePath)
    {
        using var result = fixture.RunMemoryCli("search", "--query", query, "--json");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var results = document.RootElement.GetProperty("results").EnumerateArray().ToArray();

        Assert.NotEmpty(results);
        Assert.Equal(expectedType, results[0].GetProperty("type").GetString());
        Assert.Equal(expectedSourcePath, results[0].GetProperty("source_path").GetString());
    }

    private static void AssertSearchContains(MemoryProjectFixture fixture, string query, string expectedType, string expectedSourcePath)
    {
        using var result = fixture.RunMemoryCli("search", "--query", query, "--json");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var results = document.RootElement.GetProperty("results").EnumerateArray().ToArray();

        Assert.Contains(
            results,
            hit => hit.GetProperty("type").GetString() == expectedType
                && hit.GetProperty("source_path").GetString() == expectedSourcePath);
    }

    private static void AssertSearchDoesNotContain(MemoryProjectFixture fixture, string query, string unexpectedType, string unexpectedSourcePath)
    {
        using var result = fixture.RunMemoryCli("search", "--query", query, "--json");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var results = document.RootElement.GetProperty("results").EnumerateArray().ToArray();

        Assert.DoesNotContain(
            results,
            hit => hit.GetProperty("type").GetString() == unexpectedType
                && hit.GetProperty("source_path").GetString() == unexpectedSourcePath);
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

    private static string HashText(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
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

        public void InitializeGitRepository()
        {
            RunGit("init");
            RunGit("config", "user.name", "Memory CLI Test");
            RunGit("config", "user.email", "memory-cli-test@example.invalid");
            RunGit("add", ".");
            RunGit("commit", "-m", "initial memory test fixture");
        }

        public string RunGit(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GitPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Root,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            Assert.True(process!.WaitForExit(TimeSpan.FromSeconds(30)), "git command timed out.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed with {process.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            return stdout;
        }

        public string WriteCuratedRetainReport(params RetainReportFile[] files)
        {
            var reportPath = Path.Combine(Root, "docs", "memory", "generated", "curated-retain-dry-run-report.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            var isRedactedSubset = files.Any(file => file.RedactionStatus.Equals("redacted", StringComparison.OrdinalIgnoreCase));
            var requiresReview = files.Any(file => file.FindingCount > 0 || file.RedactionStatus.Equals("review_required", StringComparison.OrdinalIgnoreCase));
            var report = new
            {
                schema_version = isRedactedSubset ? 2 : 1,
                generator = "test",
                mode = isRedactedSubset ? "redacted-subset" : "dry-run",
                status = requiresReview ? "review_required" : isRedactedSubset ? "ready_for_import" : "ready_for_review",
                blocking_reasons = Array.Empty<string>(),
                external_retain_enabled = false,
                codex_auto_retain_enabled = false,
                cloud_enabled = false,
                calls_hindsight = false,
                calls_codex_retain = false,
                installs_hooks = false,
                runs_refresh_all = false,
                rebuilds_memory = false,
                imports_denylist = false,
                writes_report_only = true,
                files = files.Select(file => new
                {
                    path = file.Path,
                    hash = file.Hash,
                    size_bytes = file.SizeBytes,
                    redaction_status = file.RedactionStatus,
                    finding_count = file.FindingCount,
                    original_finding_count = file.OriginalFindingCount,
                    redacted_text = file.RedactedText,
                    redacted_hash = string.IsNullOrEmpty(file.RedactedText) ? null : HashText(file.RedactedText),
                }).ToArray(),
                findings = files
                    .Where(file => file.FindingCount > 0)
                    .Select(file => new
                    {
                        type = "test_redaction_review",
                        severity = "review",
                        policy_reference = false,
                        source_path = file.Path,
                        line = 1,
                        rule = "test finding",
                    })
                    .ToArray(),
            };
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report));
            return reportPath;
        }

        public sealed record RetainReportFile(
            string Path,
            string Hash,
            long SizeBytes,
            string RedactionStatus,
            int FindingCount,
            string? RedactedText = null,
            int OriginalFindingCount = 0);

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
                ClearReadOnlyAttributes(Root);
                DeleteDirectoryWithRetry(Root);
            }
        }

        private static void ClearReadOnlyAttributes(string root)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(directory, FileAttributes.Directory);
            }
        }

        private static void DeleteDirectoryWithRetry(string root)
        {
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(200);
                }
                catch (UnauthorizedAccessException) when (attempt < 5)
                {
                    Thread.Sleep(200);
                }
            }

            Directory.Delete(root, recursive: true);
        }
    }

    private static string GitPath => File.Exists(@"C:\Program Files\Git\cmd\git.exe")
        ? @"C:\Program Files\Git\cmd\git.exe"
        : "git";
}
