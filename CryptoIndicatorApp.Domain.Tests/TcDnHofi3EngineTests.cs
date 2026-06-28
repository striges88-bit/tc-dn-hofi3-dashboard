using CryptoIndicatorApp.Domain.Indicators;
using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Domain.Tests;

public class TcDnHofi3EngineTests
{
    [Fact]
    public void ProcessesDepthAndTradeEventsIntoIndicatorSample()
    {
        var time = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var engine = new TcDnHofi3Engine("BTCUSDT");

        Assert.Null(engine.Process(CreateSnapshot(time)));
        Assert.Null(engine.Process(CreateTrade(time.AddMilliseconds(50), isBuyerMaker: false, quantity: 100m)));

        var sample = engine.Process(new DepthUpdateEvent(
            Symbol: "BTCUSDT",
            FirstUpdateId: 99,
            FinalUpdateId: 101,
            PreviousFinalUpdateId: 100,
            Bids: new[] { new BookLevel(100m, 8m), new BookLevel(99m, 3m), new BookLevel(98m, 2m) },
            Asks: new[] { new BookLevel(101m, 2m), new BookLevel(102m, 2m), new BookLevel(103m, 1m) },
            ExchangeTime: time.AddMilliseconds(100),
            ReceiveTime: time.AddMilliseconds(130)));

        Assert.NotNull(sample);
        Assert.True(sample.BookHealth.IsSynced);
        Assert.True(sample.Hofi > 0m);
        Assert.True(sample.Nofi > 0m);
        Assert.Equal(1m, Math.Round(sample.Tfi, 4));
        Assert.Equal(TimeSpan.FromMilliseconds(30), sample.ExchangeToReceiveLatency);
    }

    [Fact]
    public void KeepsZOfiZeroUntilMinimumHistoryIsAvailable()
    {
        var parameters = new IndicatorParameters(
            ThetaZ: 0.5m,
            ThetaStable: 0.5m,
            ThetaTfi: 0.1m,
            MinimumZScoreSamples: 3,
            NofiMadFloor: 0.000001m);

        var samples = RunBidQuantitySequence(parameters, 100m, 100m, 150m);

        Assert.Equal(3, samples.Count);
        Assert.All(samples, sample => Assert.Equal(0m, sample.ZOfi));
        Assert.All(samples, sample => Assert.Equal(SignalState.Neutral, sample.Signal));
    }

    [Fact]
    public void RequiresStabilityConfirmationBeforeCandidateSignal()
    {
        var parameters = new IndicatorParameters(
            ThetaZ: 0.5m,
            ThetaStable: 1000000m,
            ThetaTfi: 0.1m,
            MinimumZScoreSamples: 2,
            NofiMadFloor: 0.000001m);

        var samples = RunBidQuantitySequence(parameters, 100m, 100m, 150m);
        var impulseSample = samples.Last();

        Assert.True(impulseSample.ZOfi >= parameters.ThetaZ);
        Assert.True(impulseSample.Tfi >= parameters.ThetaTfi);
        Assert.Equal(SignalState.Neutral, impulseSample.Signal);
    }

    [Fact]
    public void AllowsCandidateWhenTwoOfLastThreeFastZEvaluationsConfirmDirection()
    {
        var parameters = new IndicatorParameters(
            ThetaZ: 0.5m,
            ThetaStable: 1000000m,
            ThetaTfi: 0.1m,
            MinimumZScoreSamples: 2,
            NofiMadFloor: 0.000001m);

        var samples = RunBidQuantitySequence(parameters, 100m, 100m, 150m, 200m);
        var confirmedSample = samples.Last();

        Assert.True(confirmedSample.ZOfi >= parameters.ThetaZ);
        Assert.True(confirmedSample.Tfi >= parameters.ThetaTfi);
        Assert.Equal(SignalState.LongCandidate, confirmedSample.Signal);
    }

    private static DepthSnapshotEvent CreateSnapshot(DateTimeOffset time)
    {
        return new DepthSnapshotEvent(
            Symbol: "BTCUSDT",
            LastUpdateId: 100,
            Bids: new[] { new BookLevel(100m, 5m), new BookLevel(99m, 2m), new BookLevel(98m, 1m) },
            Asks: new[] { new BookLevel(101m, 4m), new BookLevel(102m, 2m), new BookLevel(103m, 1m) },
            ExchangeTime: time,
            ReceiveTime: time);
    }

    private static IReadOnlyList<IndicatorSample> RunBidQuantitySequence(IndicatorParameters parameters, params decimal[] bidQuantities)
    {
        var time = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var engine = new TcDnHofi3Engine("BTCUSDT", parameters);
        var samples = new List<IndicatorSample>();

        engine.Process(CreateSnapshot(time));

        long finalUpdateId = 101;
        for (var index = 0; index < bidQuantities.Length; index++)
        {
            var updateTime = time.AddMilliseconds(100 * (index + 1));
            engine.Process(CreateTrade(updateTime.AddMilliseconds(-50), isBuyerMaker: false, quantity: 10m));

            var sample = engine.Process(CreateDepthUpdate(updateTime, finalUpdateId, bidQuantities[index]));
            if (sample is not null)
            {
                samples.Add(sample);
            }

            finalUpdateId++;
        }

        return samples;
    }

    private static DepthUpdateEvent CreateDepthUpdate(DateTimeOffset time, long finalUpdateId, decimal topBidQuantity)
    {
        return new DepthUpdateEvent(
            Symbol: "BTCUSDT",
            FirstUpdateId: finalUpdateId,
            FinalUpdateId: finalUpdateId,
            PreviousFinalUpdateId: finalUpdateId - 1,
            Bids: new[] { new BookLevel(100m, topBidQuantity), new BookLevel(99m, 100m), new BookLevel(98m, 100m) },
            Asks: new[] { new BookLevel(101m, 100m), new BookLevel(102m, 100m), new BookLevel(103m, 100m) },
            ExchangeTime: time,
            ReceiveTime: time.AddMilliseconds(10));
    }

    private static AggTradeEvent CreateTrade(DateTimeOffset time, bool isBuyerMaker, decimal quantity, decimal price = 100m)
    {
        return new AggTradeEvent(
            Symbol: "BTCUSDT",
            AggregateTradeId: 1,
            Price: price,
            Quantity: quantity,
            FirstTradeId: 10,
            LastTradeId: 11,
            TradeTime: time,
            IsBuyerMaker: isBuyerMaker,
            ExchangeTime: time,
            ReceiveTime: time.AddMilliseconds(5));
    }
}
