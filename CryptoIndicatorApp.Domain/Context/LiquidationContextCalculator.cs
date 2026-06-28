namespace CryptoIndicatorApp.Domain.Context;

public sealed class LiquidationContextCalculator
{
    private readonly ContextFrame _frame;
    private readonly TimeSpan _visibleDuration;
    private readonly TimeSpan _normalizationHistory;
    private readonly int _minimumNormalizationSamples;
    private readonly decimal _normalizationFloor;
    private readonly SortedDictionary<DateTimeOffset, decimal> _signedNotionalByBucket = new();
    private decimal? _latestOpenInterestValue;

    public LiquidationContextCalculator(
        ContextFrame frame,
        TimeSpan visibleDuration,
        TimeSpan normalizationHistory,
        int minimumNormalizationSamples,
        decimal normalizationFloor)
    {
        _frame = frame;
        _visibleDuration = visibleDuration <= TimeSpan.Zero ? TimeSpan.FromMinutes(150) : visibleDuration;
        _normalizationHistory = normalizationHistory <= TimeSpan.Zero ? TimeSpan.FromHours(24) : normalizationHistory;
        _minimumNormalizationSamples = Math.Max(1, minimumNormalizationSamples);
        _normalizationFloor = normalizationFloor <= 0m ? 0.00000001m : normalizationFloor;
    }

    public void SetOpenInterestValue(decimal openInterestValue)
    {
        _latestOpenInterestValue = openInterestValue > 0m ? openInterestValue : null;
    }

    public void Add(LiquidationEvent liquidation)
    {
        var bucketStart = BucketStart(liquidation.TradeTime, _frame.ToDuration());
        _signedNotionalByBucket[bucketStart] = _signedNotionalByBucket.TryGetValue(bucketStart, out var current)
            ? current + liquidation.SignedNotional
            : liquidation.SignedNotional;
    }

    public IReadOnlyList<ContextTile> Snapshot(DateTimeOffset now)
    {
        Prune(now);

        var duration = _frame.ToDuration();
        var visibleStarts = VisibleBucketStarts(now, duration).ToArray();
        var normalizedByBucket = BuildNormalizedDeltas();
        var normalizer = new RobustMagnitudeNormalizer(
            _normalizationHistory,
            _minimumNormalizationSamples,
            _normalizationFloor);

        foreach (var item in normalizedByBucket.Where(item => item.Value is not null && item.Value.Value != 0m))
        {
            normalizer.Add(item.Key, item.Value!.Value);
        }

        return visibleStarts
            .Select(bucketStart => CreateTile(bucketStart, duration, normalizedByBucket, normalizer))
            .ToArray();
    }

    private ContextTile CreateTile(
        DateTimeOffset bucketStart,
        TimeSpan duration,
        IReadOnlyDictionary<DateTimeOffset, decimal?> normalizedByBucket,
        RobustMagnitudeNormalizer normalizer)
    {
        var rawDelta = _signedNotionalByBucket.GetValueOrDefault(bucketStart);
        var direction = ContextDirectionExtensions.FromSignedValue(rawDelta);

        if (_latestOpenInterestValue is null && rawDelta != 0m)
        {
            return new ContextTile(
                bucketStart,
                bucketStart + duration,
                rawDelta,
                NormalizedDelta: null,
                ContextDirection.Unavailable,
                Intensity: 0d,
                IsReady: false,
                Status: "Waiting for OI");
        }

        var normalizedDelta = normalizedByBucket.GetValueOrDefault(bucketStart);
        if (normalizedDelta is null)
        {
            return new ContextTile(
                bucketStart,
                bucketStart + duration,
                rawDelta,
                NormalizedDelta: null,
                ContextDirection.Unavailable,
                Intensity: 0d,
                IsReady: false,
                Status: rawDelta == 0m ? "No events" : "Waiting for OI");
        }

        if (normalizedDelta.Value == 0m)
        {
            return new ContextTile(
                bucketStart,
                bucketStart + duration,
                rawDelta,
                normalizedDelta,
                ContextDirection.Neutral,
                Intensity: 0d,
                IsReady: true,
                Status: "No events");
        }

        var result = normalizer.Normalize(bucketStart, normalizedDelta.Value);

        return new ContextTile(
            bucketStart,
            bucketStart + duration,
            rawDelta,
            normalizedDelta,
            direction,
            result.Intensity,
            result.IsReady,
            result.IsReady ? "Ready" : "Warming up");
    }

    private IReadOnlyDictionary<DateTimeOffset, decimal?> BuildNormalizedDeltas()
    {
        if (_latestOpenInterestValue is null)
        {
            return _signedNotionalByBucket.ToDictionary(item => item.Key, _ => (decimal?)null);
        }

        return _signedNotionalByBucket.ToDictionary(
            item => item.Key,
            item => (decimal?)(item.Value / _latestOpenInterestValue.Value));
    }

    private IEnumerable<DateTimeOffset> VisibleBucketStarts(DateTimeOffset now, TimeSpan duration)
    {
        var count = _frame.VisibleTileCount(_visibleDuration);
        var lastStart = BucketStart(now, duration);
        var firstStart = lastStart - TimeSpan.FromTicks(duration.Ticks * (count - 1));

        for (var index = 0; index < count; index++)
        {
            yield return firstStart + TimeSpan.FromTicks(duration.Ticks * index);
        }
    }

    private void Prune(DateTimeOffset now)
    {
        var cutoff = BucketStart(now - _normalizationHistory, _frame.ToDuration());
        var staleKeys = _signedNotionalByBucket.Keys
            .Where(bucketStart => bucketStart < cutoff)
            .ToArray();

        foreach (var staleKey in staleKeys)
        {
            _signedNotionalByBucket.Remove(staleKey);
        }
    }

    private static DateTimeOffset BucketStart(DateTimeOffset timestamp, TimeSpan frame)
    {
        var ticks = timestamp.UtcDateTime.Ticks / frame.Ticks * frame.Ticks;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
