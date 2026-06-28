using System.Diagnostics;
using System.Text.Json;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class HindsightInstallSpikeTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void InstallSpikeScriptWritesSafeGeneratedReportWithoutRetainOrAutoHooks()
    {
        var scriptPath = Path.Combine(Root, "scripts", "hindsight-install-spike.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var reportPath = Path.Combine(Root, "docs", "memory", "generated", "hindsight-install-spike-report.json");
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ProjectRoot \"{Root}\" -OutputPath \"{reportPath}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);
        Assert.True(process!.WaitForExit(TimeSpan.FromSeconds(30)), "hindsight-install-spike.ps1 timed out.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}\nSTDOUT:\n{output}\nSTDERR:\n{error}");
        Assert.True(File.Exists(reportPath), $"Missing report: {reportPath}");

        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("scripts/hindsight-install-spike.ps1", root.GetProperty("generator").GetString());
        Assert.Equal("python-uvx-embedded-daemon", root.GetProperty("mode").GetString());
        Assert.False(root.GetProperty("codex_auto_retain_enabled").GetBoolean());
        Assert.False(root.GetProperty("curated_import_executed").GetBoolean());
        Assert.False(root.GetProperty("network_install_executed").GetBoolean());
        Assert.False(root.GetProperty("daemon_started").GetBoolean());
        Assert.Contains("docs/memory/generated/", File.ReadAllText(Path.Combine(Root, ".gitignore")), StringComparison.Ordinal);

        var checks = root.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "python");
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "py");
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "uv");
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "uvx");
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "hindsight");

        var nextActions = root.GetProperty("next_actions").EnumerateArray()
            .Select(action => action.GetString()!)
            .ToArray();
        Assert.Contains(nextActions, action => action.Contains("uv", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nextActions, action => action.Contains("OPENAI_API_KEY", StringComparison.Ordinal));
    }

    [Fact]
    public void InstallSpikeDocsKeepEmbeddedDaemonSeparateFromCuratedImport()
    {
        var spike = ReadText("docs/memory/hindsight-install-spike.md");
        var hindsight = ReadText("docs/memory/hindsight-spike.md");
        var contract = ReadText("docs/memory/contract.md");
        var openQuestions = ReadText("docs/memory/open-questions.md");

        Assert.Contains("Python/uvx embedded daemon", spike, StringComparison.Ordinal);
        Assert.Contains("uvx hindsight-embed", spike, StringComparison.Ordinal);
        Assert.Contains("Codex auto-retain remains disabled", spike, StringComparison.Ordinal);
        Assert.Contains("do not run `retain-files`", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("curated import manifest", spike, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("9077", spike, StringComparison.Ordinal);
        Assert.Contains("8888", spike, StringComparison.Ordinal);

        Assert.Contains("docs/memory/hindsight-install-spike.md", hindsight, StringComparison.Ordinal);
        Assert.Contains("Python/uvx embedded daemon", contract, StringComparison.Ordinal);
        Assert.Contains("install-spike report", openQuestions, StringComparison.OrdinalIgnoreCase);
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
