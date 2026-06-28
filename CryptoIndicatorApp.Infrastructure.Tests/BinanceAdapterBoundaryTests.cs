using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public class BinanceAdapterBoundaryTests
{
    [Fact]
    public void UsesUsdMFuturesHotPathStreamNames()
    {
        Assert.Equal("btcusdt@depth@100ms", BinanceUsdFuturesStreamNames.DepthDiff100ms("BTCUSDT"));
        Assert.Equal("btcusdt@aggTrade", BinanceUsdFuturesStreamNames.AggTrade("BTCUSDT"));
    }

    [Fact]
    public void MapsDepthUpdatePayloadToDomainEvent()
    {
        var exchangeTime = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var receiveTime = exchangeTime.AddMilliseconds(15);

        var update = BinanceMarketEventMapper.ToDepthUpdate(
            symbol: "BTCUSDT",
            firstUpdateId: 100,
            finalUpdateId: 105,
            previousFinalUpdateId: 99,
            bids: new[] { new BinanceRawBookLevel(100m, 2m) },
            asks: new[] { new BinanceRawBookLevel(101m, 3m) },
            exchangeTime: exchangeTime,
            receiveTime: receiveTime);

        Assert.Equal("BTCUSDT", update.Symbol);
        Assert.Equal(100, update.FirstUpdateId);
        Assert.Equal(105, update.FinalUpdateId);
        Assert.Equal(99, update.PreviousFinalUpdateId);
        Assert.Equal(100m, update.Bids.Single().Price);
        Assert.Equal(receiveTime, update.ReceiveTime);
    }

    [Fact]
    public void MapsSnapshotPayloadToDomainEvent()
    {
        var receiveTime = DateTimeOffset.Parse("2026-05-25T08:00:00.020Z");

        var snapshot = BinanceMarketEventMapper.ToDepthSnapshot(
            symbol: "BTCUSDT",
            lastUpdateId: 250,
            bids: new[] { new BinanceRawBookLevel(100m, 2m) },
            asks: new[] { new BinanceRawBookLevel(101m, 3m) },
            receiveTime: receiveTime);

        Assert.Equal("BTCUSDT", snapshot.Symbol);
        Assert.Equal(250, snapshot.LastUpdateId);
        Assert.Equal(100m, snapshot.Bids.Single().Price);
        Assert.Equal(receiveTime, snapshot.ExchangeTime);
        Assert.Equal(receiveTime, snapshot.ReceiveTime);
    }

    [Fact]
    public void MapsAggTradePayloadToDomainEventWithBuyerMakerSide()
    {
        var tradeTime = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var receiveTime = tradeTime.AddMilliseconds(12);

        var trade = BinanceMarketEventMapper.ToAggTrade(
            symbol: "BTCUSDT",
            aggregateTradeId: 17,
            price: 100m,
            quantity: 0.5m,
            firstTradeId: 11,
            lastTradeId: 12,
            tradeTime: tradeTime,
            isBuyerMaker: true,
            receiveTime: receiveTime);

        Assert.Equal("BTCUSDT", trade.Symbol);
        Assert.Equal(17, trade.AggregateTradeId);
        Assert.Equal(AggressorSide.Sell, trade.AggressorSide);
        Assert.Equal(tradeTime, trade.ExchangeTime);
        Assert.Equal(receiveTime, trade.ReceiveTime);
    }
}
