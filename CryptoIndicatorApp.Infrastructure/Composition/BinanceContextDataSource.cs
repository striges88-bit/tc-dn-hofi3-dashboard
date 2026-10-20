using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CryptoIndicatorApp.Application.Context;
using CryptoIndicatorApp.Domain.Context;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Infrastructure.Composition;

public sealed class BinanceContextDataSource : IContextDataSource, IDisposable
{
    private readonly IBinanceUsdFuturesMarketDataClient _client;
    private readonly IDisposable? _owned;

    public BinanceContextDataSource(IBinanceUsdFuturesMarketDataClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    private BinanceContextDataSource(IBinanceUsdFuturesMarketDataClient client, IDisposable owned)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _owned = owned ?? throw new ArgumentNullException(nameof(owned));
    }

    public bool LiquidationSubscriptionConnected { get; private set; }

    public static BinanceContextDataSource Create(BinanceProxyOptions? proxy = null)
    {
        var client = new BinanceNetUsdFuturesMarketDataClient(new BinanceConnectionOptions
        {
            Proxy = proxy ?? new BinanceProxyOptions()
        });
        return new BinanceContextDataSource(client, client);
    }

    public Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
        string symbol,
        ContextFrame frame,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return _client.GetOpenInterestHistoryAsync(symbol, frame, limit, cancellationToken);
    }

    public async IAsyncEnumerable<LiquidationEvent> ReadLiquidationsAsync(
        string symbol,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<LiquidationEvent>();
        await using var lease = await _client.SubscribeLiquidationsAsync(
            symbol,
            item => channel.Writer.TryWrite(item),
            cancellationToken);
        LiquidationSubscriptionConnected = true;

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    public void Dispose()
    {
        _owned?.Dispose();
    }
}
