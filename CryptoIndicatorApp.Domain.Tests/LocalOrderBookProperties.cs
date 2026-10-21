using CsCheck;
using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Domain.OrderBooks;
using Xunit;

namespace CryptoIndicatorApp.Domain.Tests;

/// <summary>
/// Formal check of the depth-book sync state machine in
/// <see cref="LocalOrderBook"/> against a reference implementation of the
/// documented Binance linking rule, over generated event sequences.
///
/// Property, in plain words: while the book reports IsSynced=true, its
/// contents, LastUpdateId, and IsCrossed flag equal the reference book built
/// by the strict Binance procedure; a break is either ignored or marks the
/// book unsynced, and never yields a silently wrong book.
///
/// Scope and exclusions are recorded in
/// .cursor/skills/verify-formal-orderbook-sync/SKILL.md.
/// </summary>
public class LocalOrderBookProperties
{
    private const string Symbol = "BTCUSDT";
    private const long StartCursor = 1_000L;
    private const int MaxLevelCount = 4;

    private static readonly DateTimeOffset FixedTime =
        DateTimeOffset.Parse("2026-10-18T12:00:00.000Z");

    private enum PuKind
    {
        Correct = 0,
        Lost = 1,
        Ahead = 2,
        Duplicate = 3
    }

    private sealed record LevelStep(int PriceTick, int QtyKind);

    private sealed record Step(
        bool IsSnapshot,
        int SnapshotDelta,
        int Overlap,
        int Width,
        PuKind Pu,
        LevelStep[] Bids,
        LevelStep[] Asks);

    private static readonly Gen<LevelStep> GenLevel =
        Gen.Select(Gen.Int[-4, 4], Gen.Int[0, 3], (tick, qty) => new LevelStep(tick, qty));

    private static readonly Gen<LevelStep[]> GenSide = GenLevel.Array[0, MaxLevelCount];

    private static readonly Gen<PuKind> GenPu = Gen.Int[0, 3].Select(i => (PuKind)i);

    private static readonly Gen<Step> GenSnapshotStep =
        Gen.Select(Gen.Int[0, 3], GenSide, GenSide,
            (delta, bids, asks) => new Step(true, delta, 0, 0, PuKind.Correct, bids, asks));

    private static readonly Gen<Step> GenUpdateStep =
        Gen.Select(Gen.Int[0, 3], Gen.Int[1, 4], GenPu, GenSide, GenSide,
            (overlap, width, pu, bids, asks) => new Step(false, 0, overlap, width, pu, bids, asks));

    private static readonly Gen<Step> GenStep =
        from kind in Gen.Int[0, 9]
        from step in kind <= 1 ? GenSnapshotStep : GenUpdateStep
        select step;

    private static readonly Gen<Step[]> GenSteps = GenStep.Array[3, 40];

    [Fact]
    public void SyncedBookAlwaysMatchesReferenceBook()
    {
        GenSteps.Sample(steps =>
        {
            var book = new LocalOrderBook(Symbol);
            var oracle = new ReferenceBook();
            var rendered = new List<string>();

            foreach (var step in steps)
            {
                if (step.IsSnapshot)
                {
                    var snapshotId = oracle.Cursor + step.SnapshotDelta;
                    rendered.Add($"snapshot id={snapshotId} bids={Render(step.Bids)} asks={Render(step.Asks)}");
                    book.ApplySnapshot(MakeSnapshot(snapshotId, step));
                    oracle.ApplySnapshot(snapshotId, step);
                    oracle.Cursor = snapshotId;
                    oracle.PreviousFinalId = snapshotId;
                }
                else
                {
                    var finalId = step.Pu == PuKind.Duplicate && oracle.PreviousFinalId is long duplicate
                        ? duplicate
                        : oracle.Cursor + step.Width;
                    var firstId = Math.Max(1, Math.Min(oracle.Cursor - step.Overlap, finalId));
                    var anchor = oracle.PreviousFinalId ?? firstId;
                    var previousFinalId = step.Pu switch
                    {
                        PuKind.Correct => anchor,
                        PuKind.Lost => anchor - 1 - (step.Overlap % 3),
                        PuKind.Ahead => anchor + 1,
                        PuKind.Duplicate => anchor,
                        _ => throw new InvalidOperationException($"Unhandled PuKind '{step.Pu}'.")
                    };
                    rendered.Add($"update U={firstId} u={finalId} pu={previousFinalId} bids={Render(step.Bids)} asks={Render(step.Asks)}");
                    book.ApplyDepthUpdate(MakeUpdate(firstId, finalId, previousFinalId, step));
                    oracle.ApplyUpdate(firstId, finalId, previousFinalId, step);
                    oracle.Cursor = Math.Max(oracle.Cursor, finalId);
                    oracle.PreviousFinalId = finalId;
                }

                AssertInvariants(book, oracle, rendered);
            }

            return true;
        }, iter: 2000);
    }

    private static void AssertInvariants(LocalOrderBook book, ReferenceBook oracle, List<string> rendered)
    {
        var trace = string.Join(Environment.NewLine, rendered);
        var health = book.Health;
        Assert.Equal(oracle.ResyncCount, health.ResyncCount);

        if (oracle.Synced)
        {
            Assert.True(health.IsSynced, $"Book lost sync the reference keeps.{Environment.NewLine}{trace}");
            Assert.Equal(oracle.LastUpdateId, health.LastUpdateId);
            Assert.Equal(ReferenceBook.ExpectedCrossed(oracle.Bids, oracle.Asks), health.IsCrossed);
            Assert.Null(health.Reason);
        }
        else
        {
            Assert.False(health.IsSynced, $"Book claims sync the reference rejects.{Environment.NewLine}{trace}");
        }
    }

    private static DepthSnapshotEvent MakeSnapshot(long snapshotId, Step step)
    {
        return new DepthSnapshotEvent(
            Symbol,
            snapshotId,
            ToLevels(step.Bids),
            ToLevels(step.Asks),
            FixedTime,
            FixedTime);
    }

    private static DepthUpdateEvent MakeUpdate(long firstId, long finalId, long previousFinalId, Step step)
    {
        return new DepthUpdateEvent(
            Symbol,
            firstId,
            finalId,
            previousFinalId,
            ToLevels(step.Bids),
            ToLevels(step.Asks),
            FixedTime,
            FixedTime);
    }

    private static IReadOnlyList<BookLevel> ToLevels(LevelStep[] steps)
    {
        return steps.Select(level => new BookLevel(Price(level.PriceTick), Quantity(level.QtyKind))).ToArray();
    }

    private static decimal Price(int tick)
    {
        return 100m + tick * 0.5m;
    }

    private static decimal Quantity(int kind)
    {
        return kind switch
        {
            0 => 0m,
            1 => 0.5m,
            2 => 1m,
            _ => 2m
        };
    }

    private static string Render(LevelStep[] levels)
    {
        return string.Join(",", levels.Select(level => $"{Price(level.PriceTick):0.0}x{Quantity(level.QtyKind):0.0}"));
    }

    /// <summary>
    /// Reference implementation of the documented Binance depth-linking
    /// procedure. It decides only whether the book is synced and what its
    /// contents are while synced; it does not mirror LocalOrderBook internals.
    /// </summary>
    private sealed class ReferenceBook
    {
        private readonly SortedDictionary<decimal, decimal> _bids = new(Comparer<decimal>.Create((left, right) => right.CompareTo(left)));
        private readonly SortedDictionary<decimal, decimal> _asks = new();

        public long Cursor { get; set; } = StartCursor;

        public long? PreviousFinalId { get; set; }

        public long? SnapshotId { get; private set; }

        public bool Synced { get; private set; }

        public long? LastUpdateId { get; private set; }

        public int ResyncCount { get; private set; }

        public IReadOnlyDictionary<decimal, decimal> Bids => _bids;

        public IReadOnlyDictionary<decimal, decimal> Asks => _asks;

        public void ApplySnapshot(long snapshotId, Step step)
        {
            _bids.Clear();
            _asks.Clear();
            ApplyLevels(_bids, step.Bids);
            ApplyLevels(_asks, step.Asks);
            SnapshotId = snapshotId;
            Synced = false;
            LastUpdateId = null;
        }

        public void ApplyUpdate(long firstId, long finalId, long previousFinalId, Step step)
        {
            if (SnapshotId is null)
            {
                ResyncCount++;
                return;
            }

            if (!Synced)
            {
                var connectId = SnapshotId.Value + 1;
                if (firstId > connectId || finalId < connectId)
                {
                    return;
                }
            }
            else if (previousFinalId != LastUpdateId)
            {
                ResyncCount++;
                Synced = false;
                LastUpdateId = null;
                return;
            }

            ApplyLevels(_bids, step.Bids);
            ApplyLevels(_asks, step.Asks);
            Synced = true;
            LastUpdateId = finalId;
        }

        public static bool ExpectedCrossed(IReadOnlyDictionary<decimal, decimal> bids, IReadOnlyDictionary<decimal, decimal> asks)
        {
            return bids.Count > 0 && asks.Count > 0 && bids.Keys.Max() >= asks.Keys.Min();
        }

        private static void ApplyLevels(SortedDictionary<decimal, decimal> side, LevelStep[] levels)
        {
            foreach (var level in levels)
            {
                var price = Price(level.PriceTick);
                var quantity = Quantity(level.QtyKind);
                if (quantity <= 0m)
                {
                    side.Remove(price);
                    continue;
                }

                side[price] = quantity;
            }
        }
    }
}