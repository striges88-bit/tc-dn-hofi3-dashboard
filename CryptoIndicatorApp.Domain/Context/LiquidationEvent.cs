namespace CryptoIndicatorApp.Domain.Context;

public sealed record LiquidationEvent(
    string Symbol,
    string Side,
    decimal AveragePrice,
    decimal QuantityFilled,
    DateTimeOffset TradeTime,
    DateTimeOffset ExchangeTime,
    DateTimeOffset ReceiveTime)
{
    public decimal Notional => AveragePrice * QuantityFilled;

    public decimal SignedNotional => string.Equals(Side, "BUY", StringComparison.OrdinalIgnoreCase)
        ? Notional
        : string.Equals(Side, "SELL", StringComparison.OrdinalIgnoreCase)
            ? -Notional
            : 0m;
}
