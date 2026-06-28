using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Application.Context;

public interface IContextDataSource
{
    Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
        string symbol,
        ContextFrame frame,
        int limit,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<LiquidationEvent> ReadLiquidationsAsync(
        string symbol,
        CancellationToken cancellationToken = default);
}
