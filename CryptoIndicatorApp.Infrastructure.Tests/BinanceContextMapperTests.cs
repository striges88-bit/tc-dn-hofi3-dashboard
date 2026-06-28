using CryptoIndicatorApp.Domain.Context;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class BinanceContextMapperTests
{
    [Fact]
    public void Liquidation_mapper_normalizes_symbol_and_keeps_side()
    {
        var tradeTime = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        var receiveTime = tradeTime.AddMilliseconds(100);

        var item = BinanceContextEventMapper.ToLiquidation(
            "xanusdt",
            "BUY",
            averagePrice: 10m,
            quantityFilled: 2m,
            tradeTime,
            receiveTime);

        Assert.Equal("XANUSDT", item.Symbol);
        Assert.Equal("BUY", item.Side);
        Assert.Equal(20m, item.SignedNotional);
    }

    [Fact]
    public void Open_interest_mapper_normalizes_symbol_and_keeps_value()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        var receiveTime = timestamp.AddMilliseconds(80);

        var point = BinanceContextEventMapper.ToOpenInterest(
            "xanusdt",
            sumOpenInterest: 123m,
            sumOpenInterestValue: 456m,
            timestamp,
            receiveTime);

        Assert.Equal("XANUSDT", point.Symbol);
        Assert.Equal(456m, point.SumOpenInterestValue);
    }
}
