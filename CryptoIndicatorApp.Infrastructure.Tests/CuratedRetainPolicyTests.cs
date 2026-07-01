using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class CuratedRetainPolicyTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void PolicyDocsDefineCuratedRetainLifecycleGates()
    {
        var adr = ReadText("docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md");
        var policy = ReadText("docs/memory/retain-policy.md");
        var contract = ReadText("docs/memory/contract.md");
        var readme = ReadText("docs/memory/README.md");
        var openQuestions = ReadText("docs/memory/open-questions.md");

        Assert.Contains("curated retain policy", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs/memory/retain-policy.md", adr, StringComparison.Ordinal);
        Assert.Contains("redaction before retain", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete policy", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("export policy", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Codex auto-retain remains disabled", adr, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Curated Retain Policy", policy, StringComparison.Ordinal);
        Assert.Contains("redaction before retain", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete policy", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("export policy", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("external retain", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Codex auto-retain", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not be enabled", policy, StringComparison.OrdinalIgnoreCase);

        foreach (var allowedPath in ExpectedAllowlist)
        {
            Assert.Contains(allowedPath, policy, StringComparison.Ordinal);
            Assert.Contains(allowedPath, contract, StringComparison.Ordinal);
        }

        foreach (var deniedPath in ExpectedDenylist)
        {
            Assert.Contains(deniedPath, policy, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(deniedPath, contract, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("retain-policy.md", readme, StringComparison.Ordinal);
        Assert.Contains("redaction", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete/export", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Codex auto-retain remains disabled", openQuestions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redaction/delete/export policy", openQuestions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostCommitMarkerInstallerRemainsMarkerOnlyAfterRetainPolicy()
    {
        var scriptPath = Path.Combine(Root, "scripts", "install-memory-post-commit-marker-hook.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        var hookPath = Path.Combine(temp.Path, "post-commit");
        var reportPath = Path.Combine(temp.Path, "install-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -HookPath {Quote(hookPath)} -OutputPath {Quote(reportPath)} -PlanOnly");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.False(File.Exists(hookPath));
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("planned", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("writes_marker").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("hook_invokes_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());
        Assert.False(root.GetProperty("imports_curated_retain").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
    }

    [Fact]
    public async Task ExportDryRunUsesCuratedReportAndAllowlistedMetadataOnly()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        temp.Write("recordings/live.jsonl", "{}\n");
        temp.Write("docs/memory/generated/project-memory-index.md", "# Generated\n");
        temp.Write(".hindsight/store.md", "# Hindsight\n");
        temp.Write("secrets/openai-token.md", "# Token\n");
        temp.Write("bin/Debug/net8.0/build-output.md", "# Build\n");
        temp.Write("obj/project.assets.md", "# Build\n");
        temp.Write("publish/desktop/report.md", "# Publish\n");

        await RunCuratedDryRunAsync(temp.Path);

        var exportReportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-export-dry-run-report.json");
        var result = await RunProjectScriptAsync(
            "curated-retain-export-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.True(File.Exists(exportReportPath), $"Missing report: {exportReportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(exportReportPath));
        var root = report.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("export-dry-run", root.GetProperty("mode").GetString());
        Assert.Equal("scripts/curated-retain-export-dry-run.ps1", root.GetProperty("generator").GetString());
        Assert.True(root.GetProperty("curated_report_present").GetBoolean());
        Assert.True(root.GetProperty("writes_report_only").GetBoolean());
        Assert.False(root.GetProperty("source_content_included").GetBoolean());
        Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("calls_hindsight").GetBoolean());
        Assert.False(root.GetProperty("calls_codex_retain").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());
        Assert.False(root.GetProperty("imports_denylist").GetBoolean());

        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        var sourcePaths = sources
            .Select(source => source.GetProperty("source_path").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("AGENTS.md", sourcePaths);
        Assert.Contains("TC-DN-HOFI3.md", sourcePaths);
        Assert.Contains("docs/formulas.md", sourcePaths);
        Assert.Contains("tasks/lessons.md", sourcePaths);
        Assert.Contains("docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md", sourcePaths);
        Assert.Contains("docs/memory/contract.md", sourcePaths);
        Assert.Contains("docs/memory/retain-policy.md", sourcePaths);
        AssertNoDeniedPaths(sourcePaths);

        foreach (var source in sources)
        {
            Assert.True(source.GetProperty("source_metadata_only").GetBoolean());
            Assert.True(source.GetProperty("hash_matches_report").GetBoolean(), source.GetProperty("source_path").GetString());
            Assert.True(source.TryGetProperty("source_hash", out _));
            Assert.False(source.TryGetProperty("content", out _));
            Assert.False(source.TryGetProperty("text", out _));
        }
    }

    [Fact]
    public async Task DeleteDryRunWritesDeletionPlanOnlyAndRemovesNothing()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        temp.Write("recordings/live.jsonl", "{}\n");
        var agentPath = Path.Combine(temp.Path, "AGENTS.md");
        var deniedPath = Path.Combine(temp.Path, "recordings", "live.jsonl");

        await RunCuratedDryRunAsync(temp.Path);
        var exportResult = await RunProjectScriptAsync(
            "curated-retain-export-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");
        Assert.True(exportResult.ExitCode == 0, exportResult.ToString());

        var deleteReportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-delete-dry-run-report.json");
        var deleteResult = await RunProjectScriptAsync(
            "curated-retain-delete-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");

        Assert.True(deleteResult.ExitCode == 0, deleteResult.ToString());
        Assert.True(File.Exists(deleteReportPath), $"Missing report: {deleteReportPath}");
        Assert.True(File.Exists(agentPath), "Delete dry-run must not remove allowlisted source files.");
        Assert.True(File.Exists(deniedPath), "Delete dry-run must not remove denylisted source files.");

        using var report = JsonDocument.Parse(File.ReadAllText(deleteReportPath));
        var root = report.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("delete-dry-run", root.GetProperty("mode").GetString());
        Assert.Equal("scripts/curated-retain-delete-dry-run.ps1", root.GetProperty("generator").GetString());
        Assert.True(root.GetProperty("export_report_present").GetBoolean());
        Assert.True(root.GetProperty("writes_report_only").GetBoolean());
        Assert.False(root.GetProperty("deletes_items").GetBoolean());
        Assert.False(root.GetProperty("removes_files").GetBoolean());
        Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("calls_hindsight").GetBoolean());
        Assert.False(root.GetProperty("calls_codex_retain").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());

        AssertJsonArrayContainsAll(
            root.GetProperty("planned_delete_selectors"),
            "retained_item_id",
            "source_path",
            "project_profile");

        var sourcePaths = root.GetProperty("sources")
            .EnumerateArray()
            .Select(source => source.GetProperty("source_path").GetString()!)
            .ToArray();
        Assert.Contains("AGENTS.md", sourcePaths);
        AssertNoDeniedPaths(sourcePaths);
    }

    [Fact]
    public async Task LifecycleDryRunsBlockRetainWhenReportsAreMissingOrStale()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);

        var missingExportResult = await RunProjectScriptAsync(
            "curated-retain-export-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");
        Assert.True(missingExportResult.ExitCode == 0, missingExportResult.ToString());

        using (var missingReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-export-dry-run-report.json"))))
        {
            var root = missingReport.RootElement;
            Assert.Equal("blocked", root.GetProperty("status").GetString());
            Assert.False(root.GetProperty("curated_report_present").GetBoolean());
            Assert.False(root.GetProperty("retain_enablement_candidate").GetBoolean());
            Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
            Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
            AssertJsonArrayContainsAll(root.GetProperty("blocking_reasons"), "missing_curated_retain_report");
        }

        var missingDeleteResult = await RunProjectScriptAsync(
            "curated-retain-delete-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)} -ExportReportPath {Quote(Path.Combine(temp.Path, "docs", "memory", "generated", "missing-export-report.json"))}");
        Assert.True(missingDeleteResult.ExitCode == 0, missingDeleteResult.ToString());

        using (var missingDeleteReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-delete-dry-run-report.json"))))
        {
            var root = missingDeleteReport.RootElement;
            Assert.Equal("blocked", root.GetProperty("status").GetString());
            Assert.False(root.GetProperty("export_report_present").GetBoolean());
            Assert.False(root.GetProperty("retain_enablement_candidate").GetBoolean());
            Assert.False(root.GetProperty("external_retain_enabled").GetBoolean());
            Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
            AssertJsonArrayContainsAll(root.GetProperty("blocking_reasons"), "missing_curated_retain_export_report");
        }

        await RunCuratedDryRunAsync(temp.Path);
        File.AppendAllText(Path.Combine(temp.Path, "AGENTS.md"), "\nChanged after dry-run.\n");

        var staleExportResult = await RunProjectScriptAsync(
            "curated-retain-export-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");
        Assert.True(staleExportResult.ExitCode == 0, staleExportResult.ToString());

        using var staleReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-export-dry-run-report.json")));
        var staleRoot = staleReport.RootElement;
        Assert.Equal("blocked", staleRoot.GetProperty("status").GetString());
        Assert.True(staleRoot.GetProperty("stale_report").GetBoolean());
        Assert.True(staleRoot.GetProperty("summary").GetProperty("source_hash_mismatch_count").GetInt32() >= 1);
        Assert.False(staleRoot.GetProperty("retain_enablement_candidate").GetBoolean());
        Assert.False(staleRoot.GetProperty("external_retain_enabled").GetBoolean());
        Assert.False(staleRoot.GetProperty("codex_auto_retain_enabled").GetBoolean());
        AssertJsonArrayContainsAll(staleRoot.GetProperty("blocking_reasons"), "stale_source_metadata");
    }

    [Fact]
    public async Task LifecycleDryRunsRejectDenylistSourcesEvenIfInputReportContainsThem()
    {
        using var temp = TemporaryDirectory.Create();
        WriteMinimumCuratedSources(temp);
        temp.Write("recordings/live.jsonl", "{}\n");
        temp.Write("docs/memory/generated/unsafe-export.md", "# Generated\n");
        temp.Write(".hindsight/store.md", "# Hindsight\n");
        WriteCompromisedCuratedReport(temp);

        var exportResult = await RunProjectScriptAsync(
            "curated-retain-export-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");
        Assert.True(exportResult.ExitCode == 0, exportResult.ToString());

        using (var exportReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-export-dry-run-report.json"))))
        {
            var root = exportReport.RootElement;
            Assert.Equal("blocked", root.GetProperty("status").GetString());
            Assert.False(root.GetProperty("imports_denylist").GetBoolean());
            Assert.True(root.GetProperty("summary").GetProperty("denied_source_count").GetInt32() >= 3);
            AssertJsonArrayContainsAll(root.GetProperty("blocking_reasons"), "denied_sources_in_input_report");

            var sourcePaths = root.GetProperty("sources")
                .EnumerateArray()
                .Select(source => source.GetProperty("source_path").GetString()!)
                .ToArray();
            AssertNoDeniedPaths(sourcePaths);

            var invalidPaths = root.GetProperty("invalid_sources")
                .EnumerateArray()
                .Select(source => source.GetProperty("source_path").GetString()!)
                .ToArray();
            Assert.Contains("recordings/live.jsonl", invalidPaths);
            Assert.Contains("docs/memory/generated/unsafe-export.md", invalidPaths);
            Assert.Contains(".hindsight/store.md", invalidPaths);
        }

        var deleteResult = await RunProjectScriptAsync(
            "curated-retain-delete-dry-run.ps1",
            $"-ProjectRoot {Quote(temp.Path)}");
        Assert.True(deleteResult.ExitCode == 0, deleteResult.ToString());

        using var deleteReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp.Path, "docs", "memory", "generated", "curated-retain-delete-dry-run-report.json")));
        var deleteRoot = deleteReport.RootElement;
        Assert.False(deleteRoot.GetProperty("deletes_items").GetBoolean());
        Assert.True(deleteRoot.GetProperty("summary").GetProperty("denied_source_count").GetInt32() >= 3);
        AssertJsonArrayContainsAll(deleteRoot.GetProperty("blocking_reasons"), "denied_sources_in_export_report");
        var deleteSourcePaths = deleteRoot.GetProperty("sources")
            .EnumerateArray()
            .Select(source => source.GetProperty("source_path").GetString()!)
            .ToArray();
        AssertNoDeniedPaths(deleteSourcePaths);
    }

    private static readonly string[] ExpectedAllowlist =
    [
        "AGENTS.md",
        "docs/decisions/*.md",
        "docs/formulas.md",
        "TC-DN-HOFI3.md",
        "docs/memory/*.md",
        "tasks/lessons.md",
    ];

    private static readonly string[] ExpectedDenylist =
    [
        "recordings/*.jsonl",
        "docs/memory/generated/",
        ".hindsight/",
        "secrets",
        "bin/",
        "obj/",
        "publish/",
        "local proxy details",
        "raw experiment dumps",
    ];

    private static string ReadText(string relativePath)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(path);
    }

    private static async Task<string> RunCuratedDryRunAsync(string projectRoot)
    {
        var result = await RunProjectScriptAsync(
            "curated-retain-dry-run.ps1",
            $"-ProjectRoot {Quote(projectRoot)}");
        Assert.True(result.ExitCode == 0, result.ToString());

        var reportPath = Path.Combine(projectRoot, "docs", "memory", "generated", "curated-retain-dry-run-report.json");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");
        return reportPath;
    }

    private static async Task<ProcessResult> RunProjectScriptAsync(string scriptName, string arguments)
    {
        var scriptPath = Path.Combine(Root, "scripts", scriptName);
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");
        return await RunPowerShellAsync(scriptPath, arguments);
    }

    private static void WriteMinimumCuratedSources(TemporaryDirectory temp)
    {
        temp.Write("CryptoIndicatorApp.sln", string.Empty);
        temp.Write("AGENTS.md", "# Agents\n\nNo secret values here.\n");
        temp.Write("TC-DN-HOFI3.md", "# Formula Source\n");
        temp.Write("docs/formulas.md", "# Formulas\n");
        temp.Write("tasks/lessons.md", "# Lessons\n");
        temp.Write("docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md", "# ADR 0008\n");
        temp.Write("docs/memory/contract.md", "# Contract\n");
        temp.Write("docs/memory/retain-policy.md", "# Retain Policy\n");
    }

    private static void WriteCompromisedCuratedReport(TemporaryDirectory temp)
    {
        var generatedPath = Path.Combine(temp.Path, "docs", "memory", "generated");
        Directory.CreateDirectory(generatedPath);

        var report = new
        {
            schema_version = 1,
            generated_at = DateTimeOffset.UtcNow.ToString("o"),
            generator = "scripts/curated-retain-dry-run.ps1",
            mode = "dry-run",
            status = "ready_for_review",
            files = new object[]
            {
                new
                {
                    path = "AGENTS.md",
                    hash = ComputeSha256(Path.Combine(temp.Path, "AGENTS.md")),
                    size_bytes = new FileInfo(Path.Combine(temp.Path, "AGENTS.md")).Length,
                    redaction_status = "candidate",
                    finding_count = 0,
                },
                new
                {
                    path = "recordings/live.jsonl",
                    hash = ComputeSha256(Path.Combine(temp.Path, "recordings", "live.jsonl")),
                    size_bytes = new FileInfo(Path.Combine(temp.Path, "recordings", "live.jsonl")).Length,
                    redaction_status = "candidate",
                    finding_count = 0,
                },
                new
                {
                    path = "docs/memory/generated/unsafe-export.md",
                    hash = ComputeSha256(Path.Combine(temp.Path, "docs", "memory", "generated", "unsafe-export.md")),
                    size_bytes = new FileInfo(Path.Combine(temp.Path, "docs", "memory", "generated", "unsafe-export.md")).Length,
                    redaction_status = "candidate",
                    finding_count = 0,
                },
                new
                {
                    path = ".hindsight/store.md",
                    hash = ComputeSha256(Path.Combine(temp.Path, ".hindsight", "store.md")),
                    size_bytes = new FileInfo(Path.Combine(temp.Path, ".hindsight", "store.md")).Length,
                    redaction_status = "candidate",
                    finding_count = 0,
                },
            },
            findings = Array.Empty<object>(),
        };

        File.WriteAllText(
            Path.Combine(generatedPath, "curated-retain-dry-run-report.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }

    private static void AssertNoDeniedPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            Assert.False(path.StartsWith("recordings/", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.StartsWith("docs/memory/generated/", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.StartsWith(".hindsight/", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.StartsWith("secrets/", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.Contains("secret", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.Contains("local-proxy", StringComparison.OrdinalIgnoreCase), path);
            Assert.False(path.Contains("raw-experiment", StringComparison.OrdinalIgnoreCase), path);
            Assert.DoesNotContain(path.Split('/'), segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(path.Split('/'), segment => segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(path.Split('/'), segment => segment.Equals("publish", StringComparison.OrdinalIgnoreCase));
        }
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "curated-retain-policy-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
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
