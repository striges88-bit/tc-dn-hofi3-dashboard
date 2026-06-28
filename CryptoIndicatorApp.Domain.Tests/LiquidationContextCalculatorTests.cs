using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Domain.Tests;

public sealed class LiquidationContextCalculatorTests
{
    [Fact]
    public void Liquidation_bucket_uses_buy_minus_sell_notional_and_oi_denominator()
    {
        var calculator = new LiquidationContextCalculator(
            ContextFrame.FiveMinutes,
            visibleDuration: TimeSpan.FromMinutes(15),
            normalizationHistory: TimeSpan.FromHours(24),
            minimumNormalizationSamples: 1,
            normalizationFloor: 0.00000001m);

        var start = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        calculator.SetOpenInterestValue(100_000m);
        calculator.Add(new LiquidationEvent("XANUSDT", "BUY", 10m, 20m, start.AddMinutes(1), start.AddMinutes(1), start.AddMinutes(1)));
        calculator.Add(new LiquidationEvent("XANUSDT", "SELL", 5m, 10m, start.AddMinutes(2), start.AddMinutes(2), start.AddMinutes(2)));

        var tiles = calculator.Snapshot(start.AddMinutes(5));

        var tile = Assert.Single(tiles.Where(item => item.RawDelta != 0m));
        Assert.Equal(150m, tile.RawDelta);
        Assert.Equal(0.0015m, tile.NormalizedDelta);
        Assert.Equal(ContextDirection.Positive, tile.Direction);
        Assert.True(tile.IsReady);
    }

    [Fact]
    public void Liquidation_tile_is_not_ready_without_open_interest_value()
    {
        var calculator = new LiquidationContextCalculator(
            ContextFrame.FiveMinutes,
            visibleDuration: TimeSpan.FromMinutes(15),
            normalizationHistory: TimeSpan.FromHours(24),
            minimumNormalizationSamples: 1,
            normalizationFloor: 0.00000001m);

        var start = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        calculator.Add(new LiquidationEvent("XANUSDT", "BUY", 10m, 20m, start, start, start));

        var tile = Assert.Single(calculator.Snapshot(start.AddMinutes(5)).Where(item => item.RawDelta != 0m));

        Assert.Null(tile.NormalizedDelta);
        Assert.False(tile.IsReady);
        Assert.Equal("Waiting for OI", tile.Status);
    }
}
