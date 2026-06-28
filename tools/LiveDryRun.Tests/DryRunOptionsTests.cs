using CryptoIndicatorApp.LiveDryRun;
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.LiveDryRun.Tests;

public class DryRunOptionsTests
{
    [Fact]
    public void ParseSupportsReplayOnlyInputPath()
    {
        var options = DryRunOptions.Parse(new[]
        {
            "--symbol", "xanusdt",
            "--input", "recordings/xanusdt.jsonl",
            "--replay-only"
        });

        Assert.Equal("XANUSDT", options.Symbol);
        Assert.True(options.ReplayOnly);
        Assert.EndsWith("recordings\\xanusdt.jsonl", options.InputPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseSupportsContextOnlySmokeOptions()
    {
        var options = DryRunOptions.Parse(new[]
        {
            "--symbol", "playusdt",
            "--context-only",
            "--frame", "5m",
            "--oi-limit", "42",
            "--seconds", "3"
        });

        Assert.Equal("PLAYUSDT", options.Symbol);
        Assert.True(options.ContextOnly);
        Assert.Equal(ContextFrame.FiveMinutes, options.ContextFrame);
        Assert.Equal(42, options.OpenInterestHistoryLimit);
        Assert.Equal(TimeSpan.FromSeconds(3), options.Duration);
    }
}
