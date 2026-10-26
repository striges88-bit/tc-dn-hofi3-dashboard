using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Application.Context;

public interface IContextDataSource
{
    public Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
        string symbol,
        ContextFrame frame,
        int limit,
        CancellationToken cancellationToken = default);

    public IAsyncEnumerable<LiquidationEvent> ReadLiquidationsAsync(
        string symbol,
        CancellationToken cancellationToken = default);
}
