using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Application.Context;
using CryptoIndicatorApp.Application.Sessions;
using CryptoIndicatorApp.Domain.Context;
using CryptoIndicatorApp.Domain.Indicators;
using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Infrastructure.Binance;
using CryptoIndicatorApp.Infrastructure.Jsonl;
using CryptoIndicatorApp.LiveDryRun;
using System.Globalization;

var options = DryRunOptions.Parse(args);
var store = new JsonlMarketEventStore();

if (options.ContextOnly)
{
    await RunContextSmokeAsync(options);
    return;
}

if (options.ReplayOnly)
{
    if (!File.Exists(options.InputPath))
    {
        throw new InvalidOperationException($"Input file does not exist: {options.InputPath}");
    }

    Console.WriteLine("Mode: ReplayOnly");
    Console.WriteLine($"Symbol: {options.Symbol}");
    Console.WriteLine($"InputPath: {options.InputPath}");

    var replayOnlyResult = await ReplayAsync(store, options.Symbol, options.InputPath);
    Console.WriteLine($"RecordedEvents: {replayOnlyResult.RecordedEventCount}");
    Console.WriteLine($"ReplaySamples: {replayOnlyResult.SampleCount}");
    Console.WriteLine($"LastReplayZOfi: {FormatDecimal(replayOnlyResult.LastSample?.ZOfi ?? 0m)}");
    Console.WriteLine($"LastReplayTfi: {FormatDecimal(replayOnlyResult.LastSample?.Tfi ?? 0m)}");
    Console.WriteLine($"LastReplaySignal: {replayOnlyResult.LastSample?.Signal}");
    Console.WriteLine($"LastReplayBookSynced: {replayOnlyResult.LastSample?.BookHealth.IsSynced}");
    Console.WriteLine($"LastReplayResyncCount: {replayOnlyResult.LastSample?.BookHealth.ResyncCount}");
    Console.WriteLine($"LastReplayTimestampUtc: {replayOnlyResult.LastSample?.Timestamp.UtcDateTime:o}");
    WriteReplaySummary(replayOnlyResult.Summary);
    return;
}

if (File.Exists(options.OutputPath))
{
    throw new InvalidOperationException($"Output file already exists: {options.OutputPath}");
}

Console.WriteLine($"Symbol: {options.Symbol}");
Console.WriteLine($"DurationSeconds: {options.Duration.TotalSeconds:0}");
Console.WriteLine($"OutputPath: {options.OutputPath}");
Console.WriteLine($"ProxyEnabled: {options.Proxy.Enabled}");

using var client = new BinanceNetUsdFuturesMarketDataClient(new BinanceConnectionOptions
{
    Proxy = options.Proxy
});

var liveSource = new LiveSourceAdapter(
    new BinanceUsdFuturesLiveMarketEventSource(options.Symbol, client));
var recorder = new JsonlRecorderAdapter(store, options.OutputPath);
var liveSession = new LiveIndicatorSession(options.Symbol, liveSource, recorder);

var liveSampleCount = 0;
IndicatorSample? lastLiveSample = null;

using (var cancellation = new CancellationTokenSource(options.Duration))
{
    try
    {
        await foreach (var sample in liveSession.RunAsync(cancellation.Token))
        {
            liveSampleCount++;
            lastLiveSample = sample;
        }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
    }
}

var replayResult = await ReplayAsync(store, options.Symbol, options.OutputPath);

Console.WriteLine($"RecordedEvents: {replayResult.RecordedEventCount}");
Console.WriteLine($"LiveSamples: {liveSampleCount}");
Console.WriteLine($"ReplaySamples: {replayResult.SampleCount}");
Console.WriteLine($"ReplaySampleCountMatchesLive: {replayResult.SampleCount == liveSampleCount}");
Console.WriteLine($"LastReplayZOfi: {FormatDecimal(replayResult.LastSample?.ZOfi ?? 0m)}");
Console.WriteLine($"LastReplayTfi: {FormatDecimal(replayResult.LastSample?.Tfi ?? 0m)}");
Console.WriteLine($"LastReplaySignal: {replayResult.LastSample?.Signal}");
Console.WriteLine($"LastReplayBookSynced: {replayResult.LastSample?.BookHealth.IsSynced}");
Console.WriteLine($"LastReplayResyncCount: {replayResult.LastSample?.BookHealth.ResyncCount}");
Console.WriteLine($"LastLiveTimestampUtc: {lastLiveSample?.Timestamp.UtcDateTime:o}");
Console.WriteLine($"LastReplayTimestampUtc: {replayResult.LastSample?.Timestamp.UtcDateTime:o}");
WriteReplaySummary(replayResult.Summary);

static async Task<ReplayRunResult> ReplayAsync(JsonlMarketEventStore store, string symbol, string path)
{
    var recordedEventCount = File.Exists(path)
        ? File.ReadLines(path).Count()
        : 0;

    if (recordedEventCount == 0)
    {
        throw new InvalidOperationException("Replay input contains zero JSONL events.");
    }

    var replaySource = new JsonlSourceAdapter(store, path);
    var replaySession = new ReplayIndicatorSession(symbol, replaySource);
    var replaySampleCount = 0;
    IndicatorSample? lastReplaySample = null;
    var replaySummaryCollector = new IndicatorSampleSummaryCollector();

    await foreach (var sample in replaySession.RunAsync(CancellationToken.None))
    {
        replaySampleCount++;
        lastReplaySample = sample;
        replaySummaryCollector.Add(sample);
    }

    if (replaySampleCount == 0)
    {
        throw new InvalidOperationException("Replay completed but produced zero indicator samples.");
    }

    return new ReplayRunResult(
        RecordedEventCount: recordedEventCount,
        SampleCount: replaySampleCount,
        LastSample: lastReplaySample,
        Summary: replaySummaryCollector.Build());
}

static async Task RunContextSmokeAsync(DryRunOptions options)
{
    Console.WriteLine("Mode: ContextOnly");
    Console.WriteLine($"Symbol: {options.Symbol}");
    Console.WriteLine($"Frame: {options.ContextFrame}");
    Console.WriteLine($"DurationSeconds: {options.Duration.TotalSeconds:0}");
    Console.WriteLine($"OpenInterestHistoryLimit: {options.OpenInterestHistoryLimit}");
    Console.WriteLine($"ProxyEnabled: {options.Proxy.Enabled}");

    using var client = new BinanceNetUsdFuturesMarketDataClient(new BinanceConnectionOptions
    {
        Proxy = options.Proxy
    });
    var source = new ContextSmokeDataSource(client);
    var session = new ContextModuleSession(
        options.Symbol,
        options.ContextFrame,
        source,
        visibleDuration: TimeSpan.FromMinutes(150),
        normalizationHistory: TimeSpan.FromHours(24),
        minimumNormalizationSamples: 12,
        normalizationFloor: 0.00000001m,
        openInterestHistoryLimit: options.OpenInterestHistoryLimit);

    var sampleCount = 0;
    ContextModuleSample? lastSample = null;
    using var cancellation = new CancellationTokenSource(options.Duration);

    try
    {
        await foreach (var sample in session.RunAsync(cancellation.Token))
        {
            sampleCount++;
            lastSample = sample;
        }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
    }

    if (lastSample is null)
    {
        throw new InvalidOperationException("Context smoke produced zero samples.");
    }

    Console.WriteLine($"ContextSamples: {sampleCount}");
    Console.WriteLine($"OpenInterestTiles: {lastSample.OpenInterestTiles.Count}");
    Console.WriteLine($"OpenInterestNonZeroTiles: {lastSample.OpenInterestTiles.Count(tile => tile.RawDelta != 0m)}");
    Console.WriteLine($"LiquidationTiles: {lastSample.LiquidationTiles.Count}");
    Console.WriteLine($"LiquidationNonZeroTiles: {lastSample.LiquidationTiles.Count(tile => tile.RawDelta != 0m)}");
    Console.WriteLine($"LiquidationSubscriptionConnected: {source.LiquidationSubscriptionConnected}");
    Console.WriteLine($"LiquidationStatus: {lastSample.LiquidationStatus}");
    Console.WriteLine($"OpenInterestStatus: {lastSample.OpenInterestStatus}");
    Console.WriteLine($"LastContextTimestampUtc: {lastSample.Timestamp.UtcDateTime:o}");
}

static void WriteReplaySummary(IndicatorSampleSummary replaySummary)
{
    Console.WriteLine($"ReplayMinZOfi: {FormatDecimal(replaySummary.MinZOfi)}");
    Console.WriteLine($"ReplayMaxZOfi: {FormatDecimal(replaySummary.MaxZOfi)}");
    Console.WriteLine($"ReplayMaxAbsZOfi: {FormatDecimal(replaySummary.MaxAbsZOfi)}");
    Console.WriteLine($"ReplayMinTfi: {FormatDecimal(replaySummary.MinTfi)}");
    Console.WriteLine($"ReplayMaxTfi: {FormatDecimal(replaySummary.MaxTfi)}");
    Console.WriteLine($"ReplayAverageAbsTfi: {FormatDecimal(replaySummary.AverageAbsTfi)}");
    Console.WriteLine($"ReplayLongCandidates: {replaySummary.LongCandidateCount}");
    Console.WriteLine($"ReplayShortCandidates: {replaySummary.ShortCandidateCount}");
    Console.WriteLine($"ReplayNeutralSamples: {replaySummary.NeutralCount}");
    Console.WriteLine($"ReplayUnsyncedSamples: {replaySummary.UnsyncedSampleCount}");
    Console.WriteLine($"ReplayMaxResyncCount: {replaySummary.MaxResyncCount}");
    Console.WriteLine($"ReplayLatencyP50Ms: {FormatDouble(replaySummary.LatencyP50Ms)}");
    Console.WriteLine($"ReplayLatencyP95Ms: {FormatDouble(replaySummary.LatencyP95Ms)}");
    Console.WriteLine($"ReplayLatencyP99Ms: {FormatDouble(replaySummary.LatencyP99Ms)}");
}

static string FormatDecimal(decimal value)
{
    return value.ToString("0.########", CultureInfo.InvariantCulture);
}

static string FormatDouble(double value)
{
    return value.ToString("0.###", CultureInfo.InvariantCulture);
}

sealed class LiveSourceAdapter(BinanceUsdFuturesLiveMarketEventSource source) : IMarketEventSource
{
    public IAsyncEnumerable<IMarketEvent> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return source.ReadAllAsync(cancellationToken);
    }
}

sealed class JsonlRecorderAdapter(JsonlMarketEventStore store, string path) : IMarketEventRecorder
{
    public ValueTask AppendAsync(IMarketEvent marketEvent, CancellationToken cancellationToken = default)
    {
        return new ValueTask(store.AppendAsync(path, marketEvent, cancellationToken));
    }
}

sealed class JsonlSourceAdapter(JsonlMarketEventStore store, string path) : IMarketEventSource
{
    public IAsyncEnumerable<IMarketEvent> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return store.ReadAsync(path, cancellationToken);
    }
}

sealed class ContextSmokeDataSource(BinanceNetUsdFuturesMarketDataClient client) : IContextDataSource
{
    public bool LiquidationSubscriptionConnected { get; private set; }

    public Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
        string symbol,
        ContextFrame frame,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return client.GetOpenInterestHistoryAsync(symbol, frame, limit, cancellationToken);
    }

    public async IAsyncEnumerable<LiquidationEvent> ReadLiquidationsAsync(
        string symbol,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<LiquidationEvent>();
        await using var lease = await client.SubscribeLiquidationsAsync(
            symbol,
            item => channel.Writer.TryWrite(item),
            cancellationToken);
        LiquidationSubscriptionConnected = true;

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
}

sealed record ReplayRunResult(
    int RecordedEventCount,
    int SampleCount,
    IndicatorSample? LastSample,
    IndicatorSampleSummary Summary);
