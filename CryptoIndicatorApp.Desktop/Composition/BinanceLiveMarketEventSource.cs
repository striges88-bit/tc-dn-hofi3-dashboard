using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Desktop.Configuration;
using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Desktop.Composition;

public sealed class BinanceLiveMarketEventSource : IMarketEventSource, IDisposable
{
    private readonly BinanceUsdFuturesLiveMarketEventSource _source;
    private readonly IDisposable? _disposable;

    public BinanceLiveMarketEventSource(
        BinanceUsdFuturesLiveMarketEventSource source,
        IDisposable? disposable = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _disposable = disposable;
    }

    public static BinanceLiveMarketEventSource Create(string symbol, ProxyOptions? proxyOptions = null)
    {
        var client = new BinanceNetUsdFuturesMarketDataClient(new BinanceConnectionOptions
        {
            Proxy = BinanceProxyOptionsMapper.ToInfrastructure(proxyOptions)
        });
        return new BinanceLiveMarketEventSource(
            new BinanceUsdFuturesLiveMarketEventSource(symbol, client),
            client);
    }

    public IAsyncEnumerable<IMarketEvent> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _source.ReadAllAsync(cancellationToken);
    }

    public void Dispose()
    {
        _disposable?.Dispose();
    }
}
