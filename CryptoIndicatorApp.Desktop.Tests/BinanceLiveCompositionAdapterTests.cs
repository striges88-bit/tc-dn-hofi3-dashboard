using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Desktop.Composition;
using CryptoIndicatorApp.Domain.Context;
using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Desktop.Tests;

public sealed class BinanceLiveCompositionAdapterTests
{
    [Fact]
    public async Task Binance_live_source_adapter_exposes_infrastructure_source_as_application_source()
    {
        var client = new FakeBinanceUsdFuturesMarketDataClient();
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

        var events = await source.ReadAllAsync()
            .TakeAsync(2)
            .ToListAsync();

        Assert.IsType<DepthSnapshotEvent>(events[0]);
        Assert.IsType<DepthUpdateEvent>(events[1]);
    }

    private static DateTimeOffset Timestamp { get; } =
        DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");

    private sealed class FakeBinanceUsdFuturesMarketDataClient : IBinanceUsdFuturesMarketDataClient
    {
        public Action<Action<DepthUpdateEvent>>? OnDepthSubscribed { get; set; }

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
