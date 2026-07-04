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

        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "CryptoIndicatorApp.sln"), string.Empty);

        var generatedDirectory = Path.Combine(temp.Path, "docs", "memory", "generated");
        Directory.CreateDirectory(generatedDirectory);
        var evalReportPath = Path.Combine(generatedDirectory, "lancedb-sidecar-report.json");
        var probeReportPath = Path.Combine(generatedDirectory, "lancedb-probe-report.json");
        const string evalSentinel = "{ \"command\": \"eval\", \"sentinel\": \"keep\" }\n";
        File.WriteAllText(evalReportPath, evalSentinel);

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ProjectRoot \"{temp.Path}\" -Command probe",
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
        Assert.True(File.Exists(probeReportPath), $"Missing report: {probeReportPath}");
        Assert.Equal(evalSentinel, File.ReadAllText(evalReportPath));

        using var report = JsonDocument.Parse(File.ReadAllText(probeReportPath));
        var root = report.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("scripts/lancedb-sidecar.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("local-python-embedded", root.GetProperty("mode").GetString());
        Assert.Equal("sqlite-fts5", root.GetProperty("source_store").GetString());
        Assert.Equal("docs/memory/generated/lancedb", root.GetProperty("lancedb_store_path").GetString());
        Assert.Equal("docs/memory/generated/lancedb-probe-report.json", root.GetProperty("report_path").GetString());
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
        Assert.Equal("lancedb==0.34.0", root.GetProperty("lancedb_package_pin").GetString());
        Assert.Equal("pyarrow==24.0.0", root.GetProperty("pyarrow_package_pin").GetString());
        Assert.Equal("tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean", root.GetProperty("embedding_runtime_model").GetString());
        Assert.Equal("mean", root.GetProperty("embedding_pooling").GetString());
        Assert.Equal("mean-pooling", root.GetProperty("embedding_pooling_baseline").GetString());
        Assert.Equal("accepted-if-eval-passes", root.GetProperty("embedding_baseline_status").GetString());
        Assert.Equal("lancedb-eval-9-of-9", root.GetProperty("embedding_baseline_eval_gate").GetString());
        Assert.Equal("production-custom-alias-no-suppression", root.GetProperty("embedding_warning_policy").GetString());
        Assert.True(root.GetProperty("hidden_network_downloads_blocked").GetBoolean());
        Assert.True(root.GetProperty("uv_offline_required_for_gate").GetBoolean());
        Assert.True(root.GetProperty("explicit_preflight_required_for_downloads").GetBoolean());
    }

    [Fact]
    public async Task SemanticDoctorPlanWritesReadOnlyDependencyPolicyReport()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-semantic-doctor.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "CryptoIndicatorApp.sln"), string.Empty);
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "memory-semantic-doctor-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(temp.Path)} -OutputPath {Quote(reportPath)} -PlanOnly");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("scripts/memory-semantic-doctor.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("plan-only", root.GetProperty("mode").GetString());
        Assert.Equal("planned", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("manual_only").GetBoolean());
        Assert.True(root.GetProperty("read_only").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());
        Assert.False(root.GetProperty("imports_curated_retain").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("touches_raw_jsonl").GetBoolean());
        Assert.False(root.GetProperty("touches_hindsight_store").GetBoolean());
        Assert.False(root.GetProperty("touches_secret_storage").GetBoolean());
        Assert.False(root.GetProperty("uses_generated_exports_as_source").GetBoolean());
        Assert.False(root.GetProperty("touches_build_artifacts").GetBoolean());
        Assert.True(root.GetProperty("hidden_network_downloads_blocked").GetBoolean());
        Assert.True(root.GetProperty("uv_offline_required_for_gate").GetBoolean());
        Assert.True(root.GetProperty("explicit_preflight_required_for_downloads").GetBoolean());

        var pins = root.GetProperty("dependency_pins");
        Assert.Equal("lancedb==0.34.0", pins.GetProperty("lancedb").GetString());
        Assert.Equal("pyarrow==24.0.0", pins.GetProperty("pyarrow").GetString());
        Assert.Equal("fastembed==0.8.0", pins.GetProperty("fastembed").GetString());

        var uvPolicy = root.GetProperty("uv_policy");
        Assert.Equal("outside-repo-user-cache", uvPolicy.GetProperty("cache_scope").GetString());
        Assert.Equal("no-project-venv", uvPolicy.GetProperty("repo_venv_policy").GetString());
        Assert.True(uvPolicy.GetProperty("cache_must_stay_outside_repo").GetBoolean());
        Assert.True(uvPolicy.GetProperty("model_cache_must_stay_outside_repo").GetBoolean());

        var discoveryOrder = uvPolicy.GetProperty("executable_discovery_order")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("PATH", discoveryOrder);
        Assert.Contains("%APPDATA%/Python/Python312/Scripts/uv.exe", discoveryOrder);
        Assert.Contains("%LOCALAPPDATA%/Microsoft/WinGet/Packages/**/uv.exe", discoveryOrder);
    }

    [Fact]
    public void SidecarWrapperPinsDependenciesAndUsesOfflineUvForGateCommands()
    {
        var script = ReadText("scripts/lancedb-sidecar.ps1");

        Assert.Contains("$lanceDbPackagePin = 'lancedb==0.34.0'", script, StringComparison.Ordinal);
        Assert.Contains("$pyArrowPackagePin = 'pyarrow==24.0.0'", script, StringComparison.Ordinal);
        Assert.Contains("'--offline'", script, StringComparison.Ordinal);
        Assert.Contains("hidden_network_downloads_blocked", script, StringComparison.Ordinal);
        Assert.Contains("explicit_preflight_required_for_downloads", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'--with', 'lancedb'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'--with', 'pyarrow'", script, StringComparison.Ordinal);
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
        Assert.Contains("lancedb==0.34.0", spike, StringComparison.Ordinal);
        Assert.Contains("pyarrow==24.0.0", spike, StringComparison.Ordinal);
        Assert.Contains("sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2", spike, StringComparison.Ordinal);
        Assert.Contains("embedding_runtime_model", spike, StringComparison.Ordinal);
        Assert.Contains("tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean", spike, StringComparison.Ordinal);
        Assert.Contains("embedding_pooling=mean", spike, StringComparison.Ordinal);
        Assert.Contains("embedding_pooling_baseline", spike, StringComparison.Ordinal);
        Assert.Contains("mean-pooling", spike, StringComparison.Ordinal);
        Assert.Contains("production-custom-alias-no-suppression", spike, StringComparison.Ordinal);
        Assert.Contains("lancedb-eval-9-of-9", spike, StringComparison.Ordinal);
        Assert.Contains("eval", spike, StringComparison.Ordinal);
        Assert.Contains("eval` passed `9/9`", spike, StringComparison.Ordinal);
        Assert.Contains("lancedb-eval-report.md", spike, StringComparison.Ordinal);
        Assert.Contains("lancedb-probe-report.json", spike, StringComparison.Ordinal);
        Assert.Contains("command-specific reports", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory-semantic-doctor.ps1", spike, StringComparison.Ordinal);
        Assert.Contains("offline", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hidden network downloads", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outside the repo", spike, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("embedding_runtime_model", contract, StringComparison.Ordinal);
        Assert.Contains("production-custom-alias-no-suppression", contract, StringComparison.Ordinal);
        Assert.Contains("embedding_pooling_baseline", contract, StringComparison.Ordinal);
        Assert.Contains("lancedb==0.34.0", contract, StringComparison.Ordinal);
        Assert.Contains("pyarrow==24.0.0", contract, StringComparison.Ordinal);
        Assert.Contains("memory-semantic-doctor.ps1", contract, StringComparison.Ordinal);
        Assert.Contains("offline", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rerun cleanup/rebuild/eval", contract, StringComparison.Ordinal);
        Assert.Contains("scripts/lancedb-sidecar.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("memory-semantic-doctor.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("lancedb-eval-report.md", readme, StringComparison.Ordinal);
        Assert.Contains("lancedb-probe-report.json", readme, StringComparison.Ordinal);
        Assert.Contains("must not overwrite", readme, StringComparison.OrdinalIgnoreCase);
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

    private static async Task<ProcessResult> RunPowerShellAsync(string scriptPath, string arguments)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);
        var outputTask = process!.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // The assertion below is the useful failure; cleanup is best effort.
            }

            Assert.Fail($"{Path.GetFileName(scriptPath)} timed out.");
        }

        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
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

    private sealed record ProcessResult(int ExitCode, string Output, string Error)
    {
        public override string ToString()
        {
            return $"Exit {ExitCode}\nSTDOUT:\n{Output}\nSTDERR:\n{Error}";
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lancedb-sidecar-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
