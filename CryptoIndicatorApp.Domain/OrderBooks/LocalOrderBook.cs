using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Domain.OrderBooks;

public sealed class LocalOrderBook
{
    private readonly SortedDictionary<decimal, decimal> _bids = new(Comparer<decimal>.Create((left, right) => right.CompareTo(left)));
    private readonly SortedDictionary<decimal, decimal> _asks = new();
    private long? _snapshotLastUpdateId;

    public LocalOrderBook(string symbol)
    {
        Symbol = symbol;
        Health = BookHealth.Empty;
    }

    public string Symbol { get; }

    public BookHealth Health { get; private set; }

    public void ApplySnapshot(DepthSnapshotEvent snapshot)
    {
        EnsureSymbol(snapshot.Symbol);
        _bids.Clear();
        _asks.Clear();

        foreach (var bid in snapshot.Bids)
        {
            ApplyLevel(_bids, bid);
        }

        foreach (var ask in snapshot.Asks)
        {
            ApplyLevel(_asks, ask);
        }

        _snapshotLastUpdateId = snapshot.LastUpdateId;
        Health = Health with
        {
            IsSynced = false,
            IsCrossed = IsCrossed(),
            LastUpdateId = snapshot.LastUpdateId,
            Reason = "snapshot_loaded"
        };
    }

    public void ApplyDepthUpdate(DepthUpdateEvent update)
    {
        EnsureSymbol(update.Symbol);

        if (_snapshotLastUpdateId is null)
        {
            MarkInvalid("no_snapshot");
            return;
        }

        if (!Health.IsSynced)
        {
            var nextUpdateId = _snapshotLastUpdateId.Value + 1;
            if (update.FirstUpdateId > nextUpdateId || update.FinalUpdateId < nextUpdateId)
            {
                return;
            }
        }
        else if (update.PreviousFinalUpdateId != Health.LastUpdateId)
        {
            MarkInvalid("sequence_gap");
            return;
        }

        foreach (var bid in update.Bids)
        {
            ApplyLevel(_bids, bid);
        }

        foreach (var ask in update.Asks)
        {
            ApplyLevel(_asks, ask);
        }

        Health = Health with
        {
            IsSynced = true,
            IsCrossed = IsCrossed(),
            LastUpdateId = update.FinalUpdateId,
            Reason = null
        };
    }

    public IReadOnlyList<BookLevel> GetTopBids(int count)
    {
        return _bids.Take(count).Select(level => new BookLevel(level.Key, level.Value)).ToArray();
    }

    public IReadOnlyList<BookLevel> GetTopAsks(int count)
    {
        return _asks.Take(count).Select(level => new BookLevel(level.Key, level.Value)).ToArray();
    }

    private static void ApplyLevel(SortedDictionary<decimal, decimal> side, BookLevel level)
    {
        if (level.Quantity <= 0)
        {
            side.Remove(level.Price);
            return;
        }

        side[level.Price] = level.Quantity;
    }

    private bool IsCrossed()
    {
        return _bids.Count > 0 && _asks.Count > 0 && _bids.First().Key >= _asks.First().Key;
    }

    private void MarkInvalid(string reason)
    {
        Health = Health with
        {
            IsSynced = false,
            IsCrossed = IsCrossed(),
            ResyncCount = Health.ResyncCount + 1,
            Reason = reason
        };
    }

    private void EnsureSymbol(string symbol)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(Symbol, symbol))
        {
            throw new ArgumentException($"Event symbol '{symbol}' does not match order book symbol '{Symbol}'.", nameof(symbol));
        }
    }
}
