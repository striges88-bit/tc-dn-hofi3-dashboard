using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed partial class CommitBoundMemoryGateTests
{
    [Theory]
    [InlineData("uses_cloud", "\"false\"", "refresh-all-safety-flags")]
    [InlineData("uses_hook", "\"false\"", "refresh-all-safety-flags")]
    [InlineData("exit_code", "\"0\"", "refresh-all-steps-completed")]
    public async Task PrePushRejectsRefreshStepJsonTypeLookalikes(
        string field,
        string invalidJsonValue,
        string expectedCheckName)
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
        var refreshReportPath = Path.Combine(generated, "memory-refresh-all-report.json");
        WriteCompletedRefreshReport(refreshReportPath, commit, tree);
        RewriteRefreshStepProperty(refreshReportPath, field, invalidJsonValue);
        WritePassingEvalReport(Path.Combine(generated, "lancedb-sidecar-report.json"), commit, tree);
        WriteIndexManifest(Path.Combine(generated, "lancedb-manifest.json"), commit, tree);
        File.WriteAllText(Path.Combine(generated, "lancedb-eval-report.md"), "# Current eval report\n");
        File.WriteAllText(Path.Combine(generated, "project-memory.sqlite"), string.Empty);
        var outputPath = fixture.PathFor("memory-pre-push-check-report.json");

        var result = await RunPowerShellAsync(
            scriptPath,
            $"-ProjectRoot {Quote(fixture.Root)} -OutputPath {Quote(outputPath)}");

        Assert.NotEqual(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(outputPath));
        var check = report.RootElement.GetProperty("checks")
            .EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == expectedCheckName);
        Assert.Equal("failed", check.GetProperty("status").GetString());
        Assert.Contains(field, check.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrePushPreservesStructuredGitUnavailableEvidence()
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
        WriteIndexManifest(Path.Combine(generated, "lancedb-manifest.json"), commit, tree);
        File.WriteAllText(Path.Combine(generated, "lancedb-eval-report.md"), "# Current eval report\n");
        File.WriteAllText(Path.Combine(generated, "project-memory.sqlite"), string.Empty);
        var outputPath = fixture.PathFor("memory-pre-push-check-report.json");

        var result = await RunPowerShellWithoutPathAsync(
            scriptPath,
            $"-ProjectRoot {Quote(fixture.Root)} -OutputPath {Quote(outputPath)}");

        Assert.NotEqual(0, result.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(outputPath));
        var freshness = report.RootElement.GetProperty("checks")
            .EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "commit-addressed-freshness");
        Assert.Equal("failed", freshness.GetProperty("status").GetString());
        var evidence = freshness.GetProperty("evidence");
        Assert.Equal("resolve-head", evidence.GetProperty("operation").GetString());
        Assert.Equal("git-unavailable", evidence.GetProperty("failure_code").GetString());
        Assert.False(evidence.GetProperty("timed_out").GetBoolean());
        Assert.Equal(10000, evidence.GetProperty("timeout_ms").GetInt32());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("exit_code").ValueKind);
    }

    private static void RewriteRefreshStepProperty(string path, string field, string invalidJsonValue)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var firstStep = root["steps"]!.AsArray()[0]!.AsObject();
        firstStep[field] = JsonNode.Parse(invalidJsonValue);
        File.WriteAllText(path, root.ToJsonString());
    }

    private static async Task<ProcessResult> RunPowerShellWithoutPathAsync(string scriptPath, string arguments)
    {
        var powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = powerShellPath,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["PATH"] = string.Empty;

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var outputTask = process!.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(cts.Token);
        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }
}
