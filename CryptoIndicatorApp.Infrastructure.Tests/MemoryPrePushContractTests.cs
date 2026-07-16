using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class MemoryPrePushContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Theory]
    [InlineData("cloud_enabled", "\"false\"")]
    [InlineData("passed", "\"true\"")]
    [InlineData("failed_count", "\"0\"")]
    [InlineData("passed_count", "\"9\"")]
    public void EvalReportRejectsJsonScalarsWithCoercibleWrongTypes(string field, string invalidJsonValue)
    {
        using var temp = TemporaryDirectory.Create();
        var reportPath = Path.Combine(temp.Path, "eval-report.json");
        var report = CreatePassingEvalReport();
        report[field] = JsonNode.Parse(invalidJsonValue);
        File.WriteAllText(reportPath, report.ToJsonString());

        var probePath = Path.Combine(temp.Path, "probe.ps1");
        var contractPath = Path.Combine(Root, "scripts", "memory-pre-push-contract.ps1");
        File.WriteAllText(
            probePath,
            $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            . '{{EscapePowerShellLiteral(contractPath)}}'
            $report = Get-Content -Raw -LiteralPath '{{EscapePowerShellLiteral(reportPath)}}' | ConvertFrom-Json
            Test-LanceDbEvalReport -Report $report -MinimumEvalCases 9 | ConvertTo-Json -Compress
            """);

        var result = RunPowerShell(probePath);

        Assert.Equal(0, result.ExitCode);
        using var output = JsonDocument.Parse(result.StandardOutput);
        Assert.False(output.RootElement.GetProperty("passed").GetBoolean());
        Assert.Contains(field, output.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"false\"")]
    [InlineData("0")]
    public void FalseFlagContractRejectsCoercibleWrongTypes(string invalidJsonValue)
    {
        using var temp = TemporaryDirectory.Create();
        var reportPath = Path.Combine(temp.Path, "report.json");
        File.WriteAllText(reportPath, $$"""{ "flag": {{invalidJsonValue}} }""");

        var probePath = Path.Combine(temp.Path, "probe.ps1");
        var contractPath = Path.Combine(Root, "scripts", "memory-pre-push-contract.ps1");
        File.WriteAllText(
            probePath,
            $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            . '{{EscapePowerShellLiteral(contractPath)}}'
            $report = Get-Content -Raw -LiteralPath '{{EscapePowerShellLiteral(reportPath)}}' | ConvertFrom-Json
            [ordered]@{ accepted = Test-JsonPropertyFalse -Object $report -Name 'flag' } | ConvertTo-Json -Compress
            """);

        var result = RunPowerShell(probePath);

        Assert.Equal(0, result.ExitCode);
        using var output = JsonDocument.Parse(result.StandardOutput);
        Assert.False(output.RootElement.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public void GitInvokerReturnsStructuredUnavailableEvidence()
    {
        using var temp = TemporaryDirectory.Create();
        var probePath = Path.Combine(temp.Path, "probe.ps1");
        var contractPath = Path.Combine(Root, "scripts", "memory-pre-push-contract.ps1");
        File.WriteAllText(
            probePath,
            $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            . '{{EscapePowerShellLiteral(contractPath)}}'
            $env:PATH = ''
            Invoke-GitText -Root '{{EscapePowerShellLiteral(temp.Path)}}' -Arguments @('rev-parse', 'HEAD') -TimeoutMilliseconds 1234 | ConvertTo-Json -Compress
            """);

        var result = RunPowerShell(probePath);

        Assert.Equal(0, result.ExitCode);
        using var output = JsonDocument.Parse(result.StandardOutput);
        var root = output.RootElement;
        Assert.False(root.GetProperty("succeeded").GetBoolean());
        Assert.Equal("git-unavailable", root.GetProperty("failure_code").GetString());
        Assert.False(root.GetProperty("timed_out").GetBoolean());
        Assert.Equal(1234, root.GetProperty("timeout_ms").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("exit_code").ValueKind);
    }

    [Theory]
    [InlineData(false, "git-timeout", true, "resolve-head")]
    [InlineData(true, "git-unavailable", false, "resolve-tree")]
    public void CommitFreshnessReturnsStructuredGitFailureEvidence(
        bool failOnTree,
        string failureCode,
        bool timedOut,
        string expectedOperation)
    {
        using var temp = TemporaryDirectory.Create();
        var probePath = Path.Combine(temp.Path, "probe.ps1");
        var contractPath = Path.Combine(Root, "scripts", "memory-pre-push-contract.ps1");
        var failOnTreeLiteral = failOnTree ? "$true" : "$false";
        var timedOutLiteral = timedOut ? "$true" : "$false";
        File.WriteAllText(
            probePath,
            $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            . '{{EscapePowerShellLiteral(contractPath)}}'
            $script:gitCall = 0
            $gitInvoker = {
                param($Root, $Arguments, $TimeoutMilliseconds)
                $script:gitCall++
                if ({{failOnTreeLiteral}} -and $script:gitCall -eq 1) {
                    return [ordered]@{ succeeded = $true; output = ('a' * 40); error = ''; failure_code = ''; timed_out = $false; timeout_ms = $TimeoutMilliseconds; exit_code = 0 }
                }
                return [ordered]@{ succeeded = $false; output = ''; error = 'injected failure'; failure_code = '{{failureCode}}'; timed_out = {{timedOutLiteral}}; timeout_ms = $TimeoutMilliseconds; exit_code = $null }
            }
            $report = [pscustomobject]@{ commit_sha = ('a' * 40); tree_sha = ('b' * 40) }
            Test-CommitAddressedFreshness -Root '{{EscapePowerShellLiteral(temp.Path)}}' -RefreshReport $report -EvalReport $report -GitInvoker $gitInvoker | ConvertTo-Json -Depth 8 -Compress
            """);

        var result = RunPowerShell(probePath);

        Assert.Equal(0, result.ExitCode);
        using var output = JsonDocument.Parse(result.StandardOutput);
        var root = output.RootElement;
        Assert.False(root.GetProperty("passed").GetBoolean());
        var evidence = root.GetProperty("evidence");
        Assert.Equal(expectedOperation, evidence.GetProperty("operation").GetString());
        Assert.Equal(failureCode, evidence.GetProperty("failure_code").GetString());
        Assert.Equal(timedOut, evidence.GetProperty("timed_out").GetBoolean());
        Assert.Equal(10000, evidence.GetProperty("timeout_ms").GetInt32());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("exit_code").ValueKind);
    }

    private static JsonObject CreatePassingEvalReport()
    {
        return new JsonObject
        {
            ["generator"] = "tools/MemorySemantic/lancedb_sidecar.py",
            ["command"] = "eval",
            ["status"] = "ok",
            ["source_store"] = "sqlite-fts5",
            ["cloud_enabled"] = false,
            ["auto_commit_refresh_enabled"] = false,
            ["direct_project_crawl_enabled"] = false,
            ["commit_hook_installed"] = false,
            ["passed"] = true,
            ["failed_count"] = 0,
            ["passed_count"] = 9,
        };
    }

    private static ProcessResult RunPowerShell(string scriptPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);
        var outputTask = process!.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "PowerShell contract probe timed out.");
        return new ProcessResult(
            process.ExitCode,
            outputTask.GetAwaiter().GetResult(),
            errorTask.GetAwaiter().GetResult());
    }

    private static string EscapePowerShellLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "memory-pre-push-contract-test-" + Guid.NewGuid().ToString("N"));
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
