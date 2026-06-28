using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Infrastructure.Jsonl;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public class JsonlEventStoreTests
{
    [Fact]
    public async Task WritesAndReadsDepthUpdateEnvelope()
    {
        var path = Path.Combine(Path.GetTempPath(), $"indic-{Guid.NewGuid():N}.jsonl");
        var store = new JsonlMarketEventStore();
        var time = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var update = new DepthUpdateEvent(
            Symbol: "BTCUSDT",
            FirstUpdateId: 10,
            FinalUpdateId: 11,
            PreviousFinalUpdateId: 9,
            Bids: new[] { new BookLevel(100m, 2m) },
            Asks: new[] { new BookLevel(101m, 3m) },
            ExchangeTime: time,
            ReceiveTime: time.AddMilliseconds(20));

        await store.AppendAsync(path, update, CancellationToken.None);

        var events = await store.ReadAsync(path, CancellationToken.None).ToListAsync();

        var actual = Assert.IsType<DepthUpdateEvent>(Assert.Single(events));
        Assert.Equal("BTCUSDT", actual.Symbol);
        Assert.Equal(10, actual.FirstUpdateId);
        Assert.Equal(11, actual.FinalUpdateId);
        Assert.Equal(9, actual.PreviousFinalUpdateId);
        Assert.Equal(100m, actual.Bids.Single().Price);
    }

    [Fact]
    public async Task RejectsUnsupportedSchemaVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"indic-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, """
            {"schemaVersion":2,"source":"binance-usdsm-futures","stream":"depth","eventType":"depthUpdate","symbol":"BTCUSDT","exchangeTime":"2026-05-25T08:00:00Z","receiveTime":"2026-05-25T08:00:00Z","payload":{}}
            """);

        var store = new JsonlMarketEventStore();

        var ex = await Assert.ThrowsAsync<JsonlReplayException>(async () =>
            await store.ReadAsync(path, CancellationToken.None).ToListAsync());
        Assert.Contains("Unsupported schema version", ex.Message);
    }

    [Fact]
    public async Task MalformedJsonStopsReplay()
    {
        var path = Path.Combine(Path.GetTempPath(), $"indic-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, "{not json}");

        var store = new JsonlMarketEventStore();

        var ex = await Assert.ThrowsAsync<JsonlReplayException>(async () =>
            await store.ReadAsync(path, CancellationToken.None).ToListAsync());
        Assert.Contains("Malformed JSONL row 1", ex.Message);
    }
}
