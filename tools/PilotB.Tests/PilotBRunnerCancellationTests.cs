using System.Diagnostics;
using System.Globalization;
using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

public sealed class PilotBRunnerCancellationTests
{
    private const string ParentReadyMarker = ".pilot-b-fake-parent-ready";
    private const string ChildReadyMarker = ".pilot-b-fake-child-ready";
    private static readonly TimeSpan RunnerTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MarkerTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    [Fact]
    public async Task Runner_CallerCancellationBeforeOutput_TerminatesOwnedProcessAndRethrows()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        using var cancellation = new CancellationTokenSource();
        var request = fixture.CreateRequest("pilot-b.fake.cancel-before-output") with
        {
            Timeout = RunnerTimeout
        };
        var execution = new PilotBRunner().RunAsync(request, cancellation.Token);
        Process? ownedProcess = null;

        try
        {
            ownedProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ParentReadyMarker));

            cancellation.Cancel();

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await execution.WaitAsync(OperationTimeout));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
            await ownedProcess.WaitForExitAsync().WaitAsync(OperationTimeout);
            Assert.True(ownedProcess.HasExited);
            AssertUnsealedDiagnostics(request.ArtifactDirectory);
        }
        finally
        {
            cancellation.Cancel();
            await KillIfRunningAsync(ownedProcess);
            ownedProcess?.Dispose();
        }
    }

    [Fact]
    public async Task Runner_CallerCancellationDuringOutput_TerminatesOwnedProcessAndRethrows()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        using var cancellation = new CancellationTokenSource();
        var request = fixture.CreateRequest("pilot-b.fake.cancel-during-output") with
        {
            Timeout = RunnerTimeout
        };
        var execution = new PilotBRunner().RunAsync(request, cancellation.Token);
        Process? ownedProcess = null;

        try
        {
            ownedProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ParentReadyMarker));

            cancellation.Cancel();

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await execution.WaitAsync(OperationTimeout));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
            await ownedProcess.WaitForExitAsync().WaitAsync(OperationTimeout);
            Assert.True(ownedProcess.HasExited);
            AssertUnsealedDiagnostics(request.ArtifactDirectory);
        }
        finally
        {
            cancellation.Cancel();
            await KillIfRunningAsync(ownedProcess);
            ownedProcess?.Dispose();
        }
    }

    [Fact]
    public async Task Runner_CallerCancellation_TerminatesOwnedChildProcessTree()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        using var cancellation = new CancellationTokenSource();
        var request = fixture.CreateRequest("pilot-b.fake.cancel-with-child") with
        {
            Timeout = RunnerTimeout
        };
        var execution = new PilotBRunner().RunAsync(request, cancellation.Token);
        Process? parentProcess = null;
        Process? childProcess = null;

        try
        {
            parentProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ParentReadyMarker));
            childProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ChildReadyMarker));

            cancellation.Cancel();

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await execution.WaitAsync(OperationTimeout));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
            await parentProcess.WaitForExitAsync().WaitAsync(OperationTimeout);
            await childProcess.WaitForExitAsync().WaitAsync(OperationTimeout);
            Assert.True(parentProcess.HasExited);
            Assert.True(childProcess.HasExited);
            AssertUnsealedDiagnostics(request.ArtifactDirectory);
        }
        finally
        {
            cancellation.Cancel();
            await KillIfRunningAsync(parentProcess);
            await KillIfRunningAsync(childProcess);
            parentProcess?.Dispose();
            childProcess?.Dispose();
        }
    }

    [Fact]
    public async Task Runner_CallerCancellation_WhenTerminationFails_RethrowsWithSecondaryContext()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        using var cancellation = new CancellationTokenSource();
        var failure = new IOException("Injected process-tree termination failure.");
        var request = fixture.CreateRequest("pilot-b.fake.cancel-before-output") with
        {
            Timeout = RunnerTimeout
        };
        var execution = new PilotBRunner(new ThrowingProcessTreeTerminator(failure))
            .RunAsync(request, cancellation.Token);
        Process? ownedProcess = null;

        try
        {
            ownedProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ParentReadyMarker));

            cancellation.Cancel();

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await execution.WaitAsync(OperationTimeout));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.Same(failure, exception.Data["PilotB.ProcessTreeTerminationFailure"]);
            AssertUnsealedDiagnostics(request.ArtifactDirectory);
        }
        finally
        {
            cancellation.Cancel();
            await KillIfRunningAsync(ownedProcess);
            ownedProcess?.Dispose();
        }
    }

    [Fact]
    public async Task Runner_CallerCancellation_WhenProcessDoesNotStop_RethrowsWithSecondaryTimeout()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        using var cancellation = new CancellationTokenSource();
        var request = fixture.CreateRequest("pilot-b.fake.cancel-before-output") with
        {
            Timeout = RunnerTimeout
        };
        var execution = new PilotBRunner(new NonTerminatingProcessTreeTerminator())
            .RunAsync(request, cancellation.Token);
        Process? ownedProcess = null;

        try
        {
            ownedProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ParentReadyMarker));

            cancellation.Cancel();

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await execution.WaitAsync(OperationTimeout));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.IsType<TimeoutException>(
                exception.Data["PilotB.ProcessTreeTerminationFailure"]);
            AssertUnsealedDiagnostics(request.ArtifactDirectory);
        }
        finally
        {
            cancellation.Cancel();
            await KillIfRunningAsync(ownedProcess);
            ownedProcess?.Dispose();
        }
    }

    [Fact]
    public async Task ProcessTreeTerminator_AlreadyExitedProcess_IsSuccessful()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fixture.FakeExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("unexpected-invocation");

        Assert.True(process.Start());
        await process.WaitForExitAsync().WaitAsync(OperationTimeout);
        Assert.True(process.HasExited);

        new PilotBProcessTreeTerminator().Terminate(process);

        Assert.True(process.HasExited);
    }

    private static async Task<Process> WaitForProcessMarkerAsync(string markerPath)
    {
        var deadline = DateTimeOffset.UtcNow.Add(MarkerTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(markerPath)
                    && TryParseProcessMarker(
                        await File.ReadAllTextAsync(markerPath),
                        out var processId,
                        out var startedAtTicks))
                {
                    var process = Process.GetProcessById(processId);
                    if (!process.HasExited
                        && process.StartTime.ToUniversalTime().Ticks == startedAtTicks)
                    {
                        return process;
                    }

                    process.Dispose();
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            {
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException($"Timed out waiting for process marker '{markerPath}'.");
    }

    private static async Task KillIfRunningAsync(Process? process)
    {
        if (process is null || process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return;
        }

        await process.WaitForExitAsync().WaitAsync(OperationTimeout);
    }

    private static string MarkerPath(PilotBRunnerTestFixture fixture, string markerName)
        => Path.Combine(fixture.Root, "markers", markerName);

    private static void AssertUnsealedDiagnostics(string artifactDirectory)
    {
        Assert.True(Directory.Exists(artifactDirectory));
        Assert.True(File.Exists(Path.Combine(artifactDirectory, "prompt.bin")));
        Assert.True(File.Exists(Path.Combine(artifactDirectory, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(artifactDirectory, "pre-manifest.json")));
        Assert.False(File.Exists(Path.Combine(artifactDirectory, "metadata.json")));
        Assert.False(File.Exists(Path.Combine(artifactDirectory, "integrity.json")));
        Assert.False(File.Exists(Path.Combine(artifactDirectory, "integrity.json.tmp")));
        Assert.False(File.Exists(Path.Combine(artifactDirectory, ".pilot-b-write-lock")));
    }

    private static bool TryParseProcessMarker(
        string marker,
        out int processId,
        out long startedAtTicks)
    {
        processId = default;
        startedAtTicks = default;
        var parts = marker.Split('|');
        return parts.Length == 2
               && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out processId)
               && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out startedAtTicks);
    }

    private sealed class ThrowingProcessTreeTerminator(Exception failure) : IPilotBProcessTreeTerminator
    {
        public void Terminate(Process process)
        {
            throw failure;
        }
    }

    private sealed class NonTerminatingProcessTreeTerminator : IPilotBProcessTreeTerminator
    {
        public void Terminate(Process process)
        {
        }
    }
}
