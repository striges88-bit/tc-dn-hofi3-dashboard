using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Infrastructure.Binance;

public static class BinanceMarketEventMapper
{
    public static DepthSnapshotEvent ToDepthSnapshot(
        string symbol,
        long lastUpdateId,
        IEnumerable<BinanceRawBookLevel> bids,
        IEnumerable<BinanceRawBookLevel> asks,
        DateTimeOffset receiveTime,
        DateTimeOffset? exchangeTime = null)
    {
        return new DepthSnapshotEvent(
            NormalizeEventSymbol(symbol),
            lastUpdateId,
            ToBookLevels(bids),
            ToBookLevels(asks),
            exchangeTime ?? receiveTime,
            receiveTime);
    }

    public static DepthUpdateEvent ToDepthUpdate(
        string symbol,
        long firstUpdateId,
        long finalUpdateId,
        long previousFinalUpdateId,
        IEnumerable<BinanceRawBookLevel> bids,
        IEnumerable<BinanceRawBookLevel> asks,
        DateTimeOffset exchangeTime,
        DateTimeOffset receiveTime)
    {
        return new DepthUpdateEvent(
            NormalizeEventSymbol(symbol),
            firstUpdateId,
            finalUpdateId,
            previousFinalUpdateId,
            ToBookLevels(bids),
            ToBookLevels(asks),
            exchangeTime,
            receiveTime);
    }

    public static AggTradeEvent ToAggTrade(
        string symbol,
        long aggregateTradeId,
        decimal price,
        decimal quantity,
        long firstTradeId,
        long lastTradeId,
        DateTimeOffset tradeTime,
        bool isBuyerMaker,
        DateTimeOffset receiveTime)
    {
        return new AggTradeEvent(
            NormalizeEventSymbol(symbol),
            aggregateTradeId,
            price,
            quantity,
            firstTradeId,
            lastTradeId,
            tradeTime,
            isBuyerMaker,
            tradeTime,
            receiveTime);
    }

    private static IReadOnlyList<BookLevel> ToBookLevels(IEnumerable<BinanceRawBookLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        return levels.Select(level => new BookLevel(level.Price, level.Quantity)).ToArray();
    }

    private static string NormalizeEventSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim().ToUpperInvariant();
    }
}
