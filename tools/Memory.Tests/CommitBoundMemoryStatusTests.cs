using System.Diagnostics;

namespace CryptoIndicatorApp.Memory.Tests;

public sealed class CommitBoundMemoryStatusTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string DotnetPath = File.Exists(Path.Combine(RepositoryRoot, ".dotnet", "dotnet.exe"))
        ? Path.Combine(RepositoryRoot, ".dotnet", "dotnet.exe")
        : "dotnet";

    [Fact]
    public void StatusFailsClosedWhenGitHeadIsUnavailable()
    {
        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "CryptoIndicatorApp.sln"), string.Empty);
        var databasePath = Path.Combine(temp.Path, "project-memory.sqlite");

        using var result = RunMemoryCli(
            "status",
            "--project-root",
            temp.Path,
            "--db",
            databasePath,
            "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Git HEAD", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput), result.StandardOutput);
    }

    private static CliResult RunMemoryCli(params string[] arguments)
    {
        var projectPath = Path.Combine(RepositoryRoot, "tools", "Memory", "CryptoIndicatorApp.Memory.csproj");
        var startInfo = new ProcessStartInfo
        {
            FileName = DotnetPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(120)), "memory CLI timed out.");
        return new CliResult(
            process.ExitCode,
            stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult());
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

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError) : IDisposable
    {
        public void Dispose()
        {
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
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "memory-status-test-" + Guid.NewGuid().ToString("N"));
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
