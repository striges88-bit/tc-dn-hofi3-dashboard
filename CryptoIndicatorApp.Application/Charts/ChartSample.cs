namespace CryptoIndicatorApp.Application.Charts;

public sealed record ChartSample(
    DateTimeOffset Timestamp,
    decimal ZOfi,
    decimal Tfi);
