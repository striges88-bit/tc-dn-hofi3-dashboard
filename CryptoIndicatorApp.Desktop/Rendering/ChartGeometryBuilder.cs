using System.Windows;
using CryptoIndicatorApp.Application.Charts;

namespace CryptoIndicatorApp.Desktop.Rendering;

public static class ChartGeometryBuilder
{
    public static IReadOnlyList<Point> BuildPoints(
        IReadOnlyList<ChartSample> samples,
        Func<ChartSample, decimal> valueSelector,
        double width,
        double height)
    {
        return BuildPoints(
            samples,
            valueSelector,
            width,
            height,
            CalculateMaxAbs(samples, sample => sample.ZOfi, sample => sample.Tfi));
    }

    public static IReadOnlyList<Point> BuildPoints(
        IReadOnlyList<ChartSample> samples,
        Func<ChartSample, decimal> valueSelector,
        double width,
        double height,
        double maxAbs)
    {
        if (samples.Count == 0 || width <= 1d || height <= 1d)
        {
            return Array.Empty<Point>();
        }

        maxAbs = Math.Max(1d, maxAbs);

        var start = samples[0].Timestamp;
        var end = samples[^1].Timestamp;
        var spanMilliseconds = Math.Max(1d, (end - start).TotalMilliseconds);
        var midY = height / 2d;
        var scale = height * 0.42d / maxAbs;
        var points = new Point[samples.Count];

        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            var x = (sample.Timestamp - start).TotalMilliseconds / spanMilliseconds * width;
            var y = midY - ((double)valueSelector(sample) * scale);
            points[index] = new Point(x, Math.Clamp(y, 0d, height));
        }

        return points;
    }

    public static double CalculateMaxAbs(
        IReadOnlyList<ChartSample> samples,
        params Func<ChartSample, decimal>[] valueSelectors)
    {
        if (samples.Count == 0 || valueSelectors.Length == 0)
        {
            return 1d;
        }

        var maxAbs = samples
            .SelectMany(sample => valueSelectors.Select(selector => Math.Abs((double)selector(sample))))
            .DefaultIfEmpty(1d)
            .Max();

        return Math.Max(1d, maxAbs);
    }

    public static IReadOnlyList<Point> BuildZeroLine(double width, double height)
    {
        if (width <= 1d || height <= 1d)
        {
            return Array.Empty<Point>();
        }

        var midY = height / 2d;
        return new[]
        {
            new Point(0d, midY),
            new Point(width, midY)
        };
    }
}
