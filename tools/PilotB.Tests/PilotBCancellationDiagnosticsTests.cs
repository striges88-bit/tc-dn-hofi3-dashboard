using System.Diagnostics;
using Xunit.Abstractions;

namespace CryptoIndicatorApp.PilotB.Tests;

[Collection(PilotBProcessBackedRunnerCollection.Name)]
public sealed class PilotBCancellationDiagnosticsTests
{
    [Fact]
    public void Failure_PreservesSeparateExceptionsAndPinnedHandleAfterProcessDispose()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var output = new CapturedOutput();
        using var diagnostics = new PilotBCancellationDiagnostics(output, fixture);
        using var process = Process.GetCurrentProcess();
        diagnostics.ObserveProcess(process);
        process.Dispose();
        diagnostics.ObserveCleanupReturn();

        Assert.DoesNotContain("stage=cleanup-kill-return", output.Text);

        diagnostics.Failure("primary", new IOException("primary sentinel"));
        diagnostics.Failure("cleanup", new InvalidOperationException("cleanup sentinel"));

        Assert.Contains("runtime=.NET", output.Text);
        Assert.Contains("process-assembly=", output.Text);
        Assert.Contains("stage=primary", output.Text);
        Assert.Contains("System.IO.IOException: primary sentinel", output.Text);
        Assert.Contains("stage=cleanup", output.Text);
        Assert.Contains("System.InvalidOperationException: cleanup sentinel", output.Text);
        Assert.Contains("native-wait=0x00000102", output.Text);
        Assert.Contains("native-exit-code=259", output.Text);
        Assert.DoesNotContain("handle=unavailable", output.Text);
        diagnostics.Dispose();
        Assert.Contains("stage=cleanup-kill-return", output.Text);
        var afterDispose = output.Text;
        diagnostics.Dispose();
        Assert.Equal(afterDispose, output.Text);
    }

    [Fact]
    public void Dispose_WhenFixtureLocked_PreservesPrimaryAndReportsDisposalFailure()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var output = new CapturedOutput();
        var diagnostics = new PilotBCancellationDiagnostics(output, fixture);
        using var lockedFile = new FileStream(
            Path.Combine(fixture.FixtureRoot, "locked.txt"), FileMode.Create,
            FileAccess.Write, FileShare.None);
        var failure = Record.Exception((Action)(() =>
        {
            try
            {
                throw new InvalidOperationException("primary sentinel");
            }
            catch (Exception primary)
            {
                diagnostics.Failure("primary", primary);
                throw;
            }
            finally
            {
                diagnostics.Dispose();
            }
        }));

        Assert.IsType<IOException>(failure);
        Assert.Contains("stage=primary", output.Text);
        Assert.Contains("primary sentinel", output.Text);
        Assert.Contains("stage=fixture-dispose", output.Text);
        Assert.Contains($"{failure.GetType().FullName}: {failure.Message}", output.Text);
        Assert.Contains("PilotBRunnerTestFixture.Dispose()", output.Text);
        Assert.Contains("handle=unavailable", output.Text);
    }

    [Fact]
    public void OutputFailure_DoesNotInterruptFixtureCleanup()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var diagnostics = new PilotBCancellationDiagnostics(new UnavailableOutput(), fixture);

        diagnostics.Failure("primary", new InvalidOperationException("primary sentinel"));
        diagnostics.Dispose();

        Assert.False(Directory.Exists(fixture.Root));
    }

    private sealed class UnavailableOutput : ITestOutputHelper
    {
        public void WriteLine(string message) => throw new InvalidOperationException("Output unavailable.");
        public void WriteLine(string format, params object[] args) => WriteLine(format);
    }

    private sealed class CapturedOutput : ITestOutputHelper
    {
        private readonly List<string> lines = [];
        public string Text => string.Join(Environment.NewLine, lines);
        public void WriteLine(string message) => lines.Add(message);
        public void WriteLine(string format, params object[] args)
            => WriteLine(string.Format(format, args));
    }
}
