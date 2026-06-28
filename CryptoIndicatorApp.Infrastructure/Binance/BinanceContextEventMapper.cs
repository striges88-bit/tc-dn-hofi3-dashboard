using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Infrastructure.Binance;

public static class BinanceContextEventMapper
{
    public static LiquidationEvent ToLiquidation(
        string symbol,
        string side,
        decimal averagePrice,
        decimal quantityFilled,
        DateTimeOffset tradeTime,
        DateTimeOffset receiveTime)
    {
        return new LiquidationEvent(
            NormalizeSymbol(symbol),
            side.Trim().ToUpperInvariant(),
            averagePrice,
            quantityFilled,
            tradeTime,
            tradeTime,
            receiveTime);
    }

    public static OpenInterestPoint ToOpenInterest(
        string symbol,
        decimal sumOpenInterest,
        decimal sumOpenInterestValue,
        DateTimeOffset timestamp,
        DateTimeOffset receiveTime)
    {
        return new OpenInterestPoint(
            NormalizeSymbol(symbol),
            sumOpenInterest,
            sumOpenInterestValue,
            timestamp,
            receiveTime);
    }

    private static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim().ToUpperInvariant();
    }
}
