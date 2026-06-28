using CryptoIndicatorApp.Domain.Indicators;
using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Domain.Tests;

public class OfiCalculatorTests
{
    [Fact]
    public void CalculatesCksLevelOfiForBidAndAskPriceMoves()
    {
        var previousBid = new BookLevel(100m, 5m);
        var previousAsk = new BookLevel(101m, 7m);
        var currentBid = new BookLevel(101m, 6m);
        var currentAsk = new BookLevel(100.5m, 4m);

        var ofi = OfiCalculator.CalculateLevelOfi(previousBid, previousAsk, currentBid, currentAsk);

        Assert.Equal(2m, ofi);
    }

    [Fact]
    public void CalculatesTop3WeightedHofiWithConfiguredDecay()
    {
        var hofi = OfiCalculator.CalculateWeightedHofi(new[] { 10m, 5m, -2m }, lambda: 0.8m);

        Assert.Equal(7.1722m, Math.Round(hofi, 4));
    }
}
