using System.Diagnostics;
using System.Text.Json;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class LanceDbSidecarSpikeTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void SidecarProbeWritesSafeGeneratedReportWithoutCloudOrAutoRefresh()
    {
        var scriptPath = Path.Combine(Root, "scripts", "lancedb-sidecar.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var reportPath = Path.Combine(Root, "docs", "memory", "generated", "lancedb-sidecar-report.json");
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ProjectRoot \"{Root}\" -Command probe -OutputPath \"{reportPath}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);
        Assert.True(process!.WaitForExit(TimeSpan.FromSeconds(30)), "lancedb-sidecar.ps1 probe timed out.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}\nSTDOUT:\n{output}\nSTDERR:\n{error}");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("scripts/lancedb-sidecar.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("local-python-embedded", root.GetProperty("mode").GetString());
        Assert.Equal("sqlite-fts5", root.GetProperty("source_store").GetString());
        Assert.Equal("docs/memory/generated/lancedb", root.GetProperty("lancedb_store_path").GetString());
        Assert.Equal("docs/memory/generated/lancedb-sidecar-report.json", root.GetProperty("eval_json_report_path").GetString());
        Assert.Equal("docs/memory/generated/lancedb-eval-report.md", root.GetProperty("eval_markdown_report_path").GetString());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("auto_commit_refresh_enabled").GetBoolean());
        Assert.False(root.GetProperty("direct_project_crawl_enabled").GetBoolean());
        Assert.False(root.GetProperty("import_executed").GetBoolean());
        Assert.False(root.GetProperty("commit_hook_installed").GetBoolean());
        Assert.Contains("docs/memory/generated/", File.ReadAllText(Path.Combine(Root, ".gitignore")), StringComparison.Ordinal);

        var commands = root.GetProperty("supported_commands").EnumerateArray()
            .Select(command => command.GetString()!)
            .ToArray();
        Assert.Contains("probe", commands);
        Assert.Contains("rebuild", commands);
        Assert.Contains("search", commands);
        Assert.Contains("explain", commands);
        Assert.Contains("cleanup", commands);
        Assert.Contains("eval", commands);
        Assert.Equal("fastembed", root.GetProperty("embedding_provider").GetString());
        Assert.Equal("sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2", root.GetProperty("embedding_model").GetString());
        Assert.Equal("fastembed==0.8.0", root.GetProperty("embedding_package_pin").GetString());
        Assert.Equal("mean-pooling", root.GetProperty("embedding_pooling_baseline").GetString());
        Assert.Equal("accepted-if-eval-passes", root.GetProperty("embedding_baseline_status").GetString());
        Assert.Equal("lancedb-eval-9-of-9", root.GetProperty("embedding_baseline_eval_gate").GetString());
    }

    [Fact]
    public void SidecarDocsRequireSqliteExportAndCleanRebuildBeforeAutomation()
    {
        var spike = ReadText("docs/memory/lancedb-spike.md");
        var contract = ReadText("docs/memory/contract.md");
        var readme = ReadText("docs/memory/README.md");
        var openQuestions = ReadText("docs/memory/open-questions.md");
        var rules = ReadText("docs/memory/rules.md");

        Assert.Contains("Status: active production-candidate semantic quality layer", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local Python embedded", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SQLite `search_documents`", spike, StringComparison.Ordinal);
        Assert.Contains("docs/memory/generated/lancedb", spike, StringComparison.Ordinal);
        Assert.Contains("clean rebuild/delete/reindex", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no Cloud", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no commit hook", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a canonical store", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("production-candidate semantic quality layer", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FastEmbed", spike, StringComparison.Ordinal);
        Assert.Contains("sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2", spike, StringComparison.Ordinal);
        Assert.Contains("embedding_pooling_baseline", spike, StringComparison.Ordinal);
        Assert.Contains("mean-pooling", spike, StringComparison.Ordinal);
        Assert.Contains("lancedb-eval-9-of-9", spike, StringComparison.Ordinal);
        Assert.Contains("eval", spike, StringComparison.Ordinal);
        Assert.Contains("eval` passed `9/9`", spike, StringComparison.Ordinal);
        Assert.Contains("lancedb-eval-report.md", spike, StringComparison.Ordinal);
        Assert.Contains("query", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expected ids", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_path", spike, StringComparison.Ordinal);
        Assert.Contains("gap notes", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("formula_owner", spike, StringComparison.Ordinal);
        Assert.Contains("binance_dto_boundary", spike, StringComparison.Ordinal);
        Assert.Contains("rest_hot_path_ban", spike, StringComparison.Ordinal);
        Assert.Contains("live_replay_same_pipeline", spike, StringComparison.Ordinal);
        Assert.Contains("funding_slow_context", spike, StringComparison.Ordinal);

        Assert.Contains("LanceDB is an active local semantic sidecar spike", contract, StringComparison.Ordinal);
        Assert.Contains("SQLite remains the canonical status store", contract, StringComparison.Ordinal);
        Assert.Contains("local FastEmbed/ONNX", contract, StringComparison.Ordinal);
        Assert.Contains("embedding_pooling_baseline", contract, StringComparison.Ordinal);
        Assert.Contains("rerun cleanup/rebuild/eval", contract, StringComparison.Ordinal);
        Assert.Contains("scripts/lancedb-sidecar.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("lancedb-eval-report.md", readme, StringComparison.Ordinal);
        Assert.Contains("LanceDB semantic quality gate", openQuestions, StringComparison.Ordinal);
        Assert.Contains("rule.rest-hot-path-ban | current | data-pipeline", rules, StringComparison.Ordinal);
        Assert.Contains("rule.binance-dto-boundary | current | architecture", rules, StringComparison.Ordinal);
        Assert.Contains("rule.live-replay-same-pipeline | current | replay", rules, StringComparison.Ordinal);
        Assert.Contains("rule.legacy-superseded | superseded", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void SidecarToolingStaysOutsideWpfRuntimeAndCommitHooks()
    {
        Assert.DoesNotContain("lancedb", ReadText("CryptoIndicatorApp.Desktop/CryptoIndicatorApp.Desktop.csproj"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lancedb", ReadText("CryptoIndicatorApp.Application/CryptoIndicatorApp.Application.csproj"), StringComparison.OrdinalIgnoreCase);

        var hooksDirectory = Path.Combine(Root, ".git", "hooks");
        if (Directory.Exists(hooksDirectory))
        {
            var hookTexts = Directory.GetFiles(hooksDirectory)
                .Where(path => !path.EndsWith(".sample", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText);
            Assert.DoesNotContain(hookTexts, text => text.Contains("lancedb", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string ReadText(string relativePath)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
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
}
