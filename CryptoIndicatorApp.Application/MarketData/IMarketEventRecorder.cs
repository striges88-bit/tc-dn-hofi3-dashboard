using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Application.MarketData;

public interface IMarketEventRecorder
{
    ValueTask AppendAsync(IMarketEvent marketEvent, CancellationToken cancellationToken = default);
}
