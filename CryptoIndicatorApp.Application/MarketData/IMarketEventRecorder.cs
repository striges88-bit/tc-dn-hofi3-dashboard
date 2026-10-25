using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Application.MarketData;

public interface IMarketEventRecorder
{
    public ValueTask AppendAsync(IMarketEvent marketEvent, CancellationToken cancellationToken = default);
}
