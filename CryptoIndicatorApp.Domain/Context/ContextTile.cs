namespace CryptoIndicatorApp.Domain.Context;

public sealed record ContextTile(
    DateTimeOffset BucketStart,
    DateTimeOffset BucketEnd,
    decimal RawDelta,
    decimal? NormalizedDelta,
    ContextDirection Direction,
    double Intensity,
    bool IsReady,
    string Status);
