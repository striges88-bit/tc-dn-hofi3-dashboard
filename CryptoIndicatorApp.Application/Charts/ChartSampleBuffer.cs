using CryptoIndicatorApp.Domain.Indicators;

namespace CryptoIndicatorApp.Application.Charts;

public sealed class ChartSampleBuffer
{
    private readonly Queue<ChartSample> _samples = new();
    private readonly TimeSpan _retentionWindow;

    public ChartSampleBuffer(TimeSpan retentionWindow)
    {
        if (retentionWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionWindow), "Retention window must be positive.");
        }

        _retentionWindow = retentionWindow;
    }

    public void Add(IndicatorSample sample)
    {
        var point = new ChartSample(sample.Timestamp, sample.ZOfi, sample.Tfi);
        _samples.Enqueue(point);
        Trim(point.Timestamp);
    }

    public IReadOnlyList<ChartSample> Snapshot()
    {
        return _samples.ToArray();
    }

    public void Clear()
    {
        _samples.Clear();
    }

    private void Trim(DateTimeOffset latestTimestamp)
    {
        var cutoff = latestTimestamp - _retentionWindow;
        while (_samples.Count > 0 && _samples.Peek().Timestamp < cutoff)
        {
            _samples.Dequeue();
        }
    }
}
