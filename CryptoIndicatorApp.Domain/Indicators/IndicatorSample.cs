using CryptoIndicatorApp.Domain.OrderBooks;

namespace CryptoIndicatorApp.Domain.Indicators;

public enum SignalState
{
    Neutral,
    LongCandidate,
    ShortCandidate
}

public sealed record IndicatorSample(
    DateTimeOffset Timestamp,
    decimal Hofi,
    decimal Nofi,
    decimal ZOfi,
    decimal Tfi,
    SignalState Signal,
    BookHealth BookHealth,
    TimeSpan? ExchangeToReceiveLatency);
