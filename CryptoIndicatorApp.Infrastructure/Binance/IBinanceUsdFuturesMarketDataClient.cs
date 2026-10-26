using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Infrastructure.Binance;

public interface IBinanceUsdFuturesMarketDataClient
{
    public Task<DepthSnapshotEvent> GetDepthSnapshotAsync(
        string symbol,
        int limit,
        CancellationToken cancellationToken = default);

    public Task<IAsyncDisposable> SubscribeDepthUpdatesAsync(
        string symbol,
        Action<DepthUpdateEvent> onDepthUpdate,
        CancellationToken cancellationToken = default);

    public Task<IAsyncDisposable> SubscribeAggTradesAsync(
        string symbol,
        Action<AggTradeEvent> onAggTrade,
        CancellationToken cancellationToken = default);

    public Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
        string symbol,
        ContextFrame frame,
        int limit,
        CancellationToken cancellationToken = default);

    public Task<IAsyncDisposable> SubscribeLiquidationsAsync(
        string symbol,
        Action<LiquidationEvent> onLiquidation,
        CancellationToken cancellationToken = default);
}
