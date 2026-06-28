namespace CryptoIndicatorApp.Domain.Context;

public sealed class OpenInterestContextCalculator
{
    private readonly ContextFrame _frame;
    private readonly TimeSpan _visibleDuration;
    private readonly TimeSpan _normalizationHistory;
    private readonly int _minimumNormalizationSamples;
    private readonly decimal _normalizationFloor;
    private readonly SortedDictionary<DateTimeOffset, OpenInterestDelta> _deltasByBucket = new();

    public OpenInterestContextCalculator(
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

    public decimal? LatestOpenInterestValue { get; private set; }

    public void LoadHistory(IEnumerable<OpenInterestPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var ordered = points
            .OrderBy(point => point.Timestamp)
            .ToArray();

        if (ordered.Length > 0)
        {
            LatestOpenInterestValue = ordered[^1].SumOpenInterestValue > 0m
                ? ordered[^1].SumOpenInterestValue
                : null;
        }

        for (var index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];
            if (previous.SumOpenInterestValue <= 0m)
            {
                continue;
            }

            var rawDelta = current.SumOpenInterestValue - previous.SumOpenInterestValue;
            var normalizedDelta = rawDelta / previous.SumOpenInterestValue;
            var bucketStart = BucketStart(current.Timestamp, _frame.ToDuration());
            _deltasByBucket[bucketStart] = new OpenInterestDelta(rawDelta, normalizedDelta);
        }
    }

    public IReadOnlyList<ContextTile> Snapshot(DateTimeOffset now)
    {
        Prune(now);

        var duration = _frame.ToDuration();
        var visibleStarts = VisibleBucketStarts(now, duration).ToArray();
        var normalizer = new RobustMagnitudeNormalizer(
            _normalizationHistory,
            _minimumNormalizationSamples,
            _normalizationFloor);

        foreach (var item in _deltasByBucket.Where(item => item.Value.NormalizedDelta != 0m))
        {
            normalizer.Add(item.Key, item.Value.NormalizedDelta);
        }

        return visibleStarts
            .Select(bucketStart => CreateTile(bucketStart, duration, normalizer))
            .ToArray();
    }

    private ContextTile CreateTile(
        DateTimeOffset bucketStart,
        TimeSpan duration,
        RobustMagnitudeNormalizer normalizer)
    {
        if (!_deltasByBucket.TryGetValue(bucketStart, out var delta))
        {
            return new ContextTile(
                bucketStart,
                bucketStart + duration,
                RawDelta: 0m,
                NormalizedDelta: 0m,
                ContextDirection.Neutral,
                Intensity: 0d,
                IsReady: true,
                Status: "No change");
        }

        var direction = ContextDirectionExtensions.FromSignedValue(delta.RawDelta);
        if (delta.NormalizedDelta == 0m)
        {
            return new ContextTile(
                bucketStart,
                bucketStart + duration,
                delta.RawDelta,
                delta.NormalizedDelta,
                ContextDirection.Neutral,
                Intensity: 0d,
                IsReady: true,
                Status: "No change");
        }

        var result = normalizer.Normalize(bucketStart, delta.NormalizedDelta);

        return new ContextTile(
            bucketStart,
            bucketStart + duration,
            delta.RawDelta,
            delta.NormalizedDelta,
            direction,
            result.Intensity,
            result.IsReady,
            result.IsReady ? "Ready" : "Warming up");
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
        var staleKeys = _deltasByBucket.Keys
            .Where(bucketStart => bucketStart < cutoff)
            .ToArray();

        foreach (var staleKey in staleKeys)
        {
            _deltasByBucket.Remove(staleKey);
        }
    }

    private static DateTimeOffset BucketStart(DateTimeOffset timestamp, TimeSpan frame)
    {
        var ticks = timestamp.UtcDateTime.Ticks / frame.Ticks * frame.Ticks;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private sealed record OpenInterestDelta(decimal RawDelta, decimal NormalizedDelta);
}
