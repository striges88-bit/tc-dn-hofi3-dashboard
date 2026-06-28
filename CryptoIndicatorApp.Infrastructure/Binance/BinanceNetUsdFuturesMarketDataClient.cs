using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients;
using Binance.Net.Objects.Models;
using Binance.Net.Objects.Models.Spot.Socket;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using CryptoIndicatorApp.Domain.Context;
using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Infrastructure.Binance;

public sealed class BinanceNetUsdFuturesMarketDataClient :
    IBinanceUsdFuturesMarketDataClient,
    IBinanceUsdFuturesSymbolProvider,
    IDisposable
{
    private readonly IBinanceRestClient _restClient;
    private readonly IBinanceSocketClient _socketClient;
    private readonly bool _ownsClients;

    public BinanceNetUsdFuturesMarketDataClient()
        : this(new BinanceConnectionOptions())
    {
    }

    public BinanceNetUsdFuturesMarketDataClient(BinanceConnectionOptions options)
        : this(CreateRestClient(options), CreateSocketClient(options), ownsClients: true)
    {
    }

    public BinanceNetUsdFuturesMarketDataClient(
        IBinanceRestClient restClient,
        IBinanceSocketClient socketClient)
        : this(restClient, socketClient, ownsClients: false)
    {
    }

    private BinanceNetUsdFuturesMarketDataClient(
        IBinanceRestClient restClient,
        IBinanceSocketClient socketClient,
        bool ownsClients)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        _socketClient = socketClient ?? throw new ArgumentNullException(nameof(socketClient));
        _ownsClients = ownsClients;
    }

    private static BinanceRestClient CreateRestClient(BinanceConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new BinanceRestClient(options.ApplyTo);
    }

    private static BinanceSocketClient CreateSocketClient(BinanceConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new BinanceSocketClient(options.ApplyTo);
    }

    public async Task<DepthSnapshotEvent> GetDepthSnapshotAsync(
        string symbol,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var receiveTime = DateTimeOffset.UtcNow;
        var result = await _restClient.UsdFuturesApi.ExchangeData.GetOrderBookAsync(
            NormalizeSymbol(symbol),
            limit,
            cancellationToken);

        ThrowIfFailed(result, "depth snapshot");

        var orderBook = result.Data;
        return BinanceMarketEventMapper.ToDepthSnapshot(
            symbol,
            orderBook.LastUpdateId,
            ToRawBookLevels(orderBook.Bids),
            ToRawBookLevels(orderBook.Asks),
            receiveTime,
            ToDateTimeOffset(orderBook.TransactionTime));
    }

    public async Task<IReadOnlyList<string>> GetActivePerpetualSymbolsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _restClient.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync(cancellationToken);
        ThrowIfFailed(result, "exchange info");

        var symbols = result.Data.Symbols.Select(symbol => new BinanceUsdFuturesSymbolMetadata(
            symbol.Name,
            $"{symbol.Status}",
            $"{symbol.ContractType}"));

        return BinanceUsdFuturesSymbolFilter.ActivePerpetualSymbols(symbols);
    }

    public async Task<IAsyncDisposable> SubscribeDepthUpdatesAsync(
        string symbol,
        Action<DepthUpdateEvent> onDepthUpdate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onDepthUpdate);

        var result = await _socketClient.UsdFuturesApi.ExchangeData.SubscribeToOrderBookUpdatesAsync(
            NormalizeSymbol(symbol),
            100,
            update =>
            {
                var data = update.Data;
                onDepthUpdate(BinanceMarketEventMapper.ToDepthUpdate(
                    data.Symbol,
                    data.FirstUpdateId ?? 0L,
                    data.LastUpdateId,
                    data.LastUpdateIdStream,
                    ToRawBookLevels(data.Bids),
                    ToRawBookLevels(data.Asks),
                    ToDateTimeOffset(data.TransactionTime),
                    ToDateTimeOffset(update.ReceiveTime)));
            },
            cancellationToken);

        ThrowIfFailed(result, "depth stream subscription");
        return new UpdateSubscriptionLease(result.Data);
    }

    public async Task<IAsyncDisposable> SubscribeAggTradesAsync(
        string symbol,
        Action<AggTradeEvent> onAggTrade,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onAggTrade);

        var result = await _socketClient.UsdFuturesApi.ExchangeData.SubscribeToAggregatedTradeUpdatesAsync(
            NormalizeSymbol(symbol),
            update =>
            {
                var data = update.Data;
                onAggTrade(BinanceMarketEventMapper.ToAggTrade(
                    data.Symbol,
                    data.Id,
                    data.Price,
                    data.Quantity,
                    data.FirstTradeId,
                    data.LastTradeId,
                    ToDateTimeOffset(data.TradeTime),
                    data.BuyerIsMaker,
                    ToDateTimeOffset(update.ReceiveTime)));
            },
            cancellationToken);

        ThrowIfFailed(result, "aggTrade stream subscription");
        return new UpdateSubscriptionLease(result.Data);
    }

    public async Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
        string symbol,
        ContextFrame frame,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var receiveTime = DateTimeOffset.UtcNow;
        var period = frame == ContextFrame.FiveMinutes
            ? PeriodInterval.FiveMinutes
            : PeriodInterval.FifteenMinutes;

        var result = await _restClient.UsdFuturesApi.ExchangeData.GetOpenInterestHistoryAsync(
            NormalizeSymbol(symbol),
            period,
            limit > 0 ? limit : null,
            startTime: null,
            endTime: null,
            ct: cancellationToken);

        ThrowIfFailed(result, "open interest history");

        return result.Data
            .Select(item =>
            {
                var timestamp = item.Timestamp.HasValue
                    ? ToDateTimeOffset(item.Timestamp.Value)
                    : receiveTime;

                return BinanceContextEventMapper.ToOpenInterest(
                    item.Symbol,
                    item.SumOpenInterest,
                    item.SumOpenInterestValue,
                    timestamp,
                    receiveTime);
            })
            .ToArray();
    }

    public async Task<IAsyncDisposable> SubscribeLiquidationsAsync(
        string symbol,
        Action<LiquidationEvent> onLiquidation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onLiquidation);

        var result = await _socketClient.UsdFuturesApi.ExchangeData.SubscribeToLiquidationUpdatesAsync(
            NormalizeSymbol(symbol),
            update =>
            {
                var data = update.Data;
                onLiquidation(BinanceContextEventMapper.ToLiquidation(
                    data.Symbol,
                    data.Side.ToString(),
                    data.AveragePrice,
                    data.QuantityFilled,
                    ToDateTimeOffset(data.Timestamp),
                    ToDateTimeOffset(update.ReceiveTime)));
            },
            cancellationToken);

        ThrowIfFailed(result, "liquidation stream subscription");
        return new UpdateSubscriptionLease(result.Data);
    }

    public void Dispose()
    {
        if (!_ownsClients)
        {
            return;
        }

        (_socketClient as IDisposable)?.Dispose();
        (_restClient as IDisposable)?.Dispose();
    }

    private static IEnumerable<BinanceRawBookLevel> ToRawBookLevels(IEnumerable<BinanceOrderBookEntry> entries)
    {
        return entries.Select(entry => new BinanceRawBookLevel(entry.Price, entry.Quantity));
    }

    private static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim().ToUpperInvariant();
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime dateTime)
    {
        return dateTime.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(dateTime),
            DateTimeKind.Local => new DateTimeOffset(dateTime).ToUniversalTime(),
            _ => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))
        };
    }

    private static void ThrowIfFailed(CallResult result, string operation)
    {
        if (result.Success)
        {
            return;
        }

        var message = result.Error?.Message ?? result.ToString();
        throw new InvalidOperationException($"Binance {operation} failed: {message}");
    }

    private sealed class UpdateSubscriptionLease(UpdateSubscription subscription) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await subscription.CloseAsync();
        }
    }
}
