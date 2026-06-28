using CryptoIndicatorApp.Application.Charts;
using CryptoIndicatorApp.Domain.Indicators;
using CryptoIndicatorApp.Domain.OrderBooks;

namespace CryptoIndicatorApp.Application.Tests;

public class ChartSampleBufferTests
{
    [Fact]
    public void RetainsOnlySamplesInsideTrailingWindow()
    {
        var start = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var buffer = new ChartSampleBuffer(TimeSpan.FromSeconds(60));

        buffer.Add(CreateSample(start, zOfi: 1m, tfi: 0.1m));
        buffer.Add(CreateSample(start.AddSeconds(30), zOfi: 2m, tfi: 0.2m));
        buffer.Add(CreateSample(start.AddSeconds(61), zOfi: 3m, tfi: 0.3m));

        var snapshot = buffer.Snapshot();

        Assert.Collection(
            snapshot,
            point => Assert.Equal(start.AddSeconds(30), point.Timestamp),
            point => Assert.Equal(start.AddSeconds(61), point.Timestamp));
        Assert.Equal(new[] { 2m, 3m }, snapshot.Select(point => point.ZOfi));
        Assert.Equal(new[] { 0.2m, 0.3m }, snapshot.Select(point => point.Tfi));
    }

    [Fact]
    public void Clear_removes_all_buffered_samples()
    {
        var buffer = new ChartSampleBuffer(TimeSpan.FromSeconds(60));

        buffer.Add(CreateSample(DateTimeOffset.Parse("2026-05-25T08:00:00.000Z"), zOfi: 1m, tfi: 0.1m));

        buffer.Clear();

        Assert.Empty(buffer.Snapshot());
    }

    private static IndicatorSample CreateSample(DateTimeOffset timestamp, decimal zOfi, decimal tfi)
    {
        return new IndicatorSample(
            Timestamp: timestamp,
            Hofi: 0m,
            Nofi: 0m,
            ZOfi: zOfi,
            Tfi: tfi,
            Signal: SignalState.Neutral,
            BookHealth: BookHealth.Empty,
            ExchangeToReceiveLatency: TimeSpan.Zero);
    }
}
