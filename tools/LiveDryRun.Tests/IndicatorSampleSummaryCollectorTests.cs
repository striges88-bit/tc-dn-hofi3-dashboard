using CryptoIndicatorApp.Domain.Indicators;
using CryptoIndicatorApp.Domain.OrderBooks;
using CryptoIndicatorApp.LiveDryRun;

namespace CryptoIndicatorApp.LiveDryRun.Tests;

public class IndicatorSampleSummaryCollectorTests
{
    [Fact]
    public void BuildsSummaryAcrossSamples()
    {
        var clock = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var collector = new IndicatorSampleSummaryCollector();

        collector.Add(CreateSample(clock, zOfi: -2.5m, tfi: -0.2m, SignalState.ShortCandidate, latencyMs: 30, resyncCount: 1));
        collector.Add(CreateSample(clock.AddMilliseconds(100), zOfi: 0.5m, tfi: 0.1m, SignalState.Neutral, latencyMs: 10, resyncCount: 0));
        collector.Add(CreateSample(clock.AddMilliseconds(200), zOfi: 3.0m, tfi: 0.8m, SignalState.LongCandidate, latencyMs: 20, resyncCount: 2));

        var summary = collector.Build();

        Assert.Equal(3, summary.Count);
        Assert.Equal(clock, summary.FirstTimestamp);
        Assert.Equal(clock.AddMilliseconds(200), summary.LastTimestamp);
        Assert.Equal(-2.5m, summary.MinZOfi);
        Assert.Equal(3.0m, summary.MaxZOfi);
        Assert.Equal(3.0m, summary.MaxAbsZOfi);
        Assert.Equal(-0.2m, summary.MinTfi);
        Assert.Equal(0.8m, summary.MaxTfi);
        Assert.Equal(0.366667m, Math.Round(summary.AverageAbsTfi, 6));
        Assert.Equal(1, summary.LongCandidateCount);
        Assert.Equal(1, summary.ShortCandidateCount);
        Assert.Equal(1, summary.NeutralCount);
        Assert.Equal(0, summary.UnsyncedSampleCount);
        Assert.Equal(2, summary.MaxResyncCount);
        Assert.Equal(20, summary.LatencyP50Ms);
        Assert.Equal(30, summary.LatencyP95Ms);
        Assert.Equal(30, summary.LatencyP99Ms);
    }

    private static IndicatorSample CreateSample(
        DateTimeOffset timestamp,
        decimal zOfi,
        decimal tfi,
        SignalState signal,
        int latencyMs,
        int resyncCount)
    {
        return new IndicatorSample(
            Timestamp: timestamp,
            Hofi: 0m,
            Nofi: 0m,
            ZOfi: zOfi,
            Tfi: tfi,
            Signal: signal,
            BookHealth: new BookHealth(
                IsSynced: true,
                IsStale: false,
                IsCrossed: false,
                LastUpdateId: 10,
                ResyncCount: resyncCount,
                Reason: null),
            ExchangeToReceiveLatency: TimeSpan.FromMilliseconds(latencyMs));
    }
}
