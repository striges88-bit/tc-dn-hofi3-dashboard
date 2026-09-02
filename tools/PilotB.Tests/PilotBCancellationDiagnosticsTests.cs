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

        Assert.True(diagnostics.RecordFailure("primary", new IOException("primary sentinel")));
        Assert.False(diagnostics.RecordFailure("cleanup", new InvalidOperationException("cleanup sentinel")));

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
        var primaryFailure = new InvalidOperationException("primary sentinel");
        var failure = Record.Exception((Action)(() =>
        {
            try
            {
                ThrowPrimary();
            }
            catch (Exception primary)
            {
                diagnostics.RecordFailure("primary", primary);
                throw;
            }
            finally
            {
                diagnostics.Dispose();
            }
        }));

        Assert.Same(primaryFailure, failure);
        Assert.Contains(nameof(ThrowPrimary), failure!.StackTrace);
        Assert.Contains("stage=primary", output.Text);
        Assert.Contains("primary sentinel", output.Text);
        Assert.Contains("stage=fixture-dispose", output.Text);
        Assert.Contains("System.IO.IOException:", output.Text);
        Assert.Contains("PilotBRunnerTestFixture.Dispose()", output.Text);
        Assert.Contains("handle=unavailable", output.Text);

        void ThrowPrimary() => throw primaryFailure;
    }

    [Fact]
    public void Dispose_WhenFixtureLockedWithoutPrimary_PropagatesDisposalFailure()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var output = new CapturedOutput();
        var diagnostics = new PilotBCancellationDiagnostics(output, fixture);
        using var lockedFile = new FileStream(
            Path.Combine(fixture.FixtureRoot, "locked.txt"), FileMode.Create,
            FileAccess.Write, FileShare.None);

        var failure = Assert.Throws<IOException>(diagnostics.Dispose);

        Assert.Contains("PilotBRunnerTestFixture.Dispose()", failure.StackTrace);
        Assert.Contains("stage=fixture-dispose", output.Text);
        Assert.Contains($"{failure.GetType().FullName}: {failure.Message}", output.Text);
        Assert.DoesNotContain("stage=primary", output.Text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RecordFailure_CleanupPreservesFirstExceptionAndStack(
        bool primaryFails, bool fixtureLocked)
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var output = new CapturedOutput();
        using var lockedFile = fixtureLocked
            ? new FileStream(Path.Combine(fixture.FixtureRoot, "locked.txt"),
                FileMode.Create, FileAccess.Write, FileShare.None)
            : null;
        var primaryFailure = new InvalidOperationException("primary sentinel");
        var cleanupFailure = new IOException("cleanup sentinel");

        var failure = await Record.ExceptionAsync(async () =>
        {
            using var diagnostics = new PilotBCancellationDiagnostics(output, fixture);
            try
            {
                await Task.Yield();
                if (primaryFails)
                {
                    ThrowPrimary();
                }
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
                    ThrowCleanup();
                }
                catch (Exception exception)
                {
                    if (diagnostics.RecordFailure("cleanup", exception))
                    {
                        throw;
                    }
                }
            }
        });

        Assert.Same(primaryFails ? (Exception)primaryFailure : cleanupFailure, failure);
        Assert.Contains(primaryFails ? nameof(ThrowPrimary) : nameof(ThrowCleanup), failure!.StackTrace);
        Assert.Equal(primaryFails, output.Text.Contains("stage=primary", StringComparison.Ordinal));
        Assert.Contains("stage=cleanup", output.Text);
        Assert.Contains("System.IO.IOException: cleanup sentinel", output.Text);
        Assert.Equal(fixtureLocked, output.Text.Contains("stage=fixture-dispose", StringComparison.Ordinal));
        Assert.Equal(fixtureLocked, Directory.Exists(fixture.Root));

        void ThrowPrimary() => throw primaryFailure;
        void ThrowCleanup() => throw cleanupFailure;
    }

    [Fact]
    public void OutputFailure_DoesNotInterruptFixtureCleanup()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var diagnostics = new PilotBCancellationDiagnostics(new UnavailableOutput(), fixture);

        Assert.True(diagnostics.RecordFailure("primary", new InvalidOperationException("primary sentinel")));
        Assert.False(diagnostics.RecordFailure("cleanup", new IOException("cleanup sentinel")));
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
