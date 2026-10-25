using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Application.MarketData;

public interface IMarketEventSource
{
    public IAsyncEnumerable<IMarketEvent> ReadAllAsync(CancellationToken cancellationToken = default);
}
