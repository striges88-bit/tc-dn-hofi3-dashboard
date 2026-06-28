namespace CryptoIndicatorApp.Domain.Context;

public sealed record RobustMagnitudeResult(bool IsReady, double Intensity, decimal Scale);

public sealed class RobustMagnitudeNormalizer
{
    private readonly TimeSpan _historyWindow;
    private readonly int _minimumSamples;
    private readonly decimal _floor;
    private readonly List<(DateTimeOffset Timestamp, decimal Value)> _history = new();

    public RobustMagnitudeNormalizer(TimeSpan historyWindow, int minimumSamples, decimal floor)
    {
        _historyWindow = historyWindow <= TimeSpan.Zero ? TimeSpan.FromHours(24) : historyWindow;
        _minimumSamples = Math.Max(1, minimumSamples);
        _floor = floor <= 0m ? 0.00000001m : floor;
    }

    public void Add(DateTimeOffset timestamp, decimal signedValue)
    {
        _history.Add((timestamp, signedValue));
        Prune(timestamp);
    }

    public RobustMagnitudeResult Normalize(DateTimeOffset timestamp, decimal signedValue)
    {
        Prune(timestamp);

        if (_history.Count < _minimumSamples)
        {
            return new RobustMagnitudeResult(false, 0d, 0m);
        }

        var magnitudes = _history
            .Select(item => Math.Abs(item.Value))
            .OrderBy(value => value)
            .ToArray();
        var median = Median(magnitudes);
        var deviations = magnitudes
            .Select(value => Math.Abs(value - median))
            .OrderBy(value => value)
            .ToArray();
        var mad = Median(deviations);
        var scale = Math.Max(_floor, median + (1.4826m * mad));
        var strength = Math.Abs(signedValue) / scale;
        var intensity = Math.Clamp((double)(strength / 3m), 0d, 1d);

        return new RobustMagnitudeResult(true, intensity, scale);
    }

    private void Prune(DateTimeOffset now)
    {
        var cutoff = now - _historyWindow;
        _history.RemoveAll(item => item.Timestamp < cutoff);
    }

    private static decimal Median(IReadOnlyList<decimal> sorted)
    {
        if (sorted.Count == 0)
        {
            return 0m;
        }

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2m;
    }
}
