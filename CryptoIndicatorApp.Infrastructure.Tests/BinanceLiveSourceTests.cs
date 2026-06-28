using CryptoIndicatorApp.Domain.Context;
using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class BinanceLiveSourceTests
{
    [Fact]
    public async Task Emits_snapshot_before_buffered_depth_updates()
    {
        var client = new FakeBinanceUsdFuturesMarketDataClient
        {
            SnapshotFactory = () => Snapshot(lastUpdateId: 100)
        };
        client.OnDepthSubscribed = handler =>
        {
            handler(Update(firstUpdateId: 100, finalUpdateId: 105, previousFinalUpdateId: 99));
        };

        var source = new BinanceUsdFuturesLiveMarketEventSource("BTCUSDT", client);

        var events = await source.ReadAllAsync()
            .TakeAsync(2)
            .ToListAsync();

        Assert.IsType<DepthSnapshotEvent>(events[0]);
        Assert.IsType<DepthUpdateEvent>(events[1]);
    }

    [Fact]
    public async Task Drops_stale_buffered_depth_updates_before_initial_sync()
    {
        var client = new FakeBinanceUsdFuturesMarketDataClient
        {
            SnapshotFactory = () => Snapshot(lastUpdateId: 100)
        };
        client.OnDepthSubscribed = handler =>
        {
            handler(Update(firstUpdateId: 90, finalUpdateId: 99, previousFinalUpdateId: 89));
            handler(Update(firstUpdateId: 100, finalUpdateId: 105, previousFinalUpdateId: 99));
        };

        var source = new BinanceUsdFuturesLiveMarketEventSource("BTCUSDT", client);

        var events = await source.ReadAllAsync()
            .TakeAsync(2)
            .ToListAsync();

        var depthUpdate = Assert.IsType<DepthUpdateEvent>(events[1]);
        Assert.Equal(100, depthUpdate.FirstUpdateId);
        Assert.Equal(105, depthUpdate.FinalUpdateId);
    }

    [Fact]
    public async Task Emits_gap_update_then_resync_snapshot_when_previous_update_id_breaks()
    {
        var snapshots = new Queue<DepthSnapshotEvent>(new[]
        {
            Snapshot(lastUpdateId: 100),
            Snapshot(lastUpdateId: 200)
        });
        var client = new FakeBinanceUsdFuturesMarketDataClient
        {
            SnapshotFactory = () => snapshots.Dequeue()
        };
        client.OnDepthSubscribed = handler =>
        {
            handler(Update(firstUpdateId: 100, finalUpdateId: 105, previousFinalUpdateId: 99));
            handler(Update(firstUpdateId: 106, finalUpdateId: 110, previousFinalUpdateId: 999));
        };

        var source = new BinanceUsdFuturesLiveMarketEventSource("BTCUSDT", client);

        var events = await source.ReadAllAsync()
            .TakeAsync(4)
            .ToListAsync();

        Assert.IsType<DepthSnapshotEvent>(events[0]);
        Assert.IsType<DepthUpdateEvent>(events[1]);
        var gapUpdate = Assert.IsType<DepthUpdateEvent>(events[2]);
        var resyncSnapshot = Assert.IsType<DepthSnapshotEvent>(events[3]);

        Assert.Equal(999, gapUpdate.PreviousFinalUpdateId);
        Assert.Equal(200, resyncSnapshot.LastUpdateId);
        Assert.Equal(2, client.SnapshotCalls);
    }

    private static DepthSnapshotEvent Snapshot(long lastUpdateId)
    {
        var timestamp = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        return new DepthSnapshotEvent(
            "BTCUSDT",
            lastUpdateId,
            new[] { new BookLevel(100m, 2m) },
            new[] { new BookLevel(101m, 3m) },
            timestamp,
            timestamp);
    }

    private static DepthUpdateEvent Update(long firstUpdateId, long finalUpdateId, long previousFinalUpdateId)
    {
        var timestamp = DateTimeOffset.Parse("2026-05-25T08:00:00.010Z");
        return new DepthUpdateEvent(
            "BTCUSDT",
            firstUpdateId,
            finalUpdateId,
            previousFinalUpdateId,
            new[] { new BookLevel(100m, 2.5m) },
            new[] { new BookLevel(101m, 3.5m) },
            timestamp,
            timestamp);
    }

    private sealed class FakeBinanceUsdFuturesMarketDataClient : IBinanceUsdFuturesMarketDataClient
    {
        public Func<DepthSnapshotEvent>? SnapshotFactory { get; init; }

        public Action<Action<DepthUpdateEvent>>? OnDepthSubscribed { get; set; }

        public int SnapshotCalls { get; private set; }

        public Task<DepthSnapshotEvent> GetDepthSnapshotAsync(
            string symbol,
            int limit,
            CancellationToken cancellationToken = default)
        {
            SnapshotCalls++;
            return Task.FromResult(SnapshotFactory?.Invoke() ?? Snapshot(lastUpdateId: 100));
        }

        public Task<IAsyncDisposable> SubscribeDepthUpdatesAsync(
            string symbol,
            Action<DepthUpdateEvent> onDepthUpdate,
            CancellationToken cancellationToken = default)
        {
            OnDepthSubscribed?.Invoke(onDepthUpdate);
            return Task.FromResult<IAsyncDisposable>(AsyncDisposable.Empty);
        }

        public Task<IAsyncDisposable> SubscribeAggTradesAsync(
            string symbol,
            Action<AggTradeEvent> onAggTrade,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable>(AsyncDisposable.Empty);
        }

        public Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
            string symbol,
            ContextFrame frame,
            int limit,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OpenInterestPoint>>(Array.Empty<OpenInterestPoint>());
        }

        public Task<IAsyncDisposable> SubscribeLiquidationsAsync(
            string symbol,
            Action<LiquidationEvent> onLiquidation,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable>(AsyncDisposable.Empty);
        }
    }

    private sealed class AsyncDisposable : IAsyncDisposable
    {
        public static AsyncDisposable Empty { get; } = new();

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
