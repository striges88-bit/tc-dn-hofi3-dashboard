using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Infrastructure.Composition;

public sealed class BinanceLiveMarketEventSource : IMarketEventSource, IDisposable
{
    private readonly BinanceUsdFuturesLiveMarketEventSource _source;
    private readonly IDisposable? _owned;

    public BinanceLiveMarketEventSource(BinanceUsdFuturesLiveMarketEventSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    private BinanceLiveMarketEventSource(BinanceUsdFuturesLiveMarketEventSource source, IDisposable owned)
        : this(source)
    {
        _owned = owned ?? throw new ArgumentNullException(nameof(owned));
    }

    public static BinanceLiveMarketEventSource Create(string symbol, BinanceProxyOptions? proxy = null)
    {
        var client = new BinanceNetUsdFuturesMarketDataClient(new BinanceConnectionOptions
        {
            Proxy = proxy ?? new BinanceProxyOptions()
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
        _owned?.Dispose();
    }
}
