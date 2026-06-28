using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Domain.Tests;

public sealed class RobustMagnitudeNormalizerTests
{
    [Fact]
    public void Context_frame_maps_to_expected_duration()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), ContextFrame.FiveMinutes.ToDuration());
        Assert.Equal(TimeSpan.FromMinutes(15), ContextFrame.FifteenMinutes.ToDuration());
    }

    [Fact]
    public void Normalizer_requires_minimum_history_before_emitting_intensity()
    {
        var normalizer = new RobustMagnitudeNormalizer(
            historyWindow: TimeSpan.FromHours(24),
            minimumSamples: 3,
            floor: 0.00000001m);

        var timestamp = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        normalizer.Add(timestamp, 0.001m);
        normalizer.Add(timestamp.AddMinutes(5), -0.002m);

        var result = normalizer.Normalize(timestamp.AddMinutes(10), 0.003m);

        Assert.False(result.IsReady);
        Assert.Equal(0d, result.Intensity);
    }

    [Fact]
    public void Normalizer_uses_absolute_magnitude_for_intensity_without_changing_direction()
    {
        var normalizer = new RobustMagnitudeNormalizer(
            historyWindow: TimeSpan.FromHours(24),
            minimumSamples: 3,
            floor: 0.00000001m);

        var timestamp = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        normalizer.Add(timestamp, 0.001m);
        normalizer.Add(timestamp.AddMinutes(5), -0.0015m);
        normalizer.Add(timestamp.AddMinutes(10), 0.002m);

        var result = normalizer.Normalize(timestamp.AddMinutes(15), -0.009m);

        Assert.True(result.IsReady);
        Assert.Equal(ContextDirection.Negative, ContextDirectionExtensions.FromSignedValue(-0.009m));
        Assert.InRange(result.Intensity, 0.01d, 1d);
    }
}
