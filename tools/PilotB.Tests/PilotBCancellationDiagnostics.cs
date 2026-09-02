using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Xunit.Abstractions;

namespace CryptoIndicatorApp.PilotB.Tests;

// Temporary PR #57 diagnostics; remove once failure-time CI evidence identifies the cause.
internal sealed class PilotBCancellationDiagnostics : IDisposable
{
    private readonly ITestOutputHelper output;
    private readonly PilotBRunnerTestFixture fixture;
    private SafeProcessHandle? processHandle;
    private bool handleRetained;
    private int? processId;
    private string? cleanupReturnSnapshot;
    private bool failureRecorded;

    public PilotBCancellationDiagnostics(ITestOutputHelper output, PilotBRunnerTestFixture fixture)
    {
        this.output = output;
        this.fixture = fixture;
        var version = typeof(Process).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Write($"runtime={RuntimeInformation.FrameworkDescription}; process-assembly={version}; "
            + $"os={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}");
    }

    public Task? Execution { get; set; }

    public void ObserveProcess(Process process)
    {
        try
        {
            processId = process.Id;
            processHandle = process.SafeHandle;
            // Retain only the kernel handle, without changing Process's cached exit state.
            // The normal cleanup still disposes Process before fixture deletion.
            processHandle.DangerousAddRef(ref handleRetained);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            Write($"handle-capture-failed: {exception}");
        }
    }

    // Only the first recorded failure should propagate; later teardown failures stay diagnostic.
    public bool RecordFailure(string stage, Exception exception)
    {
        var isFirstFailure = !failureRecorded;
        failureRecorded = true;
        Write(CaptureSnapshot(stage));
        Write($"stage={stage}; exception={exception}");
        if (Execution?.Exception is { } runnerException)
        {
            Write($"runner-exception={runnerException}");
        }

        return isFirstFailure;
    }

    public void ObserveCleanupReturn()
        => cleanupReturnSnapshot = CaptureSnapshot("cleanup-kill-return");

    private string CaptureSnapshot(string stage)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var nativeState = "handle=unavailable";
        if (handleRetained)
        {
            var handle = processHandle!.DangerousGetHandle();
            var wait = WaitForSingleObject(handle, 0);
            var waitError = wait == uint.MaxValue ? Marshal.GetLastWin32Error() : 0;
            var hasExitCode = GetExitCodeProcess(handle, out var exitCode);
            var exitError = hasExitCode ? 0 : Marshal.GetLastWin32Error();
            nativeState = $"native-wait=0x{wait:X8}; wait-error={waitError}; "
                + $"native-exit-code={(hasExitCode ? exitCode.ToString() : "unavailable")}; exit-error={exitError}";
        }

        return $"utc={timestamp:O}; stage={stage}; pid={processId}; "
            + $"runner={Execution?.Status}; {nativeState}";
    }

    public void Dispose()
    {
        try
        {
            fixture.Dispose();
        }
        catch (Exception exception)
        {
            if (RecordFailure("fixture-dispose", exception))
            {
                throw;
            }
        }
        finally
        {
            if (handleRetained)
            {
                processHandle!.DangerousRelease();
                handleRetained = false;
            }

            // Do not add output latency between the cleanup wait and fixture deletion.
            if (cleanupReturnSnapshot is not null)
            {
                Write(cleanupReturnSnapshot);
                cleanupReturnSnapshot = null;
            }
        }
    }

    private void Write(string message)
    {
        try
        {
            output.WriteLine(message);
        }
        catch (Exception)
        {
            // A diagnostic sink must not replace the test failure or interrupt teardown.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
}
