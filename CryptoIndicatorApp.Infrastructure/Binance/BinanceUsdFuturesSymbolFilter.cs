namespace CryptoIndicatorApp.Infrastructure.Binance;

public static class BinanceUsdFuturesSymbolFilter
{
    public static IReadOnlyList<string> ActivePerpetualSymbols(
        IEnumerable<BinanceUsdFuturesSymbolMetadata> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        return symbols
            .Where(IsActivePerpetual)
            .Select(symbol => symbol.Symbol.Trim().ToUpperInvariant())
            .Where(symbol => symbol.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsActivePerpetual(BinanceUsdFuturesSymbolMetadata symbol)
    {
        return IsMatch(symbol.Status, "TRADING")
            && IsMatch(symbol.ContractType, "PERPETUAL");
    }

    private static bool IsMatch(string value, string expected)
    {
        return string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
