using System.Runtime.CompilerServices;
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Application.Context;

public sealed class ContextModuleSession
{
    private readonly string _symbol;
    private readonly ContextFrame _frame;
    private readonly IContextDataSource _source;
    private readonly TimeSpan _visibleDuration;
    private readonly TimeSpan _normalizationHistory;
    private readonly int _minimumNormalizationSamples;
    private readonly decimal _normalizationFloor;
    private readonly int _openInterestHistoryLimit;
    private readonly TimeSpan _openInterestRefreshInterval;
    private readonly IContextRefreshClock _refreshClock;

    public ContextModuleSession(
        string symbol,
        ContextFrame frame,
        IContextDataSource source,
        TimeSpan visibleDuration,
        TimeSpan normalizationHistory,
        int minimumNormalizationSamples,
        decimal normalizationFloor,
        int openInterestHistoryLimit = 288,
        TimeSpan openInterestRefreshInterval = default,
        IContextRefreshClock? refreshClock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        _symbol = symbol.Trim().ToUpperInvariant();
        _frame = frame;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _visibleDuration = visibleDuration <= TimeSpan.Zero ? TimeSpan.FromMinutes(150) : visibleDuration;
        _normalizationHistory = normalizationHistory <= TimeSpan.Zero ? TimeSpan.FromHours(24) : normalizationHistory;
        _minimumNormalizationSamples = Math.Max(1, minimumNormalizationSamples);
        _normalizationFloor = normalizationFloor <= 0m ? 0.00000001m : normalizationFloor;
        _openInterestHistoryLimit = openInterestHistoryLimit > 0 ? openInterestHistoryLimit : 288;
        _openInterestRefreshInterval = openInterestRefreshInterval > TimeSpan.Zero
            ? openInterestRefreshInterval
            : TimeSpan.Zero;
        _refreshClock = refreshClock ?? PeriodicContextRefreshClock.Instance;
    }

    public async IAsyncEnumerable<ContextModuleSample> RunAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var openInterest = new OpenInterestContextCalculator(
            _frame,
            _visibleDuration,
            _normalizationHistory,
            _minimumNormalizationSamples,
            _normalizationFloor);
        var liquidations = new LiquidationContextCalculator(
            _frame,
            _visibleDuration,
            _normalizationHistory,
            _minimumNormalizationSamples,
            _normalizationFloor);

        var openInterestHistory = await LoadOpenInterestHistoryAsync(cancellationToken);

        openInterest.LoadHistory(openInterestHistory);
        if (openInterest.LatestOpenInterestValue is { } latestOpenInterest)
        {
            liquidations.SetOpenInterestValue(latestOpenInterest);
        }

        var latestOpenInterestTimestamp = LatestOpenInterestTimestamp(openInterestHistory);
        var initialTimestamp = latestOpenInterestTimestamp ?? DateTimeOffset.UtcNow;

        yield return CreateSample(
            initialTimestamp,
            liquidations,
            openInterest,
            liquidationStatus: "Waiting for liquidations",
            openInterestStatus: openInterestHistory.Count > 0 ? "Ready" : "No OI history");

        await using var liquidationEnumerator = _source
            .ReadLiquidationsAsync(_symbol, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        await using var refreshEnumerator = _openInterestRefreshInterval > TimeSpan.Zero
            ? _refreshClock.TicksAsync(_openInterestRefreshInterval, cancellationToken).GetAsyncEnumerator(cancellationToken)
            : null;

        var liquidationMove = liquidationEnumerator.MoveNextAsync().AsTask();
        var refreshMove = refreshEnumerator?.MoveNextAsync().AsTask();

        while (liquidationMove is not null || refreshMove is not null)
        {
            var completed = await Task.WhenAny(
                new[] { liquidationMove, refreshMove }
                    .Where(task => task is not null)
                    .Select(task => task!)
                    .ToArray());

            if (completed == liquidationMove)
            {
                if (!await liquidationMove)
                {
                    liquidationMove = null;
                    continue;
                }

                var liquidation = liquidationEnumerator.Current;
                liquidations.Add(liquidation);

                yield return CreateSample(
                    liquidation.TradeTime,
                    liquidations,
                    openInterest,
                    liquidationStatus: "Ready",
                    openInterestStatus: latestOpenInterestTimestamp is not null ? "Ready" : "No OI history");

                liquidationMove = liquidationEnumerator.MoveNextAsync().AsTask();
                continue;
            }

            if (refreshEnumerator is null || refreshMove is null || !await refreshMove)
            {
                refreshMove = null;
                continue;
            }

            var refreshedHistory = await LoadOpenInterestHistoryAsync(cancellationToken);
            var refreshedTimestamp = LatestOpenInterestTimestamp(refreshedHistory);
            if (refreshedTimestamp is null || refreshedTimestamp <= latestOpenInterestTimestamp)
            {
                refreshMove = refreshEnumerator.MoveNextAsync().AsTask();
                continue;
            }

            latestOpenInterestTimestamp = refreshedTimestamp;
            openInterest.LoadHistory(refreshedHistory);
            if (openInterest.LatestOpenInterestValue is { } refreshedOpenInterest)
            {
                liquidations.SetOpenInterestValue(refreshedOpenInterest);
            }

            yield return CreateSample(
                refreshedTimestamp.Value,
                liquidations,
                openInterest,
                liquidationStatus: "Waiting for liquidations",
                openInterestStatus: "Ready");

            refreshMove = refreshEnumerator.MoveNextAsync().AsTask();
        }
    }

    private Task<IReadOnlyList<OpenInterestPoint>> LoadOpenInterestHistoryAsync(CancellationToken cancellationToken)
    {
        return _source.GetOpenInterestHistoryAsync(
            _symbol,
            _frame,
            _openInterestHistoryLimit,
            cancellationToken);
    }

    private static DateTimeOffset? LatestOpenInterestTimestamp(IReadOnlyList<OpenInterestPoint> history)
    {
        return history.Count > 0
            ? history.Max(point => point.Timestamp)
            : null;
    }

    private ContextModuleSample CreateSample(
        DateTimeOffset timestamp,
        LiquidationContextCalculator liquidations,
        OpenInterestContextCalculator openInterest,
        string liquidationStatus,
        string openInterestStatus)
    {
        return new ContextModuleSample(
            _symbol,
            _frame,
            timestamp,
            liquidations.Snapshot(timestamp),
            openInterest.Snapshot(timestamp),
            liquidationStatus,
            openInterestStatus);
    }
}
