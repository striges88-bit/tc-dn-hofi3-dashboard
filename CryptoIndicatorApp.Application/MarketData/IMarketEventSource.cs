using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Application.MarketData;

public interface IMarketEventSource
{
    IAsyncEnumerable<IMarketEvent> ReadAllAsync(CancellationToken cancellationToken = default);
}
