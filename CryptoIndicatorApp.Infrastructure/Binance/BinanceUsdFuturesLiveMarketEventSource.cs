using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Infrastructure.Binance;

public sealed class BinanceUsdFuturesLiveMarketEventSource
{
    public const int DefaultSnapshotLimit = 1000;

    private readonly string _symbol;
    private readonly IBinanceUsdFuturesMarketDataClient _client;
    private readonly int _snapshotLimit;

    public BinanceUsdFuturesLiveMarketEventSource(
        string symbol,
        IBinanceUsdFuturesMarketDataClient client,
        int snapshotLimit = DefaultSnapshotLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        _symbol = symbol.Trim().ToUpperInvariant();
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _snapshotLimit = snapshotLimit;
    }

    public async IAsyncEnumerable<IMarketEvent> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var streamEvents = Channel.CreateUnbounded<IMarketEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var depthSubscription = await _client.SubscribeDepthUpdatesAsync(
            _symbol,
            update => streamEvents.Writer.TryWrite(update),
            cancellationToken);
        var tradeSubscription = await _client.SubscribeAggTradesAsync(
            _symbol,
            trade => streamEvents.Writer.TryWrite(trade),
            cancellationToken);

        try
        {
            var snapshot = await GetSnapshotAsync(cancellationToken);
            yield return snapshot;

            var snapshotLastUpdateId = snapshot.LastUpdateId;
            long? previousFinalUpdateId = null;
            var isSynced = false;

            await foreach (var marketEvent in streamEvents.Reader.ReadAllAsync(cancellationToken))
            {
                if (marketEvent is not DepthUpdateEvent depthUpdate)
                {
                    yield return marketEvent;
                    continue;
                }

                if (!isSynced)
                {
                    if (depthUpdate.FinalUpdateId < snapshotLastUpdateId)
                    {
                        continue;
                    }

                    if (!OverlapsSnapshot(depthUpdate, snapshotLastUpdateId))
                    {
                        if (depthUpdate.FirstUpdateId > snapshotLastUpdateId + 1)
                        {
                            snapshot = await GetSnapshotAsync(cancellationToken);
                            snapshotLastUpdateId = snapshot.LastUpdateId;
                            previousFinalUpdateId = null;
                            yield return snapshot;
                        }

                        continue;
                    }

                    isSynced = true;
                    previousFinalUpdateId = depthUpdate.FinalUpdateId;
                    yield return depthUpdate;
                    continue;
                }

                if (previousFinalUpdateId is not null && depthUpdate.PreviousFinalUpdateId != previousFinalUpdateId.Value)
                {
                    yield return depthUpdate;

                    snapshot = await GetSnapshotAsync(cancellationToken);
                    snapshotLastUpdateId = snapshot.LastUpdateId;
                    previousFinalUpdateId = null;
                    isSynced = false;
                    yield return snapshot;
                    continue;
                }

                previousFinalUpdateId = depthUpdate.FinalUpdateId;
                yield return depthUpdate;
            }
        }
        finally
        {
            await tradeSubscription.DisposeAsync();
            await depthSubscription.DisposeAsync();
        }
    }

    private Task<DepthSnapshotEvent> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return _client.GetDepthSnapshotAsync(_symbol, _snapshotLimit, cancellationToken);
    }

    private static bool OverlapsSnapshot(DepthUpdateEvent update, long snapshotLastUpdateId)
    {
        var nextUpdateId = snapshotLastUpdateId + 1;
        return update.FirstUpdateId <= nextUpdateId && update.FinalUpdateId >= nextUpdateId;
    }
}
