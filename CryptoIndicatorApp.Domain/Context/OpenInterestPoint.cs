namespace CryptoIndicatorApp.Domain.Context;

public sealed record OpenInterestPoint(
    string Symbol,
    decimal SumOpenInterest,
    decimal SumOpenInterestValue,
    DateTimeOffset Timestamp,
    DateTimeOffset ReceiveTime);
