using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Domain.Indicators;

public sealed class RollingTradeFlow
{
    private readonly Queue<AggTradeEvent> _trades = new();
    private readonly TimeSpan _window;

    public RollingTradeFlow(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be positive.");
        }

        _window = window;
    }

    public void Add(AggTradeEvent trade)
    {
        _trades.Enqueue(trade);
        Trim(trade.TradeTime);
    }

    public decimal Calculate(DateTimeOffset timestamp, decimal epsilon = 0.000000001m)
    {
        Trim(timestamp);
        var buy = _trades.Where(trade => trade.AggressorSide == AggressorSide.Buy).Sum(CalculateNotional);
        var sell = _trades.Where(trade => trade.AggressorSide == AggressorSide.Sell).Sum(CalculateNotional);
        return (buy - sell) / (buy + sell + epsilon);
    }

    private static decimal CalculateNotional(AggTradeEvent trade)
    {
        return trade.Price * trade.Quantity;
    }

    private void Trim(DateTimeOffset timestamp)
    {
        var cutoff = timestamp - _window;
        while (_trades.Count > 0 && _trades.Peek().TradeTime < cutoff)
        {
            _trades.Dequeue();
        }
    }
}
