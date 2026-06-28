using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Domain.Tests;

public sealed class OpenInterestContextCalculatorTests
{
    [Fact]
    public void Open_interest_tiles_use_value_delta_percentage()
    {
        var calculator = new OpenInterestContextCalculator(
            ContextFrame.FiveMinutes,
            visibleDuration: TimeSpan.FromMinutes(15),
            normalizationHistory: TimeSpan.FromHours(24),
            minimumNormalizationSamples: 1,
            normalizationFloor: 0.00000001m);

        var start = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        calculator.LoadHistory(new[]
        {
            new OpenInterestPoint("XANUSDT", 1000m, 100_000m, start, start),
            new OpenInterestPoint("XANUSDT", 1000m, 105_000m, start.AddMinutes(5), start.AddMinutes(5)),
            new OpenInterestPoint("XANUSDT", 1000m, 102_900m, start.AddMinutes(10), start.AddMinutes(10))
        });

        var tiles = calculator.Snapshot(start.AddMinutes(10));

        Assert.Contains(tiles, tile => tile.RawDelta == 5_000m
            && tile.NormalizedDelta == 0.05m
            && tile.Direction == ContextDirection.Positive);
        Assert.Contains(tiles, tile => tile.RawDelta == -2_100m
            && tile.NormalizedDelta == -0.02m
            && tile.Direction == ContextDirection.Negative);
    }
}
