using System.IO;
using CryptoIndicatorApp.Desktop.Composition;
using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Desktop.Tests;

public sealed class JsonlCompositionAdapterTests
{
    [Fact]
    public async Task Jsonl_recorder_and_source_round_trip_market_events_through_application_interfaces()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "events.jsonl");
        var recorder = JsonlMarketEventRecorder.Create(path);
        var source = JsonlMarketEventSource.Create(path);
        var timestamp = DateTimeOffset.Parse("2026-05-25T08:00:00.100Z");
        var marketEvent = new AggTradeEvent(
            "BTCUSDT",
            AggregateTradeId: 10,
            Price: 100m,
            Quantity: 2m,
            FirstTradeId: 20,
            LastTradeId: 21,
            TradeTime: timestamp,
            IsBuyerMaker: false,
            ExchangeTime: timestamp,
            ReceiveTime: timestamp.AddMilliseconds(3));

        await recorder.AppendAsync(marketEvent);

        var events = await source.ReadAllAsync().ToListAsync();
        var trade = Assert.IsType<AggTradeEvent>(Assert.Single(events));
        Assert.Equal("BTCUSDT", trade.Symbol);
        Assert.Equal(10, trade.AggregateTradeId);
        Assert.False(trade.IsBuyerMaker);
        Assert.Equal(timestamp.AddMilliseconds(3), trade.ReceiveTime);
    }
}
