using CryptoIndicatorApp.Domain.Indicators;
using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Domain.Tests;

public class RollingTradeFlowTests
{
    [Fact]
    public void CalculatesTfiFromAggressiveNotionalInsideRollingWindow()
    {
        var clock = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var flow = new RollingTradeFlow(TimeSpan.FromMilliseconds(250));

        flow.Add(CreateTrade(clock.AddMilliseconds(-300), isBuyerMaker: false, price: 100m, quantity: 100m));
        flow.Add(CreateTrade(clock.AddMilliseconds(-200), isBuyerMaker: false, price: 1000m, quantity: 1m));
        flow.Add(CreateTrade(clock.AddMilliseconds(-100), isBuyerMaker: true, price: 10m, quantity: 10m));

        var tfi = flow.Calculate(clock);

        Assert.Equal(0.8182m, Math.Round(tfi, 4));
    }

    private static AggTradeEvent CreateTrade(DateTimeOffset time, bool isBuyerMaker, decimal price, decimal quantity)
    {
        return new AggTradeEvent(
            Symbol: "BTCUSDT",
            AggregateTradeId: 1,
            Price: price,
            Quantity: quantity,
            FirstTradeId: 10,
            LastTradeId: 11,
            TradeTime: time,
            IsBuyerMaker: isBuyerMaker,
            ExchangeTime: time,
            ReceiveTime: time.AddMilliseconds(5));
    }
}
