using System.Diagnostics;
using Xunit.Abstractions;

namespace CryptoIndicatorApp.Memory.Tests;

internal sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed class MemoryProjectFixture : IDisposable
{
    private const int CleanupAttemptCount = 5;
    private static readonly TimeSpan CleanupRetryDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ProcessCleanupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProcessOutputDrainTimeout = TimeSpan.FromSeconds(5);
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string DotnetPath = File.Exists(Path.Combine(RepositoryRoot, ".dotnet", "dotnet.exe"))
        ? Path.Combine(RepositoryRoot, ".dotnet", "dotnet.exe")
        : "dotnet";
#if DEBUG
    private const string DotnetConfiguration = "Debug";
#else
    private const string DotnetConfiguration = "Release";
#endif
    private static readonly string GitPath = File.Exists(@"C:\Program Files\Git\cmd\git.exe")
        ? @"C:\Program Files\Git\cmd\git.exe"
        : "git";

    private readonly ITestOutputHelper _output;
    private readonly Action<string> _deleteDirectory;
    private Exception? primaryFailure;

    internal const string CleanupFailuresDataKey =
        "CryptoIndicatorApp.Memory.Tests.MemoryProjectFixture.CleanupFailures";

    private MemoryProjectFixture(string root, ITestOutputHelper output, Action<string> deleteDirectory)
    {
        _output = output;
        _deleteDirectory = deleteDirectory;
        Root = root;
        DatabasePath = Path.Combine(root, "docs", "memory", "generated", "project-memory.sqlite");
    }

    public string Root { get; }

    public string DatabasePath { get; }

    public static MemoryProjectFixture Create(ITestOutputHelper output, Action<string>? deleteDirectory = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "tc-dn-hofi3-memory-partial-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new MemoryProjectFixture(
            root,
            output,
            deleteDirectory ?? (path => Directory.Delete(path, recursive: true)));
    }

    public void Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public CliResult RunMemoryCli(params string[] arguments)
    {
        var projectPath = Path.Combine(RepositoryRoot, "tools", "Memory", "CryptoIndicatorApp.Memory.csproj");
        var startInfo = new ProcessStartInfo
        {
            FileName = DotnetPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepositoryRoot,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(DotnetConfiguration);
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("--project-root");
        startInfo.ArgumentList.Add(Root);
        startInfo.ArgumentList.Add("--db");
        startInfo.ArgumentList.Add(DatabasePath);

        return RunProcess(startInfo, TimeSpan.FromSeconds(120), "memory CLI");
    }

    public void InitializeGitRepository()
    {
        RunGit("init");
        RunGit("config", "user.name", "Memory Partial Test");
        RunGit("config", "user.email", "memory-partial-test@example.invalid");
        RunGit("add", ".");
        RunGit("commit", "-m", "partial declaration fixture");
    }

    public string RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GitPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Root,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            var result = RunProcess(
                startInfo,
                TimeSpan.FromSeconds(30),
                $"git {string.Join(' ', arguments)}");
            Assert.True(
                result.ExitCode == 0,
                $"git {string.Join(' ', arguments)} failed with exit code {result.ExitCode}\n"
                + $"STDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
            return result.StandardOutput;
        }
        catch (Exception exception)
        {
            RecordPrimaryFailure(exception);
            throw;
        }
    }

    public void RecordPrimaryFailure(Exception exception)
    {
        primaryFailure ??= exception;
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        try
        {
            ClearReadOnlyAttributes();
            DeleteDirectoryWithRetry();
        }
        catch (Exception cleanupFailure)
        {
            if (primaryFailure is null)
            {
                throw;
            }

            RecordCleanupFailure(primaryFailure, cleanupFailure, "fixture-dispose");
        }
    }

    private void ClearReadOnlyAttributes()
    {
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (var directory in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Directory);
        }
    }

    private void DeleteDirectoryWithRetry()
    {
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= CleanupAttemptCount; attempt++)
        {
            try
            {
                _deleteDirectory(Root);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastFailure = exception;
                if (attempt < CleanupAttemptCount)
                {
                    Thread.Sleep(CleanupRetryDelay);
                }
            }
        }

        throw new IOException(
            $"Failed to delete fixture directory '{Root}' after {CleanupAttemptCount} attempts.",
            lastFailure);
    }

    private CliResult RunProcess(ProcessStartInfo startInfo, TimeSpan timeout, string operation)
    {
        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            Assert.NotNull(process);
            var stdoutTask = process!.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                var timeoutFailure = new TimeoutException(
                    $"{operation} timed out after {timeout.TotalSeconds:0} seconds.");
                RecordPrimaryFailure(timeoutFailure);

                var cleanupFailures = new List<Exception>();
                CollectTimedOutProcessCleanup(process, cleanupFailures);
                try
                {
                    _ = DrainProcessOutput(stdoutTask, stderrTask);
                }
                catch (Exception cleanupFailure)
                {
                    cleanupFailures.Add(cleanupFailure);
                }

                foreach (var cleanupFailure in cleanupFailures)
                {
                    RecordCleanupFailure(timeoutFailure, cleanupFailure, "process-cleanup");
                }

                throw timeoutFailure;
            }

            var output = DrainProcessOutput(stdoutTask, stderrTask);
            return new CliResult(process.ExitCode, output.StandardOutput, output.StandardError);
        }
        catch (Exception exception)
        {
            RecordPrimaryFailure(exception);
            throw;
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    process.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    if (primaryFailure is null)
                    {
                        throw;
                    }

                    RecordCleanupFailure(primaryFailure, cleanupFailure, "process-dispose");
                }
            }
        }
    }

    private static void CollectTimedOutProcessCleanup(Process process, ICollection<Exception> failures)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            if (!process.WaitForExit(ProcessCleanupTimeout))
            {
                failures.Add(new TimeoutException(
                    $"Timed out waiting {ProcessCleanupTimeout.TotalSeconds:0} seconds for the {nameof(Process)} to exit after termination."));
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static (string StandardOutput, string StandardError) DrainProcessOutput(
        Task<string> stdoutTask,
        Task<string> stderrTask)
    {
        try
        {
            var output = Task.WhenAll(stdoutTask, stderrTask)
                .WaitAsync(ProcessOutputDrainTimeout)
                .GetAwaiter()
                .GetResult();
            return (output[0], output[1]);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"Timed out draining process output after {ProcessOutputDrainTimeout.TotalSeconds:0} seconds.",
                exception);
        }
    }

    private void RecordCleanupFailure(Exception primary, Exception cleanupFailure, string stage)
    {
        if (primary.Data[CleanupFailuresDataKey] is not List<Exception> cleanupFailures)
        {
            cleanupFailures = [];
            primary.Data[CleanupFailuresDataKey] = cleanupFailures;
        }

        cleanupFailures.Add(cleanupFailure);
        try
        {
            _output.WriteLine($"stage={stage} primary={primary.GetType().FullName} secondary={cleanupFailure}");
        }
        catch (Exception outputFailure)
        {
            // An unavailable test-output sink must not replace the original process failure.
            cleanupFailures.Add(outputFailure);
        }
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
