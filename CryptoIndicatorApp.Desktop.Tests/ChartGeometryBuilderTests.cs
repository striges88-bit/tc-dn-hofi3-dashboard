using CryptoIndicatorApp.Application.Charts;
using CryptoIndicatorApp.Desktop.Rendering;
using System.Windows;

namespace CryptoIndicatorApp.Desktop.Tests;

public sealed class ChartGeometryBuilderTests
{
    [Fact]
    public void Build_points_returns_non_empty_points_for_samples_and_stable_size()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var samples = new[]
        {
            new ChartSample(timestamp, 1.0m, -0.5m),
            new ChartSample(timestamp.AddMilliseconds(250), 2.0m, 0.5m)
        };

        var points = ChartGeometryBuilder.BuildPoints(samples, sample => sample.ZOfi, width: 320, height: 180);

        Assert.Equal(2, points.Count);
        Assert.All(points, point =>
        {
            Assert.InRange(point.X, 0d, 320d);
            Assert.InRange(point.Y, 0d, 180d);
        });
    }

    [Fact]
    public void Build_points_returns_empty_points_when_chart_has_no_stable_height()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var samples = new[]
        {
            new ChartSample(timestamp, 1.0m, 0m)
        };

        var points = ChartGeometryBuilder.BuildPoints(samples, sample => sample.ZOfi, width: 320, height: 1);

        Assert.Empty(points);
    }

    [Fact]
    public void Build_zero_line_spans_chart_width_at_vertical_midpoint()
    {
        var points = ChartGeometryBuilder.BuildZeroLine(width: 320, height: 180);

        Assert.Equal(2, points.Count);
        Assert.Equal(new Point(0, 90), points[0]);
        Assert.Equal(new Point(320, 90), points[1]);
    }

    [Fact]
    public void Raw_tfi_overlay_stays_subtle_next_to_larger_zofi()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var samples = new[]
        {
            new ChartSample(timestamp, 0m, 0m),
            new ChartSample(timestamp.AddMilliseconds(250), 3.0m, 0.15m)
        };
        var maxAbs = ChartGeometryBuilder.CalculateMaxAbs(samples, sample => sample.ZOfi, sample => sample.Tfi);

        var zOfiPoints = ChartGeometryBuilder.BuildPoints(
            samples,
            sample => sample.ZOfi,
            width: 320,
            height: 180,
            maxAbs: maxAbs);
        var tfiPoints = ChartGeometryBuilder.BuildPoints(
            samples,
            sample => sample.Tfi,
            width: 320,
            height: 180,
            maxAbs: maxAbs);

        Assert.True(Math.Abs(zOfiPoints[1].Y - 90d) > 70d);
        Assert.True(Math.Abs(tfiPoints[1].Y - 90d) < 8d);
    }
}
