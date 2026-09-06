using System.Diagnostics;
using System.Globalization;
using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

[Collection(PilotBProcessBackedRunnerCollection.Name)]
public sealed class PilotBRunnerTimeoutDeadlineTests
{
    private const string ParentReadyMarker = ".pilot-b-fake-parent-ready";
    private const string ParentExitReadyMarker = ".pilot-b-fake-parent-exit-ready";
    private const string ChildReadyMarker = ".pilot-b-fake-parent-exit-child-ready";
    private const string NoReadMarker = ".pilot-b-fake-no-read-stdin";
    private static readonly TimeSpan ProcessObservationTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MarkerTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan DeadlineTimeout = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task Runner_DeadlineIncludesUnconsumedStdin_SealsControlledInvalidEvidence()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        File.WriteAllText(Path.Combine(fixture.FixtureRoot, NoReadMarker), "no-read\n");
        var request = fixture.CreateRequest("pilot-b.fake.no-read-stdin") with
        {
            PromptBytes = new byte[4 * 1024 * 1024],
            Timeout = DeadlineTimeout
        };
        var execution = new PilotBRunner().RunAsync(request);
        Process? ownedProcess = null;
        Exception? executionFailure = null;
        PilotBRunnerResult? result = null;

        try
        {
            ownedProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ParentReadyMarker));
            try
            {
                result = await execution.WaitAsync(ProcessObservationTimeout);
                Assert.True(ownedProcess.WaitForExit(ProcessObservationTimeout));
                Assert.True(ownedProcess.HasExited);
            }
            catch (Exception exception)
            {
                executionFailure = exception;
            }
        }
        finally
        {
            KillIfRunning(ownedProcess);
            await ObserveCompletionAsync(execution);
            ownedProcess?.Dispose();
        }

        Assert.Null(executionFailure);
        Assert.NotNull(result);
        Assert.Equal(PilotBEvidenceState.Sealed, result.EvidenceState);
        Assert.Equal(PilotBRunValidity.Invalid, result.RunValidity);
        Assert.True(result.TimedOut);
        Assert.Contains("timeout", result.InvalidReasons);
        Assert.Matches("^[0-9a-f]{64}$", result.DeterministicFingerprint!);
        var verified = new PilotBEvidenceBundleVerifier().Verify(result.Artifacts);
        Assert.Equal(PilotBEvidenceState.Sealed, verified.EvidenceState);
        Assert.Equal(result.InvalidReasons, verified.Qualification!.InvalidReasons);
        Assert.Equal(result.DeterministicFingerprint, verified.SemanticFingerprint);
    }

    [Fact]
    public async Task Runner_CallerCancellationDuringUnconsumedStdin_PreservesCallerCancellation()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        File.WriteAllText(Path.Combine(fixture.FixtureRoot, NoReadMarker), "no-read\n");
        using var cancellation = new CancellationTokenSource();
        var request = fixture.CreateRequest("pilot-b.fake.no-read-stdin") with
        {
            PromptBytes = new byte[4 * 1024 * 1024],
            Timeout = TimeSpan.FromSeconds(10)
        };
        var execution = new PilotBRunner().RunAsync(request, cancellation.Token);
        Process? ownedProcess = null;
        Exception? executionFailure = null;

        try
        {
            ownedProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ParentReadyMarker));
            cancellation.Cancel();
            try
            {
                await execution.WaitAsync(ProcessObservationTimeout);
            }
            catch (Exception exception)
            {
                executionFailure = exception;
            }
            Assert.True(ownedProcess.WaitForExit(ProcessObservationTimeout));
            Assert.True(ownedProcess.HasExited);
        }
        finally
        {
            cancellation.Cancel();
            KillIfRunning(ownedProcess);
            await ObserveCompletionAsync(execution);
            ownedProcess?.Dispose();
        }

        var operationCanceled = Assert.IsAssignableFrom<OperationCanceledException>(executionFailure);
        Assert.Equal(cancellation.Token, operationCanceled.CancellationToken);
    }

    [Fact]
    public async Task Runner_ParentExitWithDescendantHoldingPipe_ReturnsBoundedUnsealedResult()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var request = fixture.CreateRequest("pilot-b.fake.parent-exit-child-holds-pipe") with
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        var execution = new PilotBRunner().RunAsync(request);
        Process? parentProcess = null;
        Process? childProcess = null;
        Exception? executionFailure = null;
        PilotBRunnerResult? result = null;

        try
        {
            parentProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ParentExitReadyMarker));
            childProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ChildReadyMarker));
            await parentProcess.WaitForExitAsync().WaitAsync(ProcessObservationTimeout);
            Assert.True(parentProcess.HasExited);
            Assert.Equal(0, parentProcess.ExitCode);
            Assert.False(childProcess.HasExited);
            try
            {
                result = await execution.WaitAsync(ProcessObservationTimeout);
            }
            catch (Exception exception)
            {
                executionFailure = exception;
            }
        }
        finally
        {
            KillIfRunning(childProcess);
            KillIfRunning(parentProcess);
            await ObserveCompletionAsync(execution);
            childProcess?.Dispose();
            parentProcess?.Dispose();
        }

        Assert.Null(executionFailure);
        Assert.NotNull(result);
        Assert.Equal(PilotBEvidenceState.Unsealed, result.EvidenceState);
        Assert.Null(result.RunValidity);
        Assert.Null(result.DeterministicFingerprint);
        Assert.Contains("timeout", result.InvalidReasons);
        Assert.Contains("timeout-termination-incomplete", result.InvalidReasons);
        Assert.False(File.Exists(result.Artifacts.SealPath));
        Assert.False(File.Exists(Path.Combine(result.Artifacts.Root, "metadata.json")));
    }

    private static string MarkerPath(PilotBRunnerTestFixture fixture, string markerName)
        => Path.Combine(fixture.Root, "markers", markerName);

    [Fact]
    public async Task Runner_CancellationDuringTimeoutCleanup_PreservesSecondaryTerminationFailure()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        using var cancellation = new CancellationTokenSource();
        var failure = new IOException("Injected timeout cleanup failure.");
        var request = fixture.CreateRequest("pilot-b.fake.cancel-before-output") with
        {
            Timeout = TimeSpan.FromMilliseconds(500)
        };
        var execution = new PilotBRunner(new CancelingTerminator(cancellation, failure))
            .RunAsync(request, cancellation.Token);
        Process? ownedProcess = null;
        try
        {
            ownedProcess = await WaitForProcessMarkerAsync(MarkerPath(fixture, ParentReadyMarker));
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await execution.WaitAsync(ProcessObservationTimeout));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.Same(failure, exception.Data["PilotB.ProcessTreeTerminationFailure"]);
            Assert.False(File.Exists(Path.Combine(request.ArtifactDirectory, "integrity.json")));
        }
        finally
        {
            cancellation.Cancel();
            KillIfRunning(ownedProcess);
            await ObserveCompletionAsync(execution);
            ownedProcess?.Dispose();
        }
    }

    private sealed class CancelingTerminator(CancellationTokenSource cancellation, Exception failure)
        : IPilotBProcessTreeTerminator
    {
        public void Terminate(Process process)
        {
            cancellation.Cancel();
            throw failure;
        }
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
                    Process? process = Process.GetProcessById(processId);
                    try
                    {
                        if (!process.HasExited
                            && process.StartTime.ToUniversalTime().Ticks == startedAtTicks)
                        {
                            _ = process.SafeHandle;
                            var ownedProcess = process;
                            process = null;
                            return ownedProcess;
                        }
                    }
                    finally
                    {
                        process?.Dispose();
                    }
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
            {
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException($"Timed out waiting for process marker '{markerPath}'.");
    }

    private static void KillIfRunning(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
        finally
        {
            if (!process.WaitForExit(ProcessObservationTimeout))
            {
                throw new TimeoutException("Owned process did not signal termination during cleanup.");
            }
        }
    }

    private static async Task ObserveCompletionAsync(Task execution)
    {
        _ = await Record.ExceptionAsync(
            async () => await execution.WaitAsync(ProcessObservationTimeout));
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
}
