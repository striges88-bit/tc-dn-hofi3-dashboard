using CryptoIndicatorApp.Domain.Indicators;

namespace CryptoIndicatorApp.LiveDryRun;

public sealed class IndicatorSampleSummaryCollector
{
    private readonly List<IndicatorSample> _samples = new();

    public void Add(IndicatorSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        _samples.Add(sample);
    }

    public IndicatorSampleSummary Build()
    {
        if (_samples.Count == 0)
        {
            return IndicatorSampleSummary.Empty;
        }

        var latencies = _samples
            .Select(sample => sample.ExchangeToReceiveLatency?.TotalMilliseconds)
            .Where(latency => latency.HasValue)
            .Select(latency => latency!.Value)
            .Order()
            .ToArray();

        return new IndicatorSampleSummary(
            Count: _samples.Count,
            FirstTimestamp: _samples[0].Timestamp,
            LastTimestamp: _samples[^1].Timestamp,
            MinZOfi: _samples.Min(sample => sample.ZOfi),
            MaxZOfi: _samples.Max(sample => sample.ZOfi),
            MaxAbsZOfi: _samples.Max(sample => Math.Abs(sample.ZOfi)),
            MinTfi: _samples.Min(sample => sample.Tfi),
            MaxTfi: _samples.Max(sample => sample.Tfi),
            AverageAbsTfi: _samples.Average(sample => Math.Abs(sample.Tfi)),
            LongCandidateCount: _samples.Count(sample => sample.Signal == SignalState.LongCandidate),
            ShortCandidateCount: _samples.Count(sample => sample.Signal == SignalState.ShortCandidate),
            NeutralCount: _samples.Count(sample => sample.Signal == SignalState.Neutral),
            UnsyncedSampleCount: _samples.Count(sample => !sample.BookHealth.IsSynced),
            MaxResyncCount: _samples.Max(sample => sample.BookHealth.ResyncCount),
            LatencyP50Ms: Percentile(latencies, 50),
            LatencyP95Ms: Percentile(latencies, 95),
            LatencyP99Ms: Percentile(latencies, 99));
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0d;
        }

        var index = (int)Math.Ceiling(sortedValues.Count * percentile / 100d) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
    }
}

public sealed record IndicatorSampleSummary(
    int Count,
    DateTimeOffset? FirstTimestamp,
    DateTimeOffset? LastTimestamp,
    decimal MinZOfi,
    decimal MaxZOfi,
    decimal MaxAbsZOfi,
    decimal MinTfi,
    decimal MaxTfi,
    decimal AverageAbsTfi,
    int LongCandidateCount,
    int ShortCandidateCount,
    int NeutralCount,
    int UnsyncedSampleCount,
    int MaxResyncCount,
    double LatencyP50Ms,
    double LatencyP95Ms,
    double LatencyP99Ms)
{
    public static IndicatorSampleSummary Empty { get; } = new(
        Count: 0,
        FirstTimestamp: null,
        LastTimestamp: null,
        MinZOfi: 0m,
        MaxZOfi: 0m,
        MaxAbsZOfi: 0m,
        MinTfi: 0m,
        MaxTfi: 0m,
        AverageAbsTfi: 0m,
        LongCandidateCount: 0,
        ShortCandidateCount: 0,
        NeutralCount: 0,
        UnsyncedSampleCount: 0,
        MaxResyncCount: 0,
        LatencyP50Ms: 0d,
        LatencyP95Ms: 0d,
        LatencyP99Ms: 0d);
}
