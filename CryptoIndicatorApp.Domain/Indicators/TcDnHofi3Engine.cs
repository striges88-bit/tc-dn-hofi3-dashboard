using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Domain.OrderBooks;
using CryptoIndicatorApp.Domain.Statistics;

namespace CryptoIndicatorApp.Domain.Indicators;

public sealed class TcDnHofi3Engine
{
    private readonly Queue<(DateTimeOffset Timestamp, decimal Hofi)> _hofiWindow = new();
    private readonly Queue<(DateTimeOffset Timestamp, decimal Hofi)> _stabilityHofiWindow = new();
    private readonly Queue<(DateTimeOffset Timestamp, decimal Depth)> _depthHistory = new();
    private readonly Queue<(DateTimeOffset Timestamp, decimal Nofi)> _nofiHistory = new();
    private readonly Queue<(DateTimeOffset Timestamp, decimal Nofi)> _stableNofiHistory = new();
    private readonly Queue<decimal> _recentFastZOfi = new();
    private readonly RollingTradeFlow _tradeFlow;
    private readonly LocalOrderBook _book;
    private readonly IndicatorParameters _parameters;
    private readonly decimal[] _weights;
    private IReadOnlyList<BookLevel> _previousBids = Array.Empty<BookLevel>();
    private IReadOnlyList<BookLevel> _previousAsks = Array.Empty<BookLevel>();
    private DateTimeOffset? _nextEmitTime;

    public TcDnHofi3Engine(string symbol, IndicatorParameters? parameters = null)
    {
        Symbol = symbol;
        _parameters = parameters ?? new IndicatorParameters();
        _book = new LocalOrderBook(symbol);
        _tradeFlow = new RollingTradeFlow(TimeSpan.FromMilliseconds(_parameters.OfiWindowMilliseconds));
        _weights = OfiCalculator.CalculateWeights(_parameters.TopLevels, _parameters.Lambda);
    }

    public string Symbol { get; }

    public IndicatorSample? Process(IMarketEvent marketEvent)
    {
        return marketEvent switch
        {
            DepthSnapshotEvent snapshot => ProcessSnapshot(snapshot),
            DepthUpdateEvent update => ProcessDepthUpdate(update),
            AggTradeEvent trade => ProcessTrade(trade),
            _ => throw new ArgumentOutOfRangeException(nameof(marketEvent), "Unsupported market event type.")
        };
    }

    private IndicatorSample? ProcessSnapshot(DepthSnapshotEvent snapshot)
    {
        _book.ApplySnapshot(snapshot);
        _previousBids = _book.GetTopBids(_parameters.TopLevels);
        _previousAsks = _book.GetTopAsks(_parameters.TopLevels);
        return null;
    }

    private IndicatorSample? ProcessTrade(AggTradeEvent trade)
    {
        _tradeFlow.Add(trade);
        return TryCreateSample(trade.ExchangeTime, trade.ReceiveTime);
    }

    private IndicatorSample? ProcessDepthUpdate(DepthUpdateEvent update)
    {
        var hadPreviousLevels = _previousBids.Count >= _parameters.TopLevels
            && _previousAsks.Count >= _parameters.TopLevels;

        _book.ApplyDepthUpdate(update);
        var currentBids = _book.GetTopBids(_parameters.TopLevels);
        var currentAsks = _book.GetTopAsks(_parameters.TopLevels);

        if (_book.Health.IsSynced && hadPreviousLevels && currentBids.Count >= _parameters.TopLevels && currentAsks.Count >= _parameters.TopLevels)
        {
            var levelOfis = new decimal[_parameters.TopLevels];
            for (var index = 0; index < _parameters.TopLevels; index++)
            {
                levelOfis[index] = OfiCalculator.CalculateLevelOfi(
                    _previousBids[index],
                    _previousAsks[index],
                    currentBids[index],
                    currentAsks[index]);
            }

            var weightedHofi = OfiCalculator.CalculateWeightedHofi(levelOfis, _parameters.Lambda);
            _hofiWindow.Enqueue((update.ExchangeTime, weightedHofi));
            _stabilityHofiWindow.Enqueue((update.ExchangeTime, weightedHofi));
            _depthHistory.Enqueue((update.ExchangeTime, CalculateWeightedDepth(currentBids, currentAsks)));
        }

        _previousBids = currentBids;
        _previousAsks = currentAsks;
        return TryCreateSample(update.ExchangeTime, update.ReceiveTime);
    }

    private IndicatorSample? TryCreateSample(DateTimeOffset exchangeTime, DateTimeOffset receiveTime)
    {
        if (!_book.Health.IsSynced)
        {
            return null;
        }

        if (_nextEmitTime is not null && exchangeTime < _nextEmitTime.Value)
        {
            return null;
        }

        _nextEmitTime = exchangeTime.AddMilliseconds(100);
        TrimWindows(exchangeTime);

        var hofi = _hofiWindow.Sum(item => item.Hofi);
        var stableHofi = _stabilityHofiWindow.Sum(item => item.Hofi);
        var depthReference = CalculateDepthReference();
        var nofi = depthReference == 0m ? 0m : hofi / depthReference;
        var stableNofi = depthReference == 0m ? 0m : stableHofi / depthReference;
        var zOfi = CalculateZOfi(nofi);
        var stableZOfi = CalculateStableZOfi(stableNofi);
        var tfi = _tradeFlow.Calculate(exchangeTime);
        var signal = CalculateSignal(zOfi, stableZOfi, tfi);

        _nofiHistory.Enqueue((exchangeTime, nofi));
        _stableNofiHistory.Enqueue((exchangeTime, stableNofi));
        RememberFastZOfi(zOfi);

        return new IndicatorSample(
            Timestamp: exchangeTime,
            Hofi: hofi,
            Nofi: nofi,
            ZOfi: zOfi,
            Tfi: tfi,
            Signal: signal,
            BookHealth: _book.Health,
            ExchangeToReceiveLatency: receiveTime - exchangeTime);
    }

    private void TrimWindows(DateTimeOffset timestamp)
    {
        Trim(_hofiWindow, timestamp - TimeSpan.FromMilliseconds(_parameters.OfiWindowMilliseconds));
        Trim(_stabilityHofiWindow, timestamp - TimeSpan.FromMilliseconds(_parameters.StabilityWindowMilliseconds));
        Trim(_depthHistory, timestamp - TimeSpan.FromSeconds(_parameters.DepthReferenceSeconds));
        Trim(_nofiHistory, timestamp - TimeSpan.FromSeconds(_parameters.ZScoreWindowSeconds));
        Trim(_stableNofiHistory, timestamp - TimeSpan.FromSeconds(_parameters.ZScoreWindowSeconds));
    }

    private static void Trim(Queue<(DateTimeOffset Timestamp, decimal Value)> values, DateTimeOffset cutoff)
    {
        while (values.Count > 0 && values.Peek().Timestamp < cutoff)
        {
            values.Dequeue();
        }
    }

    private decimal CalculateWeightedDepth(IReadOnlyList<BookLevel> bids, IReadOnlyList<BookLevel> asks)
    {
        var depth = 0m;
        for (var index = 0; index < _parameters.TopLevels; index++)
        {
            depth += _weights[index] * ((bids[index].Price * bids[index].Quantity) + (asks[index].Price * asks[index].Quantity));
        }

        return depth;
    }

    private decimal CalculateDepthReference()
    {
        return _depthHistory.Count == 0
            ? 0m
            : RobustStats.Median(_depthHistory.Select(item => item.Depth));
    }

    private decimal CalculateZOfi(decimal nofi)
    {
        var history = _nofiHistory.Select(item => item.Nofi).ToArray();
        if (history.Length < _parameters.MinimumZScoreSamples)
        {
            return 0m;
        }

        return RobustStats.RobustZ(nofi, history, _parameters.NofiMadFloor);
    }

    private decimal CalculateStableZOfi(decimal stableNofi)
    {
        var history = _stableNofiHistory.Select(item => item.Nofi).ToArray();
        if (history.Length < _parameters.MinimumZScoreSamples)
        {
            return 0m;
        }

        return RobustStats.RobustZ(stableNofi, history, _parameters.NofiMadFloor);
    }

    private SignalState CalculateSignal(decimal zOfi, decimal stableZOfi, decimal tfi)
    {
        if (!_book.Health.IsSynced)
        {
            return SignalState.Neutral;
        }

        var longStable = stableZOfi >= _parameters.ThetaStable || HasRecentFastZOfiDirection(zOfi, 1);
        var shortStable = stableZOfi <= -_parameters.ThetaStable || HasRecentFastZOfiDirection(zOfi, -1);

        if (zOfi >= _parameters.ThetaZ && longStable && tfi >= _parameters.ThetaTfi)
        {
            return SignalState.LongCandidate;
        }

        if (zOfi <= -_parameters.ThetaZ && shortStable && tfi <= -_parameters.ThetaTfi)
        {
            return SignalState.ShortCandidate;
        }

        return SignalState.Neutral;
    }

    private bool HasRecentFastZOfiDirection(decimal currentZOfi, int direction)
    {
        var signedCount = _recentFastZOfi
            .Concat(new[] { currentZOfi })
            .TakeLast(3)
            .Count(value => direction > 0 ? value > 0m : value < 0m);

        return signedCount >= 2;
    }

    private void RememberFastZOfi(decimal zOfi)
    {
        _recentFastZOfi.Enqueue(zOfi);
        while (_recentFastZOfi.Count > 2)
        {
            _recentFastZOfi.Dequeue();
        }
    }
}
