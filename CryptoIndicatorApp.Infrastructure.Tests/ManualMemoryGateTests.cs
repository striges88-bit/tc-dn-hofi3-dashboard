using System.Diagnostics;
using System.Text.Json;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class ManualMemoryGateTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public async Task PrePushCheckPlanWritesSafeManualGateReport()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-pre-push-check.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var reportPath = Path.Combine(Root, "docs", "memory", "generated", "memory-pre-push-check-report.json");
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ProjectRoot \"{Root}\" -OutputPath \"{reportPath}\" -PlanOnly",
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

            Assert.Fail("memory-pre-push-check.ps1 plan timed out.");
        }

        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}\nSTDOUT:\n{output}\nSTDERR:\n{error}");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("scripts/memory-pre-push-check.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("plan-only", root.GetProperty("mode").GetString());
        Assert.Equal("planned", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("manual_only").GetBoolean());
        Assert.True(root.GetProperty("requires_existing_refresh_all_report").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("post_commit_auto_refresh_enabled").GetBoolean());
        Assert.False(root.GetProperty("commit_hook_installed").GetBoolean());
        Assert.Equal(IsManagedPrePushHookInstalled(Root), root.GetProperty("pre_push_hook_installed").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("touches_raw_jsonl").GetBoolean());
        Assert.False(root.GetProperty("touches_hindsight_store").GetBoolean());
        Assert.False(root.GetProperty("touches_secret_storage").GetBoolean());
        Assert.False(root.GetProperty("uses_generated_exports_as_source").GetBoolean());
        Assert.False(root.GetProperty("touches_build_artifacts").GetBoolean());

        var checks = root.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Equal(
            new[]
            {
                "refresh-all-report-exists",
                "refresh-all-status-completed",
                "refresh-all-safety-flags",
                "refresh-all-steps-completed",
                "lancedb-eval-json-exists",
                "lancedb-eval-passed",
                "lancedb-eval-markdown-exists",
            },
            checks.Select(check => check.GetProperty("name").GetString()).ToArray());

        Assert.All(checks, check =>
        {
            Assert.Equal("planned", check.GetProperty("status").GetString());
            Assert.False(check.GetProperty("uses_cloud").GetBoolean());
            Assert.False(check.GetProperty("uses_hook").GetBoolean());
            Assert.False(check.GetProperty("touches_denylist").GetBoolean());
            Assert.False(check.GetProperty("uses_generated_exports_as_source").GetBoolean());
            var description = check.GetProperty("description").GetString()!;
            Assert.DoesNotContain("recordings/", description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".hindsight", description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/bin/", description.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/obj/", description.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("publish/", description, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task PrePushCheckPlanReportsManagedPrePushHookWithoutInstallingOrRebuilding()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-pre-push-check.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "CryptoIndicatorApp.sln"), string.Empty);
        var hookDirectory = Path.Combine(temp.Path, ".git", "hooks");
        Directory.CreateDirectory(hookDirectory);
        var hookPath = Path.Combine(hookDirectory, "pre-push");
        WriteManagedPrePushHook(hookPath, temp.Path);
        var reportPath = Path.Combine(temp.Path, "memory-pre-push-check-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(temp.Path)} -OutputPath {Quote(reportPath)} -PlanOnly");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("planned", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("manual_only").GetBoolean());
        Assert.True(root.GetProperty("pre_push_hook_installed").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("post_commit_auto_refresh_enabled").GetBoolean());
        Assert.False(root.GetProperty("commit_hook_installed").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("touches_raw_jsonl").GetBoolean());
        Assert.False(root.GetProperty("touches_hindsight_store").GetBoolean());
        Assert.False(root.GetProperty("touches_secret_storage").GetBoolean());
        Assert.False(root.GetProperty("uses_generated_exports_as_source").GetBoolean());
        Assert.False(root.GetProperty("touches_build_artifacts").GetBoolean());
    }

    [Fact]
    public async Task PrePushCheckRejectsNonEvalLanceDbReportWithoutPowerShellPropertyCrash()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-pre-push-check.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "CryptoIndicatorApp.sln"), string.Empty);

        var refreshReportPath = Path.Combine(temp.Path, "memory-refresh-all-report.json");
        var evalJsonPath = Path.Combine(temp.Path, "lancedb-sidecar-report.json");
        var evalMarkdownPath = Path.Combine(temp.Path, "lancedb-eval-report.md");
        var outputPath = Path.Combine(temp.Path, "memory-pre-push-check-report.json");

        File.WriteAllText(
            refreshReportPath,
            """
            {
              "schema_version": 1,
              "generator": "scripts/memory-refresh-all.ps1",
              "mode": "full-local-rebuild",
              "status": "completed",
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
        File.WriteAllText(
            evalJsonPath,
            """
            {
              "schema_version": 1,
              "generator": "scripts/lancedb-sidecar.ps1",
              "status": "ready-to-run",
              "command": "probe"
            }
            """);
        File.WriteAllText(evalMarkdownPath, "# Probe report, not eval\n");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(temp.Path)} -RefreshAllReportPath {Quote(refreshReportPath)} -EvalJsonReportPath {Quote(evalJsonPath)} -EvalMarkdownReportPath {Quote(evalMarkdownPath)} -OutputPath {Quote(outputPath)}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("passed_count", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(outputPath), $"Missing report: {outputPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(outputPath));
        var root = report.RootElement;
        Assert.Equal("failed", root.GetProperty("status").GetString());

        var evalCheck = root.GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == "lancedb-eval-passed");
        Assert.Equal("failed", evalCheck.GetProperty("status").GetString());
        Assert.Contains("missing", evalCheck.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualMemoryGateDocsRejectPostCommitAutomation()
    {
        var adr = ReadText("docs/decisions/0005-manual-memory-gate.md");
        var contract = ReadText("docs/memory/contract.md");
        var scriptsReadme = ReadText("scripts/README.md");
        var lessons = ReadText("tasks/lessons.md");

        Assert.Contains("manual memory gate", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory-pre-push-check.ps1", adr, StringComparison.Ordinal);
        Assert.Contains("post-commit", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not run rebuild", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory-pre-push-check", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manual", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-commit marker", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not run rebuild", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory-pre-push-check.ps1", scriptsReadme, StringComparison.Ordinal);
        Assert.Contains("memory-pre-push-check", lessons, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MemoryDailyCheckPlanWritesReadOnlyOperatorReport()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-daily-check.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        var reportPath = Path.Combine(temp.Path, "memory-daily-check-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -OutputPath {Quote(reportPath)} -PlanOnly");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("scripts/memory-daily-check.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("plan-only", root.GetProperty("mode").GetString());
        Assert.Equal("reported", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("manual_only").GetBoolean());
        Assert.True(root.GetProperty("read_only").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());
        Assert.False(root.GetProperty("imports_curated_retain").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("calls_hindsight").GetBoolean());
        Assert.False(root.GetProperty("calls_codex_retain").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("post_commit_auto_refresh_enabled").GetBoolean());
        Assert.False(root.GetProperty("touches_raw_jsonl").GetBoolean());
        Assert.False(root.GetProperty("touches_hindsight_store").GetBoolean());
        Assert.False(root.GetProperty("touches_secret_storage").GetBoolean());
        Assert.False(root.GetProperty("uses_generated_exports_as_source").GetBoolean());
        Assert.False(root.GetProperty("touches_build_artifacts").GetBoolean());
        Assert.True(root.GetProperty("memory_cli_checks_serialized").GetBoolean());
        Assert.Equal("docs/memory/generated/memory-cli.lock", root.GetProperty("memory_cli_lock_path").GetString());

        var git = root.GetProperty("git");
        Assert.True(git.TryGetProperty("branch", out _));
        Assert.False(string.IsNullOrWhiteSpace(git.GetProperty("head").GetString()));

        var memoryStatus = root.GetProperty("memory_status");
        Assert.True(memoryStatus.TryGetProperty("status", out _));
        Assert.True(memoryStatus.TryGetProperty("head", out _));
        Assert.True(memoryStatus.TryGetProperty("indexed_commit", out _));
        Assert.True(memoryStatus.TryGetProperty("needs_refresh", out _));
        Assert.True(memoryStatus.TryGetProperty("needs_refresh_is_known", out _));
        Assert.True(memoryStatus.TryGetProperty("marker_exists", out _));
        Assert.True(memoryStatus.TryGetProperty("working_tree_dirty", out _));

        var marker = root.GetProperty("marker");
        Assert.Equal("docs/memory/generated/memory-needs-refresh.marker.json", marker.GetProperty("path").GetString());
        Assert.True(marker.TryGetProperty("exists", out _));

        var eval = root.GetProperty("lancedb_eval");
        Assert.Equal("docs/memory/generated/lancedb-sidecar-report.json", eval.GetProperty("json_report_path").GetString());
        Assert.Equal("docs/memory/generated/lancedb-eval-report.md", eval.GetProperty("markdown_report_path").GetString());
        Assert.True(eval.TryGetProperty("status", out _));
        Assert.True(eval.TryGetProperty("passed", out _));
        Assert.True(eval.TryGetProperty("passed_count", out _));
        Assert.True(eval.TryGetProperty("failed_count", out _));

        var reportNames = root.GetProperty("generated_reports")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("memory-refresh-all", reportNames);
        Assert.Contains("lancedb-eval-json", reportNames);
        Assert.Contains("lancedb-eval-markdown", reportNames);
        Assert.Contains("memory-pre-push-check", reportNames);

        Assert.All(root.GetProperty("observations").EnumerateArray(), observation =>
        {
            Assert.False(observation.GetProperty("uses_cloud").GetBoolean());
            Assert.False(observation.GetProperty("uses_hook").GetBoolean());
            Assert.False(observation.GetProperty("runs_rebuild").GetBoolean());
            Assert.False(observation.GetProperty("touches_denylist").GetBoolean());
        });
    }

    [Fact]
    public async Task MemoryDailyCheckDoesNotReportNeedsRefreshWhenMemoryCliIsUnavailable()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-daily-check.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "CryptoIndicatorApp.sln"), string.Empty);
        var generated = Path.Combine(temp.Path, "docs", "memory", "generated");
        Directory.CreateDirectory(generated);
        File.WriteAllText(Path.Combine(generated, "project-memory.sqlite"), string.Empty);

        var reportPath = Path.Combine(temp.Path, "memory-daily-check-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(temp.Path)} -OutputPath {Quote(reportPath)}");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("reported", root.GetProperty("status").GetString());

        var memoryStatus = root.GetProperty("memory_status");
        Assert.False(memoryStatus.GetProperty("available").GetBoolean());
        Assert.Equal("cli-unavailable", memoryStatus.GetProperty("status").GetString());
        Assert.False(memoryStatus.GetProperty("needs_refresh_is_known").GetBoolean());
        Assert.Equal(JsonValueKind.Null, memoryStatus.GetProperty("needs_refresh").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("summary").GetProperty("needs_refresh").ValueKind);
        Assert.NotEqual(string.Empty, memoryStatus.GetProperty("error_message").GetString());
    }

    [Fact]
    public void MemoryDailyCheckDocsDescribeReadOnlyRoutine()
    {
        var memoryReadme = ReadText("docs/memory/README.md");
        var scriptsReadme = ReadText("scripts/README.md");
        var roadmap = ReadText("docs/superpowers/plans/2026-07-01-memory-polish-roadmap.md");

        Assert.Contains("memory-daily-check.ps1", memoryReadme, StringComparison.Ordinal);
        Assert.Contains("read-only", memoryReadme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not run `memory-refresh-all`", memoryReadme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CLI unavailable", memoryReadme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("needs_refresh unknown", memoryReadme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory-daily-check.ps1", scriptsReadme, StringComparison.Ordinal);
        Assert.Contains("operator", scriptsReadme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not rebuild", scriptsReadme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet run --no-restore", scriptsReadme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Memory Operator UX", roadmap, StringComparison.Ordinal);
        Assert.Contains("memory-daily-check.ps1", roadmap, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryOperationsRunbookListsRoutineCommandsAndAutomationLimits()
    {
        var runbook = ReadText("docs/memory/operations-runbook.md");
        var memoryReadme = ReadText("docs/memory/README.md");
        var scriptsReadme = ReadText("scripts/README.md");

        Assert.Contains("memory-daily-check.ps1", runbook, StringComparison.Ordinal);
        Assert.Contains("memory-refresh-all.ps1", runbook, StringComparison.Ordinal);
        Assert.Contains("memory-pre-push-check.ps1", runbook, StringComparison.Ordinal);
        Assert.Contains("memory-clone-recovery-check.ps1", runbook, StringComparison.Ordinal);
        Assert.Contains("memory-rebuild-from-head.ps1", runbook, StringComparison.Ordinal);
        Assert.Contains("/compact", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("needs_refresh=false", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("9/9", runbook, StringComparison.Ordinal);
        Assert.Contains("auto-retain", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-commit rebuild", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("raw JSONL", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secrets", runbook, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("operations-runbook.md", memoryReadme, StringComparison.Ordinal);
        Assert.Contains("memory-clone-recovery-check.ps1", memoryReadme, StringComparison.Ordinal);
        Assert.Contains("memory-clone-recovery-check.ps1", scriptsReadme, StringComparison.Ordinal);
        Assert.Contains("clone-like", scriptsReadme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallPrePushHookPlanDoesNotInstallHookOrRunRebuild()
    {
        var scriptPath = Path.Combine(Root, "scripts", "install-memory-pre-push-hook.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        var hookPath = Path.Combine(temp.Path, "pre-push");
        var reportPath = Path.Combine(temp.Path, "install-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -HookPath {Quote(hookPath)} -OutputPath {Quote(reportPath)} -PlanOnly");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.False(File.Exists(hookPath));
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("scripts/install-memory-pre-push-hook.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("plan-only", root.GetProperty("mode").GetString());
        Assert.Equal("planned", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("manual_only").GetBoolean());
        Assert.True(root.GetProperty("requires_confirm").GetBoolean());
        Assert.True(root.GetProperty("would_install_hook").GetBoolean());
        Assert.True(root.GetProperty("hook_invokes_helper").GetBoolean());
        Assert.Equal("scripts/memory-pre-push-check.ps1", root.GetProperty("helper_script").GetString());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("pre_push_hook_installed").GetBoolean());
        AssertCommonHookSafetyFlags(root);
    }

    [Fact]
    public async Task InstallPrePushHookConfirmWritesManagedHelperHookAndDisableRemovesIt()
    {
        var scriptPath = Path.Combine(Root, "scripts", "install-memory-pre-push-hook.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        var hookPath = Path.Combine(temp.Path, "pre-push");
        var installReportPath = Path.Combine(temp.Path, "install-report.json");

        var install = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -HookPath {Quote(hookPath)} -OutputPath {Quote(installReportPath)} -Confirm");

        Assert.True(install.ExitCode == 0, install.ToString());
        Assert.True(File.Exists(hookPath), $"Missing hook: {hookPath}");

        var hook = File.ReadAllText(hookPath);
        Assert.StartsWith("#!/bin/sh\n", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", hook, StringComparison.Ordinal);
        Assert.Contains("TC-DN-HOFI3 managed memory pre-push hook", hook, StringComparison.Ordinal);
        Assert.Contains("memory-pre-push-check.ps1", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("memory-refresh-all", hook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("post-commit", hook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recordings", hook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".hindsight", hook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", hook, StringComparison.OrdinalIgnoreCase);
        var normalizedHook = hook.Replace('\\', '/');
        Assert.DoesNotContain("bin/Debug", normalizedHook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bin/Release", normalizedHook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/obj/", normalizedHook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("publish/", normalizedHook, StringComparison.OrdinalIgnoreCase);

        using (var installReport = JsonDocument.Parse(File.ReadAllText(installReportPath)))
        {
            var root = installReport.RootElement;
            Assert.Equal("install", root.GetProperty("mode").GetString());
            Assert.Equal("installed", root.GetProperty("status").GetString());
            Assert.True(root.GetProperty("installs_hooks").GetBoolean());
            Assert.True(root.GetProperty("pre_push_hook_installed").GetBoolean());
            Assert.True(root.GetProperty("hook_invokes_helper").GetBoolean());
            Assert.False(root.GetProperty("hook_invokes_refresh_all").GetBoolean());
            AssertCommonHookSafetyFlags(root);
        }

        var disableReportPath = Path.Combine(temp.Path, "disable-report.json");
        var disable = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -HookPath {Quote(hookPath)} -OutputPath {Quote(disableReportPath)} -Disable -Confirm");

        Assert.True(disable.ExitCode == 0, disable.ToString());
        Assert.False(File.Exists(hookPath));

        using var disableReport = JsonDocument.Parse(File.ReadAllText(disableReportPath));
        var disableRoot = disableReport.RootElement;
        Assert.Equal("disable", disableRoot.GetProperty("mode").GetString());
        Assert.Equal("disabled", disableRoot.GetProperty("status").GetString());
        Assert.True(disableRoot.GetProperty("managed_hook_removed").GetBoolean());
        Assert.False(disableRoot.GetProperty("installs_hooks").GetBoolean());
        Assert.False(disableRoot.GetProperty("pre_push_hook_installed").GetBoolean());
        AssertCommonHookSafetyFlags(disableRoot);
    }

    [Fact]
    public async Task InstallPrePushHookConfirmRefusesUnmanagedExistingHook()
    {
        var scriptPath = Path.Combine(Root, "scripts", "install-memory-pre-push-hook.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        var hookPath = Path.Combine(temp.Path, "pre-push");
        var reportPath = Path.Combine(temp.Path, "install-report.json");
        File.WriteAllText(hookPath, "#!/bin/sh\nexit 0\n");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -HookPath {Quote(hookPath)} -OutputPath {Quote(reportPath)} -Confirm");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("#!/bin/sh\nexit 0\n", File.ReadAllText(hookPath));
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("failed", root.GetProperty("status").GetString());
        Assert.Equal("unmanaged-hook-exists", root.GetProperty("failure_code").GetString());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        AssertCommonHookSafetyFlags(root);
    }

    [Fact]
    public void OptionalMemoryHookDocsRequireExplicitConfirmAndDisable()
    {
        var adr = ReadText("docs/decisions/0006-optional-memory-pre-push-hook.md");
        var contract = ReadText("docs/memory/contract.md");
        var memoryReadme = ReadText("docs/memory/README.md");
        var scriptsReadme = ReadText("scripts/README.md");
        var lessons = ReadText("tasks/lessons.md");

        Assert.Contains("optional", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install-memory-pre-push-hook.ps1", adr, StringComparison.Ordinal);
        Assert.Contains("-Confirm", adr, StringComparison.Ordinal);
        Assert.Contains("-Disable", adr, StringComparison.Ordinal);
        Assert.Contains("memory-pre-push-check.ps1", adr, StringComparison.Ordinal);
        Assert.Contains("not run", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-commit", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not add", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install-memory-pre-push-hook.ps1", contract, StringComparison.Ordinal);
        Assert.Contains("-Confirm", contract, StringComparison.Ordinal);
        Assert.Contains("-Disable", contract, StringComparison.Ordinal);
        Assert.Contains("install-memory-pre-push-hook.ps1", memoryReadme, StringComparison.Ordinal);
        Assert.Contains("install-memory-pre-push-hook.ps1", scriptsReadme, StringComparison.Ordinal);
        Assert.Contains("optional pre-push", lessons, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallPostCommitMarkerHookPlanDoesNotInstallHookOrRunRebuild()
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
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("scripts/install-memory-post-commit-marker-hook.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("plan-only", root.GetProperty("mode").GetString());
        Assert.Equal("planned", root.GetProperty("status").GetString());
        Assert.Equal("post-commit", root.GetProperty("hook_type").GetString());
        AssertCustomPostCommitHookValidationPath(root);
        Assert.True(root.GetProperty("manual_only").GetBoolean());
        Assert.True(root.GetProperty("requires_confirm").GetBoolean());
        Assert.True(root.GetProperty("writes_marker").GetBoolean());
        Assert.True(root.GetProperty("uses_lock").GetBoolean());
        Assert.True(root.GetProperty("timeout_seconds").GetInt32() > 0);
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("post_commit_hook_installed").GetBoolean());
        AssertPostCommitMarkerSafetyFlags(root);
    }

    [Fact]
    public async Task InstallPostCommitMarkerHookConfirmWritesManagedMarkerOnlyHookAndDisableRemovesIt()
    {
        var scriptPath = Path.Combine(Root, "scripts", "install-memory-post-commit-marker-hook.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        var hookPath = Path.Combine(temp.Path, "post-commit");
        var reportPath = Path.Combine(temp.Path, "install-report.json");

        var install = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -HookPath {Quote(hookPath)} -OutputPath {Quote(reportPath)} -Confirm -TimeoutSeconds 7");

        Assert.True(install.ExitCode == 0, install.ToString());
        Assert.True(File.Exists(hookPath), $"Missing hook: {hookPath}");

        var hook = File.ReadAllText(hookPath);
        Assert.StartsWith("#!/bin/sh\n", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", hook, StringComparison.Ordinal);
        Assert.Contains("TC-DN-HOFI3 managed memory post-commit marker hook", hook, StringComparison.Ordinal);
        Assert.Contains("memory-mark-needs-refresh.ps1", hook, StringComparison.Ordinal);
        Assert.Contains("-TimeoutSeconds 7", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("memory-refresh-all", hook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lancedb", hook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("retain", hook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recordings", hook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".hindsight", hook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", hook, StringComparison.OrdinalIgnoreCase);

        using (var installReport = JsonDocument.Parse(File.ReadAllText(reportPath)))
        {
            var root = installReport.RootElement;
            Assert.Equal("install", root.GetProperty("mode").GetString());
            Assert.Equal("installed", root.GetProperty("status").GetString());
            AssertCustomPostCommitHookValidationPath(root);
            Assert.True(root.GetProperty("installs_hooks").GetBoolean());
            Assert.True(root.GetProperty("post_commit_hook_installed").GetBoolean());
            Assert.True(root.GetProperty("hook_invokes_marker_helper").GetBoolean());
            Assert.True(root.GetProperty("writes_marker").GetBoolean());
            AssertPostCommitMarkerSafetyFlags(root);
        }

        var disableReportPath = Path.Combine(temp.Path, "disable-report.json");
        var disable = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -HookPath {Quote(hookPath)} -OutputPath {Quote(disableReportPath)} -Disable -Confirm");

        Assert.True(disable.ExitCode == 0, disable.ToString());
        Assert.False(File.Exists(hookPath));

        using var disableReport = JsonDocument.Parse(File.ReadAllText(disableReportPath));
        var disableRoot = disableReport.RootElement;
        Assert.Equal("disable", disableRoot.GetProperty("mode").GetString());
        Assert.Equal("disabled", disableRoot.GetProperty("status").GetString());
        AssertCustomPostCommitHookValidationPath(disableRoot);
        Assert.True(disableRoot.GetProperty("managed_hook_removed").GetBoolean());
        Assert.False(disableRoot.GetProperty("installs_hooks").GetBoolean());
        Assert.False(disableRoot.GetProperty("post_commit_hook_installed").GetBoolean());
        AssertPostCommitMarkerSafetyFlags(disableRoot);
    }

    [Fact]
    public async Task InstallPostCommitMarkerHookRequiresConfirmAndDoesNotTouchRepoHook()
    {
        var scriptPath = Path.Combine(Root, "scripts", "install-memory-post-commit-marker-hook.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        var hookPath = Path.Combine(temp.Path, "post-commit");
        var reportPath = Path.Combine(temp.Path, "confirm-required-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -HookPath {Quote(hookPath)} -OutputPath {Quote(reportPath)}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(hookPath));
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("install", root.GetProperty("mode").GetString());
        Assert.Equal("failed", root.GetProperty("status").GetString());
        Assert.Equal("confirm-required", root.GetProperty("failure_code").GetString());
        Assert.False(root.GetProperty("confirm_provided").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("post_commit_hook_installed").GetBoolean());
        AssertCustomPostCommitHookValidationPath(root);
        AssertPostCommitMarkerSafetyFlags(root);
    }

    [Fact]
    public async Task InstallPostCommitMarkerHookRejectsNonPositiveTimeout()
    {
        var scriptPath = Path.Combine(Root, "scripts", "install-memory-post-commit-marker-hook.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        var hookPath = Path.Combine(temp.Path, "post-commit");
        var reportPath = Path.Combine(temp.Path, "invalid-timeout-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -HookPath {Quote(hookPath)} -OutputPath {Quote(reportPath)} -Confirm -TimeoutSeconds 0");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(hookPath));
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("failed", root.GetProperty("status").GetString());
        Assert.Equal("invalid-timeout-seconds", root.GetProperty("failure_code").GetString());
        Assert.Equal(0, root.GetProperty("timeout_seconds").GetInt32());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("post_commit_hook_installed").GetBoolean());
        AssertCustomPostCommitHookValidationPath(root);
        AssertPostCommitMarkerSafetyFlags(root);
    }

    [Fact]
    public async Task InstallPostCommitMarkerHookConfirmRefusesUnmanagedExistingHook()
    {
        var scriptPath = Path.Combine(Root, "scripts", "install-memory-post-commit-marker-hook.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        var hookPath = Path.Combine(temp.Path, "post-commit");
        var reportPath = Path.Combine(temp.Path, "unmanaged-report.json");
        File.WriteAllText(hookPath, "#!/bin/sh\nexit 0\n");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(Root)} -HookPath {Quote(hookPath)} -OutputPath {Quote(reportPath)} -Confirm");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("#!/bin/sh\nexit 0\n", File.ReadAllText(hookPath));
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal("failed", root.GetProperty("status").GetString());
        Assert.Equal("unmanaged-hook-exists", root.GetProperty("failure_code").GetString());
        Assert.True(root.GetProperty("unmanaged_hook_detected").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        AssertCustomPostCommitHookValidationPath(root);
        AssertPostCommitMarkerSafetyFlags(root);
    }

    [Fact]
    public async Task PostCommitMarkerHelperWritesMarkerOnlyReport()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-mark-needs-refresh.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "CryptoIndicatorApp.sln"), string.Empty);

        var markerPath = Path.Combine(temp.Path, "docs", "memory", "generated", "memory-needs-refresh.marker.json");
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "memory-mark-needs-refresh-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(temp.Path)} -MarkerPath {Quote(markerPath)} -OutputPath {Quote(reportPath)} -Reason post-commit-validation -TimeoutSeconds 3");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.True(File.Exists(markerPath), $"Missing marker: {markerPath}");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using (var marker = JsonDocument.Parse(File.ReadAllText(markerPath)))
        {
            var root = marker.RootElement;
            Assert.Equal("scripts/memory-mark-needs-refresh.ps1", root.GetProperty("generator").GetString());
            Assert.Equal("post-commit-validation", root.GetProperty("reason").GetString());
            Assert.Equal("tools/Memory refresh-from-commit --commit HEAD", root.GetProperty("refresh_command").GetString());
            Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
            Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());
            Assert.False(root.GetProperty("imports_curated_retain").GetBoolean());
            Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
            Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        }

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var reportRoot = report.RootElement;
        Assert.Equal("marked", reportRoot.GetProperty("status").GetString());
        Assert.True(reportRoot.GetProperty("writes_marker").GetBoolean());
        Assert.True(reportRoot.GetProperty("uses_lock").GetBoolean());
        AssertPostCommitMarkerSafetyFlags(reportRoot);
    }

    [Fact]
    public async Task PostCommitMarkerHelperRejectsNonPositiveTimeoutWithoutWritingMarker()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-mark-needs-refresh.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "CryptoIndicatorApp.sln"), string.Empty);

        var markerPath = Path.Combine(temp.Path, "docs", "memory", "generated", "memory-needs-refresh.marker.json");
        var reportPath = Path.Combine(temp.Path, "docs", "memory", "generated", "memory-mark-needs-refresh-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(temp.Path)} -MarkerPath {Quote(markerPath)} -OutputPath {Quote(reportPath)} -Reason post-commit-validation -TimeoutSeconds 0");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(markerPath));
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var reportRoot = report.RootElement;
        Assert.Equal("failed", reportRoot.GetProperty("status").GetString());
        Assert.Equal("invalid-timeout-seconds", reportRoot.GetProperty("failure_code").GetString());
        Assert.Equal(0, reportRoot.GetProperty("timeout_seconds").GetInt32());
        Assert.True(reportRoot.GetProperty("uses_lock").GetBoolean());
        Assert.False(reportRoot.GetProperty("lock_acquired").GetBoolean());
        Assert.False(reportRoot.GetProperty("writes_marker").GetBoolean());
        Assert.EndsWith("docs/memory/generated/memory-needs-refresh.lock", reportRoot.GetProperty("lock_path").GetString()!.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        AssertPostCommitMarkerSafetyFlags(reportRoot);
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

    private static void AssertCommonHookSafetyFlags(JsonElement root)
    {
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("hook_invokes_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("post_commit_auto_refresh_enabled").GetBoolean());
        Assert.False(root.GetProperty("commit_hook_installed").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("touches_raw_jsonl").GetBoolean());
        Assert.False(root.GetProperty("touches_hindsight_store").GetBoolean());
        Assert.False(root.GetProperty("touches_secret_storage").GetBoolean());
        Assert.False(root.GetProperty("uses_generated_exports_as_source").GetBoolean());
        Assert.False(root.GetProperty("touches_build_artifacts").GetBoolean());
    }

    private static void AssertPostCommitMarkerSafetyFlags(JsonElement root)
    {
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("hook_invokes_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("rebuilds_memory").GetBoolean());
        Assert.False(root.GetProperty("imports_curated_retain").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("touches_raw_jsonl").GetBoolean());
        Assert.False(root.GetProperty("touches_hindsight_store").GetBoolean());
        Assert.False(root.GetProperty("touches_secret_storage").GetBoolean());
        Assert.False(root.GetProperty("uses_generated_exports_as_source").GetBoolean());
        Assert.False(root.GetProperty("touches_build_artifacts").GetBoolean());
    }

    private static void AssertCustomPostCommitHookValidationPath(JsonElement root)
    {
        Assert.Equal(".git/hooks/post-commit", root.GetProperty("default_hook_path").GetString());
        Assert.False(root.GetProperty("targets_default_repo_hook").GetBoolean());
        Assert.True(root.GetProperty("custom_hook_path").GetBoolean());
        Assert.False(root.GetProperty("writes_default_repo_hook").GetBoolean());
        Assert.False(root.GetProperty("removes_default_repo_hook").GetBoolean());
        Assert.False(root.GetProperty("actual_repo_hook_touched").GetBoolean());
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static bool IsManagedPrePushHookInstalled(string root)
    {
        var hookPath = Path.Combine(root, ".git", "hooks", "pre-push");
        if (!File.Exists(hookPath))
        {
            return false;
        }

        var hook = File.ReadAllText(hookPath);
        return hook.Contains("TC-DN-HOFI3 managed memory pre-push hook", StringComparison.Ordinal) &&
            hook.Contains("Managed-By: scripts/install-memory-pre-push-hook.ps1", StringComparison.Ordinal);
    }

    private static void WriteManagedPrePushHook(string hookPath, string projectRoot)
    {
        var normalizedRoot = projectRoot.Replace('\\', '/');
        File.WriteAllText(
            hookPath,
            $"""
            #!/bin/sh
            # TC-DN-HOFI3 managed memory pre-push hook
            # Managed-By: scripts/install-memory-pre-push-hook.ps1
            # Validates existing memory refresh/eval reports only.
            PROJECT_ROOT='{normalizedRoot}'
            exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PROJECT_ROOT/scripts/memory-pre-push-check.ps1" -ProjectRoot "$PROJECT_ROOT"

            """.Replace("\r\n", "\n", StringComparison.Ordinal));
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "memory-hook-test-" + Guid.NewGuid().ToString("N"));
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
