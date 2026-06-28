using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CryptoIndicatorApp.Application.Context;
using CryptoIndicatorApp.Desktop.Configuration;
using CryptoIndicatorApp.Domain.Context;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Desktop.Composition;

public sealed class BinanceContextDataSource : IContextDataSource, IDisposable
{
    private readonly BinanceNetUsdFuturesMarketDataClient _client;

    private BinanceContextDataSource(BinanceNetUsdFuturesMarketDataClient client)
    {
        _client = client;
    }

    public static BinanceContextDataSource Create(ProxyOptions? proxyOptions = null)
    {
        var options = new BinanceConnectionOptions
        {
            Proxy = BinanceProxyOptionsMapper.ToInfrastructure(proxyOptions)
        };

        return new BinanceContextDataSource(new BinanceNetUsdFuturesMarketDataClient(options));
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

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
