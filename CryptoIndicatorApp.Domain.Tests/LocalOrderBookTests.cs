using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Domain.OrderBooks;

namespace CryptoIndicatorApp.Domain.Tests;

public class LocalOrderBookTests
{
    [Fact]
    public void FirstDepthUpdateSynchronizesWhenItOverlapsSnapshot()
    {
        var time = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var book = new LocalOrderBook("BTCUSDT");

        book.ApplySnapshot(new DepthSnapshotEvent(
            Symbol: "BTCUSDT",
            LastUpdateId: 100,
            Bids: new[] { new BookLevel(100m, 2m), new BookLevel(99m, 1m) },
            Asks: new[] { new BookLevel(101m, 3m), new BookLevel(102m, 1m) },
            ExchangeTime: time,
            ReceiveTime: time));

        book.ApplyDepthUpdate(new DepthUpdateEvent(
            Symbol: "BTCUSDT",
            FirstUpdateId: 99,
            FinalUpdateId: 101,
            PreviousFinalUpdateId: 100,
            Bids: new[] { new BookLevel(100m, 4m) },
            Asks: Array.Empty<BookLevel>(),
            ExchangeTime: time.AddMilliseconds(100),
            ReceiveTime: time.AddMilliseconds(105)));

        Assert.True(book.Health.IsSynced);
        Assert.Equal(101, book.Health.LastUpdateId);
        Assert.Equal(4m, book.GetTopBids(1).Single().Quantity);
    }

    [Fact]
    public void GapAfterSyncInvalidatesBookAndIncrementsResyncCount()
    {
        var time = DateTimeOffset.Parse("2026-05-25T08:00:00.000Z");
        var book = CreateSyncedBook(time);

        book.ApplyDepthUpdate(new DepthUpdateEvent(
            Symbol: "BTCUSDT",
            FirstUpdateId: 105,
            FinalUpdateId: 106,
            PreviousFinalUpdateId: 104,
            Bids: new[] { new BookLevel(100m, 5m) },
            Asks: Array.Empty<BookLevel>(),
            ExchangeTime: time.AddMilliseconds(200),
            ReceiveTime: time.AddMilliseconds(205)));

        Assert.False(book.Health.IsSynced);
        Assert.Equal(1, book.Health.ResyncCount);
        Assert.Equal("sequence_gap", book.Health.Reason);
    }

    private static LocalOrderBook CreateSyncedBook(DateTimeOffset time)
    {
        var book = new LocalOrderBook("BTCUSDT");
        book.ApplySnapshot(new DepthSnapshotEvent(
            Symbol: "BTCUSDT",
            LastUpdateId: 100,
            Bids: new[] { new BookLevel(100m, 2m) },
            Asks: new[] { new BookLevel(101m, 3m) },
            ExchangeTime: time,
            ReceiveTime: time));
        book.ApplyDepthUpdate(new DepthUpdateEvent(
            Symbol: "BTCUSDT",
            FirstUpdateId: 99,
            FinalUpdateId: 101,
            PreviousFinalUpdateId: 100,
            Bids: Array.Empty<BookLevel>(),
            Asks: Array.Empty<BookLevel>(),
            ExchangeTime: time.AddMilliseconds(100),
            ReceiveTime: time.AddMilliseconds(105)));
        return book;
    }
}
