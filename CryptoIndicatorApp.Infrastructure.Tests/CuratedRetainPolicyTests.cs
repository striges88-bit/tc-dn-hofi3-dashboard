using System.Diagnostics;
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

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
