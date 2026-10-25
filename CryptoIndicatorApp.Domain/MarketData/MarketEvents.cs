namespace CryptoIndicatorApp.Domain.MarketData;

public interface IMarketEvent
{
    public string Symbol { get; }

    public DateTimeOffset ExchangeTime { get; }

    public DateTimeOffset ReceiveTime { get; }
}

public sealed record DepthSnapshotEvent(
    string Symbol,
    long LastUpdateId,
    IReadOnlyList<BookLevel> Bids,
    IReadOnlyList<BookLevel> Asks,
    DateTimeOffset ExchangeTime,
    DateTimeOffset ReceiveTime) : IMarketEvent;

public sealed record DepthUpdateEvent(
    string Symbol,
    long FirstUpdateId,
    long FinalUpdateId,
    long PreviousFinalUpdateId,
    IReadOnlyList<BookLevel> Bids,
    IReadOnlyList<BookLevel> Asks,
    DateTimeOffset ExchangeTime,
    DateTimeOffset ReceiveTime) : IMarketEvent;

public sealed record AggTradeEvent(
    string Symbol,
    long AggregateTradeId,
    decimal Price,
    decimal Quantity,
    long FirstTradeId,
    long LastTradeId,
    DateTimeOffset TradeTime,
    bool IsBuyerMaker,
    DateTimeOffset ExchangeTime,
    DateTimeOffset ReceiveTime) : IMarketEvent
{
    public AggressorSide AggressorSide => TradeClassifier.GetAggressorSide(IsBuyerMaker);
}
