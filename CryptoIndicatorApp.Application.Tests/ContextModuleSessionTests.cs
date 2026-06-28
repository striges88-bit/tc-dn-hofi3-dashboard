using System.Runtime.CompilerServices;
using CryptoIndicatorApp.Application.Context;
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Application.Tests;

public sealed class ContextModuleSessionTests
{
    [Fact]
    public async Task Context_session_bootstraps_oi_history_before_liquidation_tiles()
    {
        var start = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        var source = new FakeContextDataSource(
            new[]
            {
                new OpenInterestPoint("XANUSDT", 1m, 100_000m, start, start),
                new OpenInterestPoint("XANUSDT", 1m, 101_000m, start.AddMinutes(5), start.AddMinutes(5))
            },
            new[]
            {
                new LiquidationEvent("XANUSDT", "BUY", 10m, 10m, start.AddMinutes(6), start.AddMinutes(6), start.AddMinutes(6))
            });

        var session = new ContextModuleSession(
            "XANUSDT",
            ContextFrame.FiveMinutes,
            source,
            visibleDuration: TimeSpan.FromMinutes(15),
            normalizationHistory: TimeSpan.FromHours(24),
            minimumNormalizationSamples: 1,
            normalizationFloor: 0.00000001m);

        var samples = await TakeAsync(session.RunAsync(CancellationToken.None), count: 2);

        Assert.True(source.OpenInterestHistoryLoadedBeforeLiquidations);
        Assert.Contains(samples.Last().LiquidationTiles, tile => tile.RawDelta > 0m);
        Assert.Contains(samples.Last().OpenInterestTiles, tile => tile.RawDelta == 1_000m);
    }

    [Fact]
    public async Task Context_session_refreshes_oi_history_without_liquidation_events()
    {
        var start = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        var source = new FakeContextDataSource(
            new[]
            {
                new[]
                {
                    new OpenInterestPoint("XANUSDT", 1m, 100_000m, start, start),
                    new OpenInterestPoint("XANUSDT", 1m, 101_000m, start.AddMinutes(5), start.AddMinutes(5))
                },
                new[]
                {
                    new OpenInterestPoint("XANUSDT", 1m, 100_000m, start, start),
                    new OpenInterestPoint("XANUSDT", 1m, 101_000m, start.AddMinutes(5), start.AddMinutes(5)),
                    new OpenInterestPoint("XANUSDT", 1m, 103_000m, start.AddMinutes(10), start.AddMinutes(10))
                }
            },
            Array.Empty<LiquidationEvent>());
        var refreshClock = new FakeContextRefreshClock(start.AddMinutes(10));

        var session = new ContextModuleSession(
            "XANUSDT",
            ContextFrame.FiveMinutes,
            source,
            visibleDuration: TimeSpan.FromMinutes(15),
            normalizationHistory: TimeSpan.FromHours(24),
            minimumNormalizationSamples: 1,
            normalizationFloor: 0.00000001m,
            openInterestHistoryLimit: 288,
            openInterestRefreshInterval: TimeSpan.FromSeconds(60),
            refreshClock: refreshClock);

        var samples = await TakeAsync(session.RunAsync(CancellationToken.None), count: 2);

        Assert.Equal(2, source.OpenInterestHistoryRequestCount);
        Assert.Equal(TimeSpan.FromSeconds(60), refreshClock.ObservedInterval);
        Assert.Contains(samples.Last().OpenInterestTiles, tile => tile.RawDelta == 2_000m);
        Assert.Equal("Ready", samples.Last().OpenInterestStatus);
        Assert.Equal("Waiting for liquidations", samples.Last().LiquidationStatus);
    }

    [Fact]
    public async Task Context_session_does_not_emit_refresh_sample_for_same_oi_timestamp()
    {
        var start = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        var history = new[]
        {
            new OpenInterestPoint("XANUSDT", 1m, 100_000m, start, start),
            new OpenInterestPoint("XANUSDT", 1m, 101_000m, start.AddMinutes(5), start.AddMinutes(5))
        };
        var source = new FakeContextDataSource(
            new[] { history, history },
            Array.Empty<LiquidationEvent>());
        var refreshClock = new FakeContextRefreshClock(completeAfterTicks: true, start.AddMinutes(6));

        var session = new ContextModuleSession(
            "XANUSDT",
            ContextFrame.FiveMinutes,
            source,
            visibleDuration: TimeSpan.FromMinutes(15),
            normalizationHistory: TimeSpan.FromHours(24),
            minimumNormalizationSamples: 1,
            normalizationFloor: 0.00000001m,
            openInterestHistoryLimit: 288,
            openInterestRefreshInterval: TimeSpan.FromSeconds(60),
            refreshClock: refreshClock);

        var samples = await CollectAsync(session.RunAsync(CancellationToken.None));

        Assert.Equal(2, source.OpenInterestHistoryRequestCount);
        Assert.Single(samples);
    }

    private static async Task<IReadOnlyList<T>> CollectAsync<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
    {
        var result = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            result.Add(item);
        }

        return result;
    }

    private static async Task<IReadOnlyList<T>> TakeAsync<T>(
        IAsyncEnumerable<T> source,
        int count,
        CancellationToken cancellationToken = default)
    {
        var result = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            result.Add(item);
            if (result.Count >= count)
            {
                break;
            }
        }

        return result;
    }

    private sealed class FakeContextDataSource : IContextDataSource
    {
        private readonly IReadOnlyList<IReadOnlyList<OpenInterestPoint>> _openInterestHistories;
        private readonly IReadOnlyList<LiquidationEvent> _liquidationEvents;
        private bool _liquidationsStarted;

        public FakeContextDataSource(
            IReadOnlyList<OpenInterestPoint> openInterestHistory,
            IReadOnlyList<LiquidationEvent> liquidationEvents)
            : this(new[] { openInterestHistory }, liquidationEvents)
        {
        }

        public FakeContextDataSource(
            IReadOnlyList<IReadOnlyList<OpenInterestPoint>> openInterestHistories,
            IReadOnlyList<LiquidationEvent> liquidationEvents)
        {
            _openInterestHistories = openInterestHistories;
            _liquidationEvents = liquidationEvents;
        }

        public bool OpenInterestHistoryLoadedBeforeLiquidations { get; private set; }

        public int OpenInterestHistoryRequestCount { get; private set; }

        public Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
            string symbol,
            ContextFrame frame,
            int limit,
            CancellationToken cancellationToken = default)
        {
            OpenInterestHistoryLoadedBeforeLiquidations = !_liquidationsStarted;
            var index = Math.Min(OpenInterestHistoryRequestCount, _openInterestHistories.Count - 1);
            OpenInterestHistoryRequestCount++;
            return Task.FromResult(_openInterestHistories[index]);
        }

        public async IAsyncEnumerable<LiquidationEvent> ReadLiquidationsAsync(
            string symbol,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _liquidationsStarted = true;
            foreach (var liquidationEvent in _liquidationEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return liquidationEvent;
            }
        }
    }

    private sealed class FakeContextRefreshClock : IContextRefreshClock
    {
        private readonly IReadOnlyList<DateTimeOffset> _ticks;
        private readonly bool _completeAfterTicks;

        public FakeContextRefreshClock(params DateTimeOffset[] ticks)
            : this(completeAfterTicks: false, ticks)
        {
        }

        public FakeContextRefreshClock(bool completeAfterTicks, params DateTimeOffset[] ticks)
        {
            _ticks = ticks;
            _completeAfterTicks = completeAfterTicks;
        }

        public TimeSpan? ObservedInterval { get; private set; }

        public async IAsyncEnumerable<DateTimeOffset> TicksAsync(
            TimeSpan interval,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ObservedInterval = interval;
            foreach (var tick in _ticks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return tick;
            }

            if (!_completeAfterTicks)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }
    }
}
