using System.Runtime.CompilerServices;
using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Application.Pipeline;
using CryptoIndicatorApp.Application.Sessions;
using CryptoIndicatorApp.Domain.Indicators;
using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Domain.OrderBooks;

namespace CryptoIndicatorApp.Application.Tests;

public class IndicatorSessionTests
{
    [Fact]
    public async Task ReplayAndLiveSessionsProduceSameSamplesFromSameMarketEvents()
    {
        var events = CreateDeterministicEvents();

        var replay = new ReplayIndicatorSession("BTCUSDT", new SequenceMarketEventSource(events));
        var live = new LiveIndicatorSession("BTCUSDT", new SequenceMarketEventSource(events), recorder: null);

        var replaySamples = await replay.RunAsync(CancellationToken.None).ToListAsync();
        var liveSamples = await live.RunAsync(CancellationToken.None).ToListAsync();

        Assert.Equal(
            replaySamples.Select(ToComparableSample),
            liveSamples.Select(ToComparableSample));
    }

    [Fact]
    public async Task ReplaySessionProducesDeterministicSampleSequence()
    {
        var first = new ReplayIndicatorSession("BTCUSDT", new SequenceMarketEventSource(CreateDeterministicEvents()));
        var second = new ReplayIndicatorSession("BTCUSDT", new SequenceMarketEventSource(CreateDeterministicEvents()));

        var firstSamples = await first.RunAsync(CancellationToken.None).ToListAsync();
        var secondSamples = await second.RunAsync(CancellationToken.None).ToListAsync();

        Assert.Equal(2, firstSamples.Count);
        Assert.Equal(
            firstSamples.Select(ToComparableSample),
            secondSamples.Select(ToComparableSample));
        Assert.Equal(
            new[]
            {
                DateTimeOffset.Parse("2026-05-25T08:00:00.100Z"),
                DateTimeOffset.Parse("2026-05-25T08:00:00.200Z")
            },
            firstSamples.Select(sample => sample.Timestamp));
    }

    [Fact]
    public async Task LiveSessionRecordsRawEventsItProcesses()
    {
        var events = CreateDeterministicEvents();
        var recorder = new RecordingMarketEventRecorder();
        var live = new LiveIndicatorSession("BTCUSDT", new SequenceMarketEventSource(events), recorder);

        _ = await live.RunAsync(CancellationToken.None).ToListAsync();

        Assert.Equal(events, recorder.Recorded);
    }

    [Fact]
    public async Task IndicatorPipelineRecordsEventBeforeProcessingIt()
    {
        var order = new List<string>();
        var source = new SequenceMarketEventSource(CreateDeterministicEvents().Take(2).ToArray());
        var recorder = new OrderedMarketEventRecorder(order);
        var pipeline = new IndicatorPipeline(marketEvent =>
        {
            order.Add($"process:{marketEvent.GetType().Name}");
            return null;
        });

        _ = await pipeline.RunAsync(source.ReadAllAsync(CancellationToken.None), recorder, CancellationToken.None).ToListAsync();

        Assert.Equal(
            new[]
            {
                "record:DepthSnapshotEvent",
                "process:DepthSnapshotEvent",
                "record:AggTradeEvent",
                "process:AggTradeEvent"
            },
            order);
    }

    private static (DateTimeOffset Timestamp, decimal ZOfi, decimal Tfi, SignalState Signal, bool IsSynced, int ResyncCount) ToComparableSample(
        IndicatorSample sample)
    {
        return (
            sample.Timestamp,
            Math.Round(sample.ZOfi, 8),
            Math.Round(sample.Tfi, 8),
            sample.Signal,
            sample.BookHealth.IsSynced,
            sample.BookHealth.ResyncCount);
    }

    private static IMarketEvent[] CreateDeterministicEvents()
    {
        var time = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        return new IMarketEvent[]
        {
            new DepthSnapshotEvent(
                Symbol: "BTCUSDT",
                LastUpdateId: 100,
                Bids: new[] { new BookLevel(100m, 5m), new BookLevel(99m, 2m), new BookLevel(98m, 1m) },
                Asks: new[] { new BookLevel(101m, 4m), new BookLevel(102m, 2m), new BookLevel(103m, 1m) },
                ExchangeTime: time,
                ReceiveTime: time),
            new AggTradeEvent(
                Symbol: "BTCUSDT",
                AggregateTradeId: 1,
                Price: 100m,
                Quantity: 100m,
                FirstTradeId: 10,
                LastTradeId: 11,
                TradeTime: time.AddMilliseconds(50),
                IsBuyerMaker: false,
                ExchangeTime: time.AddMilliseconds(50),
                ReceiveTime: time.AddMilliseconds(55)),
            new DepthUpdateEvent(
                Symbol: "BTCUSDT",
                FirstUpdateId: 99,
                FinalUpdateId: 101,
                PreviousFinalUpdateId: 100,
                Bids: new[] { new BookLevel(100m, 8m), new BookLevel(99m, 3m), new BookLevel(98m, 2m) },
                Asks: new[] { new BookLevel(101m, 2m), new BookLevel(102m, 2m), new BookLevel(103m, 1m) },
                ExchangeTime: time.AddMilliseconds(100),
                ReceiveTime: time.AddMilliseconds(130)),
            new AggTradeEvent(
                Symbol: "BTCUSDT",
                AggregateTradeId: 2,
                Price: 101m,
                Quantity: 50m,
                FirstTradeId: 12,
                LastTradeId: 13,
                TradeTime: time.AddMilliseconds(150),
                IsBuyerMaker: true,
                ExchangeTime: time.AddMilliseconds(150),
                ReceiveTime: time.AddMilliseconds(158)),
            new DepthUpdateEvent(
                Symbol: "BTCUSDT",
                FirstUpdateId: 102,
                FinalUpdateId: 102,
                PreviousFinalUpdateId: 101,
                Bids: new[] { new BookLevel(100m, 7m), new BookLevel(99m, 3m), new BookLevel(98m, 2m) },
                Asks: new[] { new BookLevel(101m, 3m), new BookLevel(102m, 2m), new BookLevel(103m, 1m) },
                ExchangeTime: time.AddMilliseconds(200),
                ReceiveTime: time.AddMilliseconds(222))
        };
    }

    private sealed class SequenceMarketEventSource(IReadOnlyList<IMarketEvent> marketEvents) : IMarketEventSource
    {
        public async IAsyncEnumerable<IMarketEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var marketEvent in marketEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return marketEvent;
            }
        }
    }

    private sealed class RecordingMarketEventRecorder : IMarketEventRecorder
    {
        public List<IMarketEvent> Recorded { get; } = [];

        public ValueTask AppendAsync(IMarketEvent marketEvent, CancellationToken cancellationToken = default)
        {
            Recorded.Add(marketEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderedMarketEventRecorder(List<string> order) : IMarketEventRecorder
    {
        public ValueTask AppendAsync(IMarketEvent marketEvent, CancellationToken cancellationToken = default)
        {
            order.Add($"record:{marketEvent.GetType().Name}");
            return ValueTask.CompletedTask;
        }
    }
}
