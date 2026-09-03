using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CryptoIndicatorApp.PilotB;
using Xunit.Abstractions;

namespace CryptoIndicatorApp.PilotB.Tests;

[Collection(PilotBProcessBackedRunnerCollection.Name)]
public sealed class PilotBRunnerCancellationTests(ITestOutputHelper output)
{
    private const string ParentReadyMarker = ".pilot-b-fake-parent-ready";
    private const string ChildReadyMarker = ".pilot-b-fake-child-ready";
    private static readonly TimeSpan RunnerTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MarkerTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan ControlledTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ChildTreeTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FaultInjectionTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly string[] ControlledTimeoutInvalidReasons =
    [
        "timeout",
        "empty-transcript",
        "missing-thread-started",
        "missing-turn-started",
        "partial-run",
        "timing-violation"
    ];

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
            KillIfRunning(ownedProcess);
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
            KillIfRunning(ownedProcess);
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
            KillIfRunning(parentProcess);
            KillIfRunning(childProcess);
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
            KillIfRunning(ownedProcess);
            await ObserveCompletionAsync(execution);
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
            KillIfRunning(ownedProcess);
            await ObserveCompletionAsync(execution);
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

    [Fact]
    public async Task Runner_ControlledTimeout_UsesProcessTreeTerminatorAndSealsExactInvalidEvidence()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var terminator = new RecordingProcessTreeTerminator();
        var request = fixture.CreateRequest("pilot-b.fake.timeout") with
        {
            Timeout = ControlledTimeout
        };

        var result = await new PilotBRunner(terminator).RunAsync(request);

        Assert.True(terminator.WasCalled);
        await AssertSealedTimeoutAgreementAsync(result);
    }

    [Fact]
    public async Task Runner_ControlledTimeout_TerminatesOwnedParentAndChildBeforeSealing()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var request = fixture.CreateRequest("pilot-b.fake.cancel-with-child") with
        {
            Timeout = ChildTreeTimeout
        };
        Process? parentProcess = null;
        Process? childProcess = null;
        var publisher = new SealObservationPublisher(
            () => parentProcess?.HasExited == true && childProcess?.HasExited == true);
        var execution = new PilotBRunner(publisher).RunAsync(request);

        try
        {
            parentProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ParentReadyMarker));
            childProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ChildReadyMarker));

            var result = await execution.WaitAsync(OperationTimeout);

            Assert.True(publisher.TreeExitedBeforePublication);
            Assert.True(parentProcess.HasExited);
            Assert.True(childProcess.HasExited);
            await AssertSealedTimeoutAgreementAsync(result);
        }
        finally
        {
            KillIfRunning(parentProcess);
            KillIfRunning(childProcess);
            await ObserveCompletionAsync(execution);
            parentProcess?.Dispose();
            childProcess?.Dispose();
        }
    }

    [Fact]
    public async Task Runner_ControlledTimeout_WhenTerminationThrows_ReturnsBoundedUnsealedResult()
    {
        var fixture = PilotBRunnerTestFixture.Create();
        using var diagnostics = new PilotBCancellationDiagnostics(output, fixture);
        var failure = new IOException("Injected timeout termination failure.");
        var request = fixture.CreateRequest("pilot-b.fake.cancel-before-output") with
        {
            Timeout = FaultInjectionTimeout
        };
        var execution = new PilotBRunner(new ThrowingProcessTreeTerminator(failure))
            .RunAsync(request);
        diagnostics.Execution = execution;
        Process? ownedProcess = null;

        try
        {
            ownedProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ParentReadyMarker));
            diagnostics.ObserveProcess(ownedProcess);

            var result = await execution.WaitAsync(OperationTimeout);

            AssertUnsealedTimeoutResult(result, "timeout-termination-failed");
            Assert.False(ownedProcess.HasExited);
        }
        catch (Exception exception)
        {
            diagnostics.RecordFailure("primary", exception);
            throw;
        }
        finally
        {
            try
            {
                KillIfRunning(ownedProcess);
                diagnostics.ObserveCleanupReturn();
                await ObserveCompletionAsync(execution);
                ownedProcess?.Dispose();
            }
            catch (Exception exception)
            {
                if (diagnostics.RecordFailure("cleanup", exception))
                {
                    throw;
                }
            }
        }
    }

    [Fact]
    public async Task Runner_ControlledTimeout_WhenProcessDoesNotStop_ReturnsBoundedUnsealedResult()
    {
        var fixture = PilotBRunnerTestFixture.Create();
        using var diagnostics = new PilotBCancellationDiagnostics(output, fixture);
        var request = fixture.CreateRequest("pilot-b.fake.cancel-before-output") with
        {
            Timeout = FaultInjectionTimeout
        };
        var execution = new PilotBRunner(new NonTerminatingProcessTreeTerminator())
            .RunAsync(request);
        diagnostics.Execution = execution;
        Process? ownedProcess = null;

        try
        {
            ownedProcess = await WaitForProcessMarkerAsync(
                MarkerPath(fixture, ParentReadyMarker));
            diagnostics.ObserveProcess(ownedProcess);

            var result = await execution.WaitAsync(OperationTimeout);

            AssertUnsealedTimeoutResult(result, "timeout-termination-incomplete");
            Assert.False(ownedProcess.HasExited);
        }
        catch (Exception exception)
        {
            diagnostics.RecordFailure("primary", exception);
            throw;
        }
        finally
        {
            try
            {
                KillIfRunning(ownedProcess);
                diagnostics.ObserveCleanupReturn();
                await ObserveCompletionAsync(execution);
                ownedProcess?.Dispose();
            }
            catch (Exception exception)
            {
                if (diagnostics.RecordFailure("cleanup", exception))
                {
                    throw;
                }
            }
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
                    var process = Process.GetProcessById(processId);
                    if (!process.HasExited
                        && process.StartTime.ToUniversalTime().Ticks == startedAtTicks)
                    {
                        // Keep the verified process handle until cleanup, rather than reopening by PID.
                        _ = process.SafeHandle;
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
            // Exit racing with Kill still requires the native completion wait below.
        }
        finally
        {
            // HasExited/WaitForExitAsync may observe the exit code before the handle is signaled.
            if (!process.WaitForExit(OperationTimeout))
            {
                throw new TimeoutException("Owned process did not signal termination during cleanup.");
            }
        }
    }

    private static async Task ObserveCompletionAsync(Task execution)
    {
        _ = await Record.ExceptionAsync(async () =>
        {
            await execution;
        });
    }

    private static string MarkerPath(PilotBRunnerTestFixture fixture, string markerName)
        => Path.Combine(fixture.Root, "markers", markerName);

    private static async Task AssertSealedTimeoutAgreementAsync(PilotBRunnerResult result)
    {
        Assert.Equal(PilotBEvidenceState.Sealed, result.EvidenceState);
        Assert.Equal(PilotBRunValidity.Invalid, result.RunValidity);
        Assert.Equal(ControlledTimeoutInvalidReasons, result.InvalidReasons);
        Assert.DoesNotContain("nonzero-exit", result.InvalidReasons);

        using var metadata = JsonDocument.Parse(
            await File.ReadAllBytesAsync(result.Artifacts.MetadataPath));
        Assert.Equal(
            ControlledTimeoutInvalidReasons,
            metadata.RootElement.GetProperty("run_qualification")
                .GetProperty("invalid_reasons")
                .EnumerateArray()
                .Select(reason => reason.GetString()!)
                .ToArray());

        using var seal = JsonDocument.Parse(
            await File.ReadAllBytesAsync(result.Artifacts.SealPath));
        Assert.Equal(
            ControlledTimeoutInvalidReasons,
            seal.RootElement.GetProperty("invalid_reasons")
                .EnumerateArray()
                .Select(reason => reason.GetString()!)
                .ToArray());

        var verification = new PilotBEvidenceBundleVerifier().Verify(result.Artifacts);
        Assert.Equal(PilotBEvidenceState.Sealed, verification.EvidenceState);
        Assert.Equal(ControlledTimeoutInvalidReasons, verification.Qualification!.InvalidReasons);
        Assert.Equal(result.DeterministicFingerprint, verification.SemanticFingerprint);
    }

    private static void AssertUnsealedDiagnostics(string artifactDirectory)
    {
        Assert.True(Directory.Exists(artifactDirectory));
        Assert.True(File.Exists(Path.Combine(artifactDirectory, "prompt.bin")));
        Assert.True(File.Exists(Path.Combine(artifactDirectory, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(artifactDirectory, "pre-manifest.json")));
        AssertNoFinalPublication(artifactDirectory);
    }

    private static void AssertUnsealedTimeoutResult(PilotBRunnerResult result, string terminationReason)
    {
        Assert.Equal(PilotBEvidenceState.Unsealed, result.EvidenceState);
        Assert.Null(result.RunValidity);
        Assert.Null(result.Qualification);
        Assert.Null(result.DeterministicFingerprint);
        Assert.False(result.IntegrityFacts.ArtifactComplete);
        Assert.Contains("timeout", result.InvalidReasons);
        Assert.Contains(terminationReason, result.InvalidReasons);
        Assert.Contains("evidence-unsealed", result.InvalidReasons);
        Assert.True(File.Exists(result.Artifacts.PromptPath));
        Assert.True(File.Exists(result.Artifacts.ManifestPath));
        Assert.True(File.Exists(result.Artifacts.PreManifestPath));
        Assert.True(File.Exists(result.Artifacts.PostManifestPath));
        Assert.True(File.Exists(result.Artifacts.RawOutputPath));
        Assert.True(File.Exists(result.Artifacts.StderrPath));
        AssertNoFinalPublication(result.Artifacts.Root);
    }

    private static void AssertNoFinalPublication(string artifactDirectory)
    {
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

    private sealed class RecordingProcessTreeTerminator : IPilotBProcessTreeTerminator
    {
        private readonly PilotBProcessTreeTerminator inner = new();

        public bool WasCalled { get; private set; }

        public void Terminate(Process process)
        {
            WasCalled = true;
            inner.Terminate(process);
        }
    }

    private sealed class SealObservationPublisher(Func<bool> treeExited) : IEvidenceBundlePublisher
    {
        private readonly PilotBEvidenceBundlePublisher inner = new();

        public bool TreeExitedBeforePublication { get; private set; }

        public Task WriteNewBytesAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken)
            => inner.WriteNewBytesAsync(path, bytes, cancellationToken);

        public Task PublishSealAsync(
            PilotBArtifactPaths paths,
            PilotBEvidenceMetadata metadata,
            string semanticFingerprint,
            CancellationToken cancellationToken)
        {
            TreeExitedBeforePublication = treeExited();
            return inner.PublishSealAsync(paths, metadata, semanticFingerprint, cancellationToken);
        }
    }
}
