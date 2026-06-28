using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Application.Context;

public sealed record ContextModuleSample(
    string Symbol,
    ContextFrame Frame,
    DateTimeOffset Timestamp,
    IReadOnlyList<ContextTile> LiquidationTiles,
    IReadOnlyList<ContextTile> OpenInterestTiles,
    string LiquidationStatus,
    string OpenInterestStatus);
