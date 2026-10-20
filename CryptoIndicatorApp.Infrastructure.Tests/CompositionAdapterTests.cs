using CryptoIndicatorApp.Application.Context;
using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Domain.Context;
using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Infrastructure.Binance;
using CryptoIndicatorApp.Infrastructure.Composition;
using CryptoIndicatorApp.Infrastructure.Jsonl;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class CompositionAdapterTests
{
    [Fact]
    public async Task Jsonl_recorder_writes_the_same_bytes_as_the_store_and_reads_them_back()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var directPath = Path.Combine(directory, "direct.jsonl");
        var adapterPath = Path.Combine(directory, "adapter.jsonl");
        var timestamp = DateTimeOffset.Parse("2026-05-25T08:00:00.100Z");
        var marketEvent = new AggTradeEvent(
            "BTCUSDT",
            AggregateTradeId: 10,
            Price: 100m,
            Quantity: 2m,
            FirstTradeId: 20,
            LastTradeId: 21,
            TradeTime: timestamp,
            IsBuyerMaker: false,
            ExchangeTime: timestamp,
            ReceiveTime: timestamp.AddMilliseconds(3));

        await new JsonlMarketEventStore().AppendAsync(directPath, marketEvent, CancellationToken.None);
        await JsonlMarketEventRecorder.Create(adapterPath).AppendAsync(marketEvent);

        Assert.Equal(await File.ReadAllTextAsync(directPath), await File.ReadAllTextAsync(adapterPath));

        IMarketEventSource source = JsonlMarketEventSource.Create(adapterPath);
        var events = await source.ReadAllAsync().ToListAsync();
        var trade = Assert.IsType<AggTradeEvent>(Assert.Single(events));
        Assert.Equal("BTCUSDT", trade.Symbol);
        Assert.Equal(10, trade.AggregateTradeId);
        Assert.False(trade.IsBuyerMaker);
        Assert.Equal(timestamp.AddMilliseconds(3), trade.ReceiveTime);
    }

    [Fact]
    public void Jsonl_adapters_reject_a_blank_path()
    {
        Assert.Throws<ArgumentException>(() => JsonlMarketEventRecorder.Create(" "));
        Assert.Throws<ArgumentException>(() => JsonlMarketEventSource.Create(" "));
    }

    [Fact]
    public async Task Live_source_adapter_forwards_snapshot_then_depth_update()
    {
        var client = new FakeMarketDataClient();
        client.OnDepthSubscribed = handler =>
        {
            handler(new DepthUpdateEvent(
                "BTCUSDT",
                FirstUpdateId: 100,
                FinalUpdateId: 105,
                PreviousFinalUpdateId: 99,
                Bids: new[] { new BookLevel(100m, 2m) },
                Asks: new[] { new BookLevel(101m, 3m) },
                ExchangeTime: Timestamp,
                ReceiveTime: Timestamp));
        };
        IMarketEventSource source = new BinanceLiveMarketEventSource(
            new BinanceUsdFuturesLiveMarketEventSource("BTCUSDT", client));

        var events = await source.ReadAllAsync().TakeAsync(2).ToListAsync();

        Assert.IsType<DepthSnapshotEvent>(events[0]);
        Assert.IsType<DepthUpdateEvent>(events[1]);
    }

    [Fact]
    public void Binance_context_source_implements_application_boundary()
    {
        Assert.True(typeof(IContextDataSource).IsAssignableFrom(typeof(BinanceContextDataSource)));
    }

    [Fact]
    public async Task Context_source_marks_the_liquidation_subscription_after_it_connects()
    {
        var client = new FakeMarketDataClient();
        client.OnLiquidationSubscribed = handler =>
        {
            handler(new LiquidationEvent(
                "BTCUSDT",
                "SELL",
                AveragePrice: 100m,
                QuantityFilled: 1m,
                TradeTime: Timestamp,
                ExchangeTime: Timestamp,
                ReceiveTime: Timestamp));
        };
        var source = new BinanceContextDataSource(client);

        var events = await source.ReadLiquidationsAsync("BTCUSDT").TakeAsync(1).ToListAsync();

        Assert.True(source.LiquidationSubscriptionConnected);
        Assert.Equal("SELL", Assert.Single(events).Side);
    }

    private static DateTimeOffset Timestamp { get; } =
        DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");

    private sealed class FakeMarketDataClient : IBinanceUsdFuturesMarketDataClient
    {
        public Action<Action<DepthUpdateEvent>>? OnDepthSubscribed { get; set; }

        public Action<Action<LiquidationEvent>>? OnLiquidationSubscribed { get; set; }

        public Task<DepthSnapshotEvent> GetDepthSnapshotAsync(
            string symbol,
            int limit,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DepthSnapshotEvent(
                symbol,
                LastUpdateId: 100,
                Bids: new[] { new BookLevel(100m, 1m) },
                Asks: new[] { new BookLevel(101m, 1m) },
                ExchangeTime: Timestamp,
                ReceiveTime: Timestamp));
        }

        public Task<IAsyncDisposable> SubscribeDepthUpdatesAsync(
            string symbol,
            Action<DepthUpdateEvent> onDepthUpdate,
            CancellationToken cancellationToken = default)
        {
            OnDepthSubscribed?.Invoke(onDepthUpdate);
            return Task.FromResult<IAsyncDisposable>(EmptyDisposable.Instance);
        }

        public Task<IAsyncDisposable> SubscribeAggTradesAsync(
            string symbol,
            Action<AggTradeEvent> onAggTrade,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable>(EmptyDisposable.Instance);
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
            OnLiquidationSubscribed?.Invoke(onLiquidation);
            return Task.FromResult<IAsyncDisposable>(EmptyDisposable.Instance);
        }
    }

    private sealed class EmptyDisposable : IAsyncDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
