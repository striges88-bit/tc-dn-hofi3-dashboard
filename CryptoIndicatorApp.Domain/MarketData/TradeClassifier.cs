namespace CryptoIndicatorApp.Domain.MarketData;

public static class TradeClassifier
{
    public static AggressorSide GetAggressorSide(bool isBuyerMaker)
    {
        return isBuyerMaker ? AggressorSide.Sell : AggressorSide.Buy;
    }
}
