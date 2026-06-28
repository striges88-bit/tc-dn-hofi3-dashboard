namespace CryptoIndicatorApp.Infrastructure.Binance;

public static class BinanceUsdFuturesStreamNames
{
    public static string DepthDiff100ms(string symbol)
    {
        return $"{NormalizeSymbol(symbol)}@depth@100ms";
    }

    public static string AggTrade(string symbol)
    {
        return $"{NormalizeSymbol(symbol)}@aggTrade";
    }

    private static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim().ToLowerInvariant();
    }
}
