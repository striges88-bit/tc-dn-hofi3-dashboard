using System.Diagnostics;
using System.Text.Json;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class MemoryRefreshAllTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public async Task RefreshAllPlanWritesSafeReportWithExpectedStepOrder()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-refresh-all.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var reportPath = Path.Combine(Root, "docs", "memory", "generated", "memory-refresh-all-report.json");
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

            Assert.Fail("memory-refresh-all.ps1 plan timed out.");
        }

        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}\nSTDOUT:\n{output}\nSTDERR:\n{error}");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("scripts/memory-refresh-all.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("plan-only", root.GetProperty("mode").GetString());
        Assert.Equal("planned", root.GetProperty("status").GetString());
        Assert.Equal(".", root.GetProperty("project_root").GetString());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("auto_commit_refresh_enabled").GetBoolean());
        Assert.False(root.GetProperty("commit_hook_installed").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("direct_project_crawl_enabled").GetBoolean());
        Assert.False(root.GetProperty("imports_raw_jsonl").GetBoolean());
        Assert.False(root.GetProperty("imports_generated_exports").GetBoolean());
        Assert.False(root.GetProperty("uses_generated_exports_as_source").GetBoolean());
        Assert.False(root.GetProperty("imports_secrets").GetBoolean());
        Assert.False(root.GetProperty("imports_local_proxy_details").GetBoolean());
        Assert.False(root.GetProperty("imports_build_artifacts").GetBoolean());
        Assert.False(root.GetProperty("touches_raw_jsonl").GetBoolean());
        Assert.False(root.GetProperty("touches_hindsight_store").GetBoolean());
        Assert.False(root.GetProperty("touches_secret_storage").GetBoolean());
        Assert.False(root.GetProperty("touches_build_artifacts").GetBoolean());
        Assert.True(root.GetProperty("memory_cli_checks_serialized").GetBoolean());
        Assert.Equal("docs/memory/generated/memory-cli.lock", root.GetProperty("memory_cli_lock_path").GetString());

        var steps = root.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(
            new[]
            {
                "legacy-json-refresh",
                "sqlite-refresh",
                "sqlite-stale-check",
                "lancedb-cleanup",
                "lancedb-rebuild",
                "lancedb-eval",
            },
            steps.Select(step => step.GetProperty("name").GetString()).ToArray());

        Assert.All(steps, step =>
        {
            Assert.Equal("planned", step.GetProperty("status").GetString());
            Assert.False(step.GetProperty("uses_cloud").GetBoolean());
            Assert.False(step.GetProperty("uses_hook").GetBoolean());
            var command = step.GetProperty("command").GetString()!.Replace('\\', '/');
            Assert.DoesNotContain("recordings/", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".hindsight/", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("docs/memory/generated/", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/bin/", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/obj/", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("publish/", command, StringComparison.OrdinalIgnoreCase);
        });

        var sqliteRefreshCommand = steps.Single(step => step.GetProperty("name").GetString() == "sqlite-refresh")
            .GetProperty("command")
            .GetString()!;
        var sqliteStaleCheck = steps.Single(step => step.GetProperty("name").GetString() == "sqlite-stale-check");
        var sqliteStaleCheckCommand = sqliteStaleCheck
            .GetProperty("command")
            .GetString()!;
        var sqliteRefreshStep = steps.Single(step => step.GetProperty("name").GetString() == "sqlite-refresh");

        Assert.True(sqliteRefreshStep.GetProperty("uses_memory_cli_lock").GetBoolean());
        Assert.True(sqliteStaleCheck.GetProperty("uses_memory_cli_lock").GetBoolean());
        Assert.Contains("run --no-restore --project", sqliteRefreshCommand, StringComparison.Ordinal);
        Assert.Contains("run --no-restore --project", sqliteStaleCheckCommand, StringComparison.Ordinal);
        Assert.Contains("refresh-from-commit", sqliteRefreshCommand, StringComparison.Ordinal);
        Assert.Contains("--commit HEAD", sqliteRefreshCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAllDocsKeepWrapperManualAndOutsideRuntime()
    {
        var readme = ReadText("docs/memory/README.md");
        var contract = ReadText("docs/memory/contract.md");

        Assert.Contains("scripts/memory-refresh-all.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("legacy JSON", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SQLite refresh", readme, StringComparison.Ordinal);
        Assert.Contains("LanceDB", readme, StringComparison.Ordinal);
        Assert.Contains("eval", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scripts/memory-rebuild-from-head.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("generated memory artifacts", readme, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("memory-refresh-all", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory-rebuild-from-head", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manual", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not install hooks", contract, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memory-refresh-all", ReadText("CryptoIndicatorApp.Desktop/CryptoIndicatorApp.Desktop.csproj"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memory-refresh-all", ReadText("CryptoIndicatorApp.Application/CryptoIndicatorApp.Application.csproj"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memory-rebuild-from-head", ReadText("CryptoIndicatorApp.Desktop/CryptoIndicatorApp.Desktop.csproj"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memory-rebuild-from-head", ReadText("CryptoIndicatorApp.Application/CryptoIndicatorApp.Application.csproj"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RebuildFromHeadPlanDeletesOnlyGeneratedMemoryArtifactsAndRunsRefresh()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-rebuild-from-head.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var reportPath = Path.Combine(Root, "docs", "memory", "generated", "memory-rebuild-from-head-report.json");
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

            Assert.Fail("memory-rebuild-from-head.ps1 plan timed out.");
        }

        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}\nSTDOUT:\n{output}\nSTDERR:\n{error}");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("scripts/memory-rebuild-from-head.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("plan-only", root.GetProperty("mode").GetString());
        Assert.Equal("planned", root.GetProperty("status").GetString());
        Assert.Equal(".", root.GetProperty("project_root").GetString());
        Assert.True(root.GetProperty("planned_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("deletes_raw_jsonl").GetBoolean());
        Assert.False(root.GetProperty("deletes_secrets").GetBoolean());
        Assert.False(root.GetProperty("deletes_build_artifacts").GetBoolean());
        Assert.False(root.GetProperty("deletes_hindsight_store").GetBoolean());
        Assert.False(root.GetProperty("deletes_source_files").GetBoolean());

        var plannedDeletes = root.GetProperty("delete_plan").EnumerateArray().ToArray();
        Assert.Contains(plannedDeletes, item => item.GetProperty("path").GetString() == "docs/memory/generated/project-memory.sqlite");
        Assert.Contains(plannedDeletes, item => item.GetProperty("path").GetString() == "docs/memory/generated/lancedb");
        Assert.Contains(plannedDeletes, item => item.GetProperty("path").GetString() == "docs/memory/generated/memory-needs-refresh.marker.json");

        Assert.All(plannedDeletes, item =>
        {
            Assert.True(item.GetProperty("under_generated_memory").GetBoolean());
            var path = item.GetProperty("path").GetString()!.Replace('\\', '/');
            Assert.StartsWith("docs/memory/generated/", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recordings/", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".hindsight/", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/bin/", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/obj/", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("publish/", path, StringComparison.OrdinalIgnoreCase);
        });

        var refreshCommand = root.GetProperty("refresh_all_command").GetString()!;
        Assert.Contains("scripts", refreshCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory-refresh-all.ps1", refreshCommand, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloneLikeRecoveryPlanWritesSafeReportWithoutRunningCloneOrRebuild()
    {
        var scriptPath = Path.Combine(Root, "scripts", "memory-clone-recovery-check.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var reportPath = Path.Combine(Root, "docs", "memory", "generated", "memory-clone-recovery-check-report.json");
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

            Assert.Fail("memory-clone-recovery-check.ps1 plan timed out.");
        }

        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}\nSTDOUT:\n{output}\nSTDERR:\n{error}");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("scripts/memory-clone-recovery-check.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("plan-only", root.GetProperty("mode").GetString());
        Assert.Equal("planned", root.GetProperty("status").GetString());
        Assert.Equal(".", root.GetProperty("project_root").GetString());
        Assert.True(root.GetProperty("manual_only").GetBoolean());
        Assert.True(root.GetProperty("requires_clean_working_tree").GetBoolean());
        Assert.True(root.GetProperty("planned_clone").GetBoolean());
        Assert.False(root.GetProperty("clone_created").GetBoolean());
        Assert.False(root.GetProperty("runs_recovery").GetBoolean());
        Assert.False(root.GetProperty("runs_refresh_all").GetBoolean());
        Assert.False(root.GetProperty("installs_hooks").GetBoolean());
        Assert.False(root.GetProperty("cloud_enabled").GetBoolean());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("post_commit_auto_refresh_enabled").GetBoolean());
        Assert.False(root.GetProperty("imports_raw_jsonl").GetBoolean());
        Assert.False(root.GetProperty("imports_generated_exports").GetBoolean());
        Assert.False(root.GetProperty("uses_generated_exports_as_source").GetBoolean());
        Assert.False(root.GetProperty("imports_secrets").GetBoolean());
        Assert.False(root.GetProperty("imports_build_artifacts").GetBoolean());
        Assert.False(root.GetProperty("touches_hindsight_store").GetBoolean());
        Assert.False(root.GetProperty("touches_secret_storage").GetBoolean());
        Assert.False(root.GetProperty("touches_build_artifacts").GetBoolean());
        Assert.True(root.GetProperty("clone_deleted").GetBoolean());

        var head = root.GetProperty("git").GetProperty("head").GetString();
        var tree = root.GetProperty("git").GetProperty("tree").GetString();
        Assert.False(string.IsNullOrWhiteSpace(head));
        Assert.False(string.IsNullOrWhiteSpace(tree));

        var commands = root.GetProperty("planned_commands");
        Assert.Contains("git clone", commands.GetProperty("clone").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("checkout --detach", commands.GetProperty("checkout").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory-rebuild-from-head.ps1", commands.GetProperty("recovery").GetString(), StringComparison.OrdinalIgnoreCase);

        var clonePath = root.GetProperty("clone").GetProperty("path").GetString()!.Replace('\\', '/');
        Assert.DoesNotContain("/recordings/", clonePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/docs/memory/generated/", clonePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/.hindsight/", clonePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/bin/", clonePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/obj/", clonePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/publish/", clonePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", clonePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CloneLikeRecoveryOwnershipMarkerDoesNotDirtyWorkingTree()
    {
        var script = ReadText("scripts/memory-clone-recovery-check.ps1");

        Assert.Contains("Get-OwnershipMarkerPath", script, StringComparison.Ordinal);
        Assert.Contains("Join-Path $gitDirectory 'memory-clone-recovery-check.marker'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Join-Path $ClonePath '.memory-clone-recovery-check'", script, StringComparison.Ordinal);
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
