using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class BinanceSymbolProviderTests
{
    [Fact]
    public void Active_perpetual_filter_returns_only_trading_perpetual_symbols_sorted()
    {
        var symbols = new[]
        {
            new BinanceUsdFuturesSymbolMetadata("XANUSDT", "TRADING", "PERPETUAL"),
            new BinanceUsdFuturesSymbolMetadata("OLDUSDT", "SETTLING", "PERPETUAL"),
            new BinanceUsdFuturesSymbolMetadata("BTCUSDT_260626", "TRADING", "CURRENT_QUARTER"),
            new BinanceUsdFuturesSymbolMetadata("ESPORTSUSDT", "TRADING", "PERPETUAL"),
            new BinanceUsdFuturesSymbolMetadata(" ", "TRADING", "PERPETUAL"),
            new BinanceUsdFuturesSymbolMetadata("PLAYUSDT", "Trading", "Perpetual")
        };

        var active = BinanceUsdFuturesSymbolFilter.ActivePerpetualSymbols(symbols);

        Assert.Equal(
            new[] { "ESPORTSUSDT", "PLAYUSDT", "XANUSDT" },
            active);
    }
}
