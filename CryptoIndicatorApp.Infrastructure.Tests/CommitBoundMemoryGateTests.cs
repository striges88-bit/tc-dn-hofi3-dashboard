using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed partial class CommitBoundMemoryGateTests
{
    private static readonly string Root = FindRepositoryRoot();
    private const string EmbeddingModel = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2";
    private const string EmbeddingRuntimeModel = "tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean";

    [Fact]
    public async Task PrePushRejectsCommitAReportsAfterHeadAdvancesToCommitB()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-pre-push-check.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var fixture = TemporaryGitRepository.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("memory-source.md", "commit A\n");
        fixture.CommitAll("commit A");

        var commitA = fixture.RunGit("rev-parse", "HEAD").Trim();
        var treeA = fixture.RunGit("rev-parse", "HEAD^{tree}").Trim();
        var refreshReportPath = fixture.PathFor("memory-refresh-all-report.json");
        var evalJsonPath = fixture.PathFor("lancedb-sidecar-report.json");
        var evalMarkdownPath = fixture.PathFor("lancedb-eval-report.md");
        var outputPath = fixture.PathFor("memory-pre-push-check-report.json");

        WriteCompletedRefreshReport(refreshReportPath, commitA, treeA);
        WritePassingEvalReport(evalJsonPath, commitA, treeA);
        File.WriteAllText(evalMarkdownPath, "# Eval report for commit A\n");

        fixture.Write("memory-source.md", "commit B\n");
        fixture.CommitAll("commit B");
        var commitB = fixture.RunGit("rev-parse", "HEAD").Trim();
        Assert.NotEqual(commitA, commitB);

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(fixture.Root)} " +
            $"-RefreshAllReportPath {Quote(refreshReportPath)} " +
            $"-EvalJsonReportPath {Quote(evalJsonPath)} " +
            $"-EvalMarkdownReportPath {Quote(evalMarkdownPath)} " +
            $"-OutputPath {Quote(outputPath)}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), $"Missing report: {outputPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(outputPath));
        var reportRoot = report.RootElement;
        Assert.Equal("failed", reportRoot.GetProperty("status").GetString());

        var freshnessCheck = reportRoot.GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == "commit-addressed-freshness");
        Assert.Equal("failed", freshnessCheck.GetProperty("status").GetString());
        var detail = freshnessCheck.GetProperty("detail").GetString()!;
        Assert.Contains(commitA, detail, StringComparison.Ordinal);
        Assert.Contains(commitB, detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrePushRejectsMissingLanceDbStoreEvenWhenReportsAreCurrent()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-pre-push-check.ps1");
        using var fixture = TemporaryGitRepository.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("memory-source.md", "current source\n");
        fixture.CommitAll("current memory source");

        var commit = fixture.RunGit("rev-parse", "HEAD").Trim();
        var tree = fixture.RunGit("rev-parse", "HEAD^{tree}").Trim();
        var generated = fixture.PathFor(Path.Combine("docs", "memory", "generated"));
        Directory.CreateDirectory(generated);
        WriteCompletedRefreshReport(Path.Combine(generated, "memory-refresh-all-report.json"), commit, tree);
        WritePassingEvalReport(Path.Combine(generated, "lancedb-sidecar-report.json"), commit, tree);
        WriteIndexManifest(Path.Combine(generated, "lancedb-manifest.json"), commit, tree);
        File.WriteAllText(Path.Combine(generated, "lancedb-eval-report.md"), "# Current eval report\n");
        File.WriteAllText(Path.Combine(generated, "project-memory.sqlite"), string.Empty);
        var outputPath = fixture.PathFor("memory-pre-push-check-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(fixture.Root)} -OutputPath {Quote(outputPath)}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), $"Missing report: {outputPath}");
        using var report = JsonDocument.Parse(File.ReadAllText(outputPath));
        var manifestCheck = report.RootElement.GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == "semantic-index-manifest");
        Assert.Equal("failed", manifestCheck.GetProperty("status").GetString());
        Assert.Contains("store", manifestCheck.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrePushRejectsEvalModelThatDoesNotMatchIndexManifest()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-pre-push-check.ps1");
        using var fixture = TemporaryGitRepository.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("memory-source.md", "current source\n");
        fixture.CommitAll("current memory source");

        var commit = fixture.RunGit("rev-parse", "HEAD").Trim();
        var tree = fixture.RunGit("rev-parse", "HEAD^{tree}").Trim();
        var generated = fixture.PathFor(Path.Combine("docs", "memory", "generated"));
        Directory.CreateDirectory(Path.Combine(generated, "lancedb"));
        WriteCompletedRefreshReport(Path.Combine(generated, "memory-refresh-all-report.json"), commit, tree);
        WritePassingEvalReport(
            Path.Combine(generated, "lancedb-sidecar-report.json"),
            commit,
            tree,
            "tc-dn-hofi3/unreviewed-runtime-model");
        WriteIndexManifest(Path.Combine(generated, "lancedb-manifest.json"), commit, tree);
        File.WriteAllText(Path.Combine(generated, "lancedb-eval-report.md"), "# Current eval report\n");
        File.WriteAllText(Path.Combine(generated, "project-memory.sqlite"), string.Empty);
        var outputPath = fixture.PathFor("memory-pre-push-check-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(fixture.Root)} -OutputPath {Quote(outputPath)}");

        Assert.NotEqual(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(outputPath));
        var manifestCheck = report.RootElement.GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == "semantic-index-manifest");
        Assert.Equal("failed", manifestCheck.GetProperty("status").GetString());
        Assert.Contains("embedding_runtime_model", manifestCheck.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrePushRejectsIndexManifestFromCommitAAfterHeadAndReportsAdvanceToCommitB()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-pre-push-check.ps1");
        using var fixture = TemporaryGitRepository.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("memory-source.md", "commit A\n");
        fixture.CommitAll("commit A");
        var commitA = fixture.RunGit("rev-parse", "HEAD").Trim();
        var treeA = fixture.RunGit("rev-parse", "HEAD^{tree}").Trim();

        fixture.Write("memory-source.md", "commit B\n");
        fixture.CommitAll("commit B");
        var commitB = fixture.RunGit("rev-parse", "HEAD").Trim();
        var treeB = fixture.RunGit("rev-parse", "HEAD^{tree}").Trim();

        var generated = fixture.PathFor(Path.Combine("docs", "memory", "generated"));
        Directory.CreateDirectory(Path.Combine(generated, "lancedb"));
        WriteCompletedRefreshReport(Path.Combine(generated, "memory-refresh-all-report.json"), commitB, treeB);
        WritePassingEvalReport(Path.Combine(generated, "lancedb-sidecar-report.json"), commitB, treeB);
        WriteIndexManifest(Path.Combine(generated, "lancedb-manifest.json"), commitA, treeA);
        File.WriteAllText(Path.Combine(generated, "lancedb-eval-report.md"), "# Current eval report\n");
        File.WriteAllText(Path.Combine(generated, "project-memory.sqlite"), string.Empty);
        var outputPath = fixture.PathFor("memory-pre-push-check-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(fixture.Root)} -OutputPath {Quote(outputPath)}");

        Assert.NotEqual(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(outputPath));
        var manifestCheck = report.RootElement.GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == "semantic-index-manifest");
        Assert.Equal("failed", manifestCheck.GetProperty("status").GetString());
        Assert.Contains("commit_sha", manifestCheck.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains(commitA, manifestCheck.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains(commitB, manifestCheck.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("schema_version", "\"1\"")]
    [InlineData("generator", "\"tools/MemorySemantic/untrusted.py\"")]
    [InlineData("status", "\"unknown\"")]
    [InlineData("source_store", "\"external-store\"")]
    [InlineData("lancedb_table", "\"unreviewed_documents\"")]
    [InlineData("indexed_count", "\"17\"")]
    public async Task PrePushRejectsUnexpectedIndexManifestContractValue(string field, string invalidJsonValue)
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-pre-push-check.ps1");
        using var fixture = TemporaryGitRepository.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("memory-source.md", "current source\n");
        fixture.CommitAll("current memory source");

        var commit = fixture.RunGit("rev-parse", "HEAD").Trim();
        var tree = fixture.RunGit("rev-parse", "HEAD^{tree}").Trim();
        var generated = fixture.PathFor(Path.Combine("docs", "memory", "generated"));
        Directory.CreateDirectory(Path.Combine(generated, "lancedb"));
        WriteCompletedRefreshReport(Path.Combine(generated, "memory-refresh-all-report.json"), commit, tree);
        WritePassingEvalReport(Path.Combine(generated, "lancedb-sidecar-report.json"), commit, tree);
        var manifestPath = Path.Combine(generated, "lancedb-manifest.json");
        WriteIndexManifest(manifestPath, commit, tree);
        RewriteJsonProperty(manifestPath, field, invalidJsonValue);
        File.WriteAllText(Path.Combine(generated, "lancedb-eval-report.md"), "# Current eval report\n");
        File.WriteAllText(Path.Combine(generated, "project-memory.sqlite"), string.Empty);
        var outputPath = fixture.PathFor("memory-pre-push-check-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(fixture.Root)} -OutputPath {Quote(outputPath)}");

        Assert.NotEqual(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(outputPath));
        var manifestCheck = report.RootElement.GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == "semantic-index-manifest");
        Assert.Equal("failed", manifestCheck.GetProperty("status").GetString());
        Assert.Contains(field, manifestCheck.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    private static void WriteCompletedRefreshReport(string path, string commitSha, string treeSha)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
            {
              "schema_version": 1,
              "generator": "scripts/memory-refresh-all.ps1",
              "mode": "full-local-rebuild",
              "status": "completed",
              "commit_sha": "{{commitSha}}",
              "tree_sha": "{{treeSha}}",
              "cloud_enabled": false,
              "codex_auto_retain_enabled": false,
              "auto_commit_refresh_enabled": false,
              "commit_hook_installed": false,
              "installs_hooks": false,
              "direct_project_crawl_enabled": false,
              "imports_raw_jsonl": false,
              "imports_generated_exports": false,
              "uses_generated_exports_as_source": false,
              "imports_secrets": false,
              "imports_local_proxy_details": false,
              "imports_build_artifacts": false,
              "touches_raw_jsonl": false,
              "touches_hindsight_store": false,
              "touches_secret_storage": false,
              "touches_build_artifacts": false,
              "steps": [
                { "name": "legacy-json-refresh", "status": "completed", "exit_code": 0, "uses_cloud": false, "uses_hook": false },
                { "name": "sqlite-refresh", "status": "completed", "exit_code": 0, "uses_cloud": false, "uses_hook": false },
                { "name": "sqlite-stale-check", "status": "completed", "exit_code": 0, "stdout_tail": "{\"issues\": []}", "uses_cloud": false, "uses_hook": false },
                { "name": "lancedb-cleanup", "status": "completed", "exit_code": 0, "uses_cloud": false, "uses_hook": false },
                { "name": "lancedb-rebuild", "status": "completed", "exit_code": 0, "uses_cloud": false, "uses_hook": false },
                { "name": "lancedb-eval", "status": "completed", "exit_code": 0, "uses_cloud": false, "uses_hook": false }
              ]
            }
            """);
    }

    private static void WritePassingEvalReport(
        string path,
        string commitSha,
        string treeSha,
        string embeddingRuntimeModel = EmbeddingRuntimeModel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
            {
              "schema_version": 1,
              "generator": "tools/MemorySemantic/lancedb_sidecar.py",
              "command": "eval",
              "status": "ok",
              "source_store": "sqlite-fts5",
              "commit_sha": "{{commitSha}}",
              "tree_sha": "{{treeSha}}",
              "indexed_at": "2026-07-14T00:00:00Z",
              "index_manifest_status": "ready",
              "manifest_identity_match": true,
              "embedding_provider": "fastembed",
              "embedding_model": "{{EmbeddingModel}}",
              "embedding_runtime_model": "{{embeddingRuntimeModel}}",
              "embedding_dimensions": 384,
              "embedding_package_version": "0.8.0",
              "embedding_package_pin": "fastembed==0.8.0",
              "embedding_pooling": "mean",
              "cloud_enabled": false,
              "auto_commit_refresh_enabled": false,
              "direct_project_crawl_enabled": false,
              "commit_hook_installed": false,
              "passed": true,
              "failed_count": 0,
              "passed_count": 9
            }
            """);
    }

    private static void WriteIndexManifest(string path, string commitSha, string treeSha)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
            {
              "schema_version": 1,
              "generator": "tools/MemorySemantic/lancedb_sidecar.py",
              "status": "ready",
              "source_store": "sqlite-fts5",
              "lancedb_table": "memory_documents",
              "commit_sha": "{{commitSha}}",
              "tree_sha": "{{treeSha}}",
              "indexed_at": "2026-07-14T00:00:00Z",
              "indexed_count": 17,
              "embedding_provider": "fastembed",
              "embedding_model": "{{EmbeddingModel}}",
              "embedding_runtime_model": "{{EmbeddingRuntimeModel}}",
              "embedding_dimensions": 384,
              "embedding_package_version": "0.8.0",
              "embedding_package_pin": "fastembed==0.8.0",
              "embedding_pooling": "mean"
            }
            """);
    }

    private static void RewriteJsonProperty(string path, string field, string jsonValue)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root[field] = JsonNode.Parse(jsonValue);
        File.WriteAllText(path, root.ToJsonString());
    }

    private static async Task<ProcessResult> RunPowerShellAsync(string scriptPath, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
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
        await process.WaitForExitAsync(cts.Token);
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

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private sealed class TemporaryGitRepository : IDisposable
    {
        private TemporaryGitRepository(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporaryGitRepository Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "commit-memory-gate-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var repository = new TemporaryGitRepository(root);
            repository.RunGit("init");
            repository.RunGit("config", "user.name", "Memory Gate Test");
            repository.RunGit("config", "user.email", "memory-gate-test@example.invalid");
            return repository;
        }

        public string PathFor(string relativePath)
        {
            return Path.Combine(Root, relativePath);
        }

        public void Write(string relativePath, string content)
        {
            var path = PathFor(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void CommitAll(string message)
        {
            RunGit("add", ".");
            RunGit("commit", "-m", message);
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
            Assert.True(
                process.ExitCode == 0,
                $"git {string.Join(' ', arguments)} failed with {process.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            return stdout;
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

        private static string GitPath => File.Exists(@"C:\Program Files\Git\cmd\git.exe")
            ? @"C:\Program Files\Git\cmd\git.exe"
            : "git";
    }
}
