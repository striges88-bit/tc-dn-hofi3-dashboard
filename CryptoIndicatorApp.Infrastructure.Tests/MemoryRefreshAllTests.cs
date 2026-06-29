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
        Assert.False(root.GetProperty("direct_project_crawl_enabled").GetBoolean());

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
            Assert.DoesNotContain("recordings", step.GetProperty("command").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".hindsight", step.GetProperty("command").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", step.GetProperty("command").GetString(), StringComparison.OrdinalIgnoreCase);
        });
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

        Assert.Contains("memory-refresh-all", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manual", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not install hooks", contract, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memory-refresh-all", ReadText("CryptoIndicatorApp.Desktop/CryptoIndicatorApp.Desktop.csproj"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memory-refresh-all", ReadText("CryptoIndicatorApp.Application/CryptoIndicatorApp.Application.csproj"), StringComparison.OrdinalIgnoreCase);
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
