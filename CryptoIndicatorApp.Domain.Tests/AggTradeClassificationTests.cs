using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Domain.Tests;

public class AggTradeClassificationTests
{
    [Fact]
    public void BuyerMakerTradeIsAggressiveSell()
    {
        Assert.Equal(AggressorSide.Sell, TradeClassifier.GetAggressorSide(isBuyerMaker: true));
    }

    [Fact]
    public void BuyerTakerTradeIsAggressiveBuy()
    {
        Assert.Equal(AggressorSide.Buy, TradeClassifier.GetAggressorSide(isBuyerMaker: false));
    }
}
