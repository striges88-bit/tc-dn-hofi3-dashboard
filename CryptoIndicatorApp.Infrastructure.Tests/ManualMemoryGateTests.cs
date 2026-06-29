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
        Assert.False(root.GetProperty("pre_push_hook_installed").GetBoolean());
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
    public void ManualMemoryGateDocsRejectPostCommitAutomation()
    {
        var adr = ReadText("docs/decisions/0005-manual-memory-gate.md");
        var contract = ReadText("docs/memory/contract.md");
        var scriptsReadme = ReadText("scripts/README.md");
        var lessons = ReadText("tasks/lessons.md");

        Assert.Contains("manual memory gate", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory-pre-push-check.ps1", adr, StringComparison.Ordinal);
        Assert.Contains("post-commit", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not add", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory-pre-push-check", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manual", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no post-commit", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("memory-pre-push-check.ps1", scriptsReadme, StringComparison.Ordinal);
        Assert.Contains("memory-pre-push-check", lessons, StringComparison.OrdinalIgnoreCase);
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
