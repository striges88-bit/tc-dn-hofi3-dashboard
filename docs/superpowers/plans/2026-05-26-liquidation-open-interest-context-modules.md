# Liquidation And Open Interest Context Modules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add separate Binance USDS-M liquidation and open-interest context modules with 5m/15m bucket deltas, robust per-symbol normalization, and gradient tile display, without changing TC-DN-HOFI3 HOFI/TFI formula or signal logic.

**Architecture:** Domain owns context events, bucket math, and normalization. Application owns context source interfaces and session orchestration. Infrastructure maps Binance.Net REST/WebSocket DTOs to project-owned context models. Desktop composes the sources and renders compact context strips.

**Tech Stack:** C#/.NET 8, WPF, Binance.Net 12.12.0, xUnit, existing layered solution.

---

## Sources And Constraints

- Binance liquidation stream: `<symbol>@forceOrder`, update speed 1000 ms, only largest liquidation order snapshot per symbol per interval.
- Binance OI current endpoint: `/fapi/v1/openInterest`.
- Binance OI history endpoint: `/futures/data/openInterestHist`, periods include `5m` and `15m`, latest one month available.
- Binance.Net methods verified locally:
  - `UsdFuturesApi.ExchangeData.SubscribeToLiquidationUpdatesAsync(...)`
  - `UsdFuturesApi.ExchangeData.GetOpenInterestAsync(...)`
  - `UsdFuturesApi.ExchangeData.GetOpenInterestHistoryAsync(...)`
  - `PeriodInterval.FiveMinutes`, `PeriodInterval.FifteenMinutes`
- These modules are context only. Do not feed liquidation/OI into `TcDnHofi3Engine`, `IndicatorSample`, `SignalState`, or HOFI/TFI thresholds.
- No trading, order placement, multi-symbol scanning, or per-symbol optimization UI.
- The workspace is not a git repository. Skip commit steps unless git is initialized later.

## Normalization Design

Use signed delta for direction and robust normalized absolute magnitude for intensity:

```text
direction = sign(rawDelta)
strengthInput = abs(normalizedDelta)
robustScale = median(abs(history)) + 1.4826 * MAD(abs(history)) + floor
strength = strengthInput / robustScale
intensity = clamp(strength / 3, 0, 1)
```

Reason: signed z-score can invert color semantics. Example: OI grows slightly, but less than its historical median growth; signed z may be negative even though the delta is positive. The UI requirement is green for rising OI and red for falling OI, with brightness showing strength. Therefore sign and intensity must be separated.

Liquidation normalized delta:

```text
buyLiqNotional = sum(avgPrice * filledQty) for forceOrder side BUY
sellLiqNotional = sum(avgPrice * filledQty) for forceOrder side SELL
liqNet = buyLiqNotional - sellLiqNotional
liqRate = liqNet / latestOpenInterestValue
```

Open-interest normalized delta:

```text
oiDeltaValue = sumOpenInterestValue[t] - sumOpenInterestValue[t - 1]
oiDeltaPct = oiDeltaValue / sumOpenInterestValue[t - 1]
```

Default frame and history:

```text
ContextFrame = 15m default, 5m selectable
Visible duration = 150 minutes
15m tiles = 10
5m tiles = 30
Normalization history target = 24 hours
Minimum normalization buckets = 12
MAD floor = 0.00000001
```

If liquidation normalization has no OI denominator yet, emit a warm-up/unavailable state instead of bright color. This avoids false strength on newly started symbols.

---

## File Map

### Domain

- Create `CryptoIndicatorApp.Domain/Context/ContextFrame.cs`
  - `ContextFrame` enum and duration helpers.
- Create `CryptoIndicatorApp.Domain/Context/ContextDirection.cs`
  - `Positive`, `Negative`, `Neutral`, `Unavailable`.
- Create `CryptoIndicatorApp.Domain/Context/LiquidationEvent.cs`
  - Project-owned liquidation event.
- Create `CryptoIndicatorApp.Domain/Context/OpenInterestPoint.cs`
  - Project-owned OI history/current point.
- Create `CryptoIndicatorApp.Domain/Context/ContextTile.cs`
  - Output tile model for UI.
- Create `CryptoIndicatorApp.Domain/Context/RobustMagnitudeNormalizer.cs`
  - Per-symbol/frame robust strength normalization.
- Create `CryptoIndicatorApp.Domain/Context/LiquidationContextCalculator.cs`
  - Bucket liquidation events and emit signed normalized tiles.
- Create `CryptoIndicatorApp.Domain/Context/OpenInterestContextCalculator.cs`
  - Convert OI history points into delta tiles.

### Application

- Create `CryptoIndicatorApp.Application/Context/IContextDataSource.cs`
  - Application boundary for liquidation stream and OI history/current data.
- Create `CryptoIndicatorApp.Application/Context/ContextModuleSample.cs`
  - Combined sample for Desktop: liquidation tiles, OI tiles, statuses.
- Create `CryptoIndicatorApp.Application/Context/ContextModuleSession.cs`
  - Orchestrates one-symbol context module updates.
- Add `CryptoIndicatorApp.Application.Tests/ContextModuleSessionTests.cs`
  - Proves context orchestration without Infrastructure reference.

### Infrastructure

- Create `CryptoIndicatorApp.Infrastructure/Binance/BinanceContextEventMapper.cs`
  - Maps Binance liquidation/OI DTO data to Domain context models.
- Modify `CryptoIndicatorApp.Infrastructure/Binance/IBinanceUsdFuturesMarketDataClient.cs`
  - Add context methods.
- Modify `CryptoIndicatorApp.Infrastructure/Binance/BinanceNetUsdFuturesMarketDataClient.cs`
  - Implement liquidation subscription and OI REST methods.
- Add `CryptoIndicatorApp.Infrastructure.Tests/BinanceContextMapperTests.cs`
  - DTO-boundary and math tests.

### Desktop

- Modify `CryptoIndicatorApp.Desktop/Configuration/DashboardOptions.cs`
  - Add `ContextOptions`.
- Create `CryptoIndicatorApp.Desktop/Configuration/ContextOptions.cs`
  - Config-only context defaults.
- Modify `CryptoIndicatorApp.Desktop/appsettings.json`
  - Add context frame, visible minutes, history hours, polling interval.
- Create `CryptoIndicatorApp.Desktop/Composition/BinanceContextDataSource.cs`
  - Adapts Infrastructure client to Application `IContextDataSource`.
- Modify `CryptoIndicatorApp.Desktop/ViewModels/DashboardViewModel.cs`
  - Add frame selection, context tiles, and status strings.
- Create `CryptoIndicatorApp.Desktop/ViewModels/ContextTileViewModel.cs`
  - Brush/intensity/text projection for WPF.
- Modify `CryptoIndicatorApp.Desktop/MainWindow.xaml`
  - Add compact liquidation and OI strips.
- Modify `CryptoIndicatorApp.Desktop/MainWindow.xaml.cs`
  - Start/stop context session alongside live market session.
- Add/update Desktop tests for config, ViewModel, and composition.

---

## Task 1: Domain Context Models And Normalizer

**Files:**
- Create `CryptoIndicatorApp.Domain/Context/ContextFrame.cs`
- Create `CryptoIndicatorApp.Domain/Context/ContextDirection.cs`
- Create `CryptoIndicatorApp.Domain/Context/LiquidationEvent.cs`
- Create `CryptoIndicatorApp.Domain/Context/OpenInterestPoint.cs`
- Create `CryptoIndicatorApp.Domain/Context/ContextTile.cs`
- Create `CryptoIndicatorApp.Domain/Context/RobustMagnitudeNormalizer.cs`
- Test: `CryptoIndicatorApp.Domain.Tests/RobustMagnitudeNormalizerTests.cs`

- [ ] **Step 1: Write failing tests for frame durations and robust intensity**

Add tests:

```csharp
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Domain.Tests;

public sealed class RobustMagnitudeNormalizerTests
{
    [Fact]
    public void Context_frame_maps_to_expected_duration()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), ContextFrame.FiveMinutes.ToDuration());
        Assert.Equal(TimeSpan.FromMinutes(15), ContextFrame.FifteenMinutes.ToDuration());
    }

    [Fact]
    public void Normalizer_requires_minimum_history_before_emitting_intensity()
    {
        var normalizer = new RobustMagnitudeNormalizer(
            historyWindow: TimeSpan.FromHours(24),
            minimumSamples: 3,
            floor: 0.00000001m);

        var timestamp = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        normalizer.Add(timestamp, 0.001m);
        normalizer.Add(timestamp.AddMinutes(5), -0.002m);

        var result = normalizer.Normalize(timestamp.AddMinutes(10), 0.003m);

        Assert.False(result.IsReady);
        Assert.Equal(0d, result.Intensity);
    }

    [Fact]
    public void Normalizer_uses_absolute_magnitude_for_intensity_without_changing_direction()
    {
        var normalizer = new RobustMagnitudeNormalizer(
            historyWindow: TimeSpan.FromHours(24),
            minimumSamples: 3,
            floor: 0.00000001m);

        var timestamp = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        normalizer.Add(timestamp, 0.001m);
        normalizer.Add(timestamp.AddMinutes(5), -0.0015m);
        normalizer.Add(timestamp.AddMinutes(10), 0.002m);

        var result = normalizer.Normalize(timestamp.AddMinutes(15), -0.009m);

        Assert.True(result.IsReady);
        Assert.Equal(ContextDirection.Negative, ContextDirection.FromSignedValue(-0.009m));
        Assert.InRange(result.Intensity, 0.01d, 1d);
    }
}
```

- [ ] **Step 2: Run the failing tests**

Run:

```powershell
$env:DOTNET_CLI_HOME='C:\Users\Steven Owl\Desktop\PRJCT-INDIC\.dotnet-home'
$env:APPDATA='C:\Users\Steven Owl\Desktop\PRJCT-INDIC\.dotnet-home\AppData'
$env:NUGET_PACKAGES='C:\Users\Steven Owl\Desktop\PRJCT-INDIC\.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& '.dotnet\dotnet.exe' test CryptoIndicatorApp.Domain.Tests\CryptoIndicatorApp.Domain.Tests.csproj --no-restore
```

Expected: FAIL because `CryptoIndicatorApp.Domain.Context` types do not exist.

- [ ] **Step 3: Implement minimal Domain context models**

Create:

```csharp
namespace CryptoIndicatorApp.Domain.Context;

public enum ContextFrame
{
    FiveMinutes = 5,
    FifteenMinutes = 15
}

public static class ContextFrameExtensions
{
    public static TimeSpan ToDuration(this ContextFrame frame)
    {
        return frame switch
        {
            ContextFrame.FiveMinutes => TimeSpan.FromMinutes(5),
            ContextFrame.FifteenMinutes => TimeSpan.FromMinutes(15),
            _ => throw new ArgumentOutOfRangeException(nameof(frame), frame, "Unsupported context frame.")
        };
    }

    public static int VisibleTileCount(this ContextFrame frame, TimeSpan visibleDuration)
    {
        return Math.Max(1, (int)Math.Ceiling(visibleDuration.TotalMilliseconds / frame.ToDuration().TotalMilliseconds));
    }
}
```

```csharp
namespace CryptoIndicatorApp.Domain.Context;

public enum ContextDirection
{
    Unavailable,
    Neutral,
    Positive,
    Negative
}

public static class ContextDirectionExtensions
{
    public static ContextDirection FromSignedValue(decimal value)
    {
        if (value > 0m)
        {
            return ContextDirection.Positive;
        }

        if (value < 0m)
        {
            return ContextDirection.Negative;
        }

        return ContextDirection.Neutral;
    }
}
```

```csharp
namespace CryptoIndicatorApp.Domain.Context;

public sealed record LiquidationEvent(
    string Symbol,
    string Side,
    decimal AveragePrice,
    decimal QuantityFilled,
    DateTimeOffset TradeTime,
    DateTimeOffset ExchangeTime,
    DateTimeOffset ReceiveTime)
{
    public decimal Notional => AveragePrice * QuantityFilled;

    public decimal SignedNotional => string.Equals(Side, "BUY", StringComparison.OrdinalIgnoreCase)
        ? Notional
        : string.Equals(Side, "SELL", StringComparison.OrdinalIgnoreCase)
            ? -Notional
            : 0m;
}
```

```csharp
namespace CryptoIndicatorApp.Domain.Context;

public sealed record OpenInterestPoint(
    string Symbol,
    decimal SumOpenInterest,
    decimal SumOpenInterestValue,
    DateTimeOffset Timestamp,
    DateTimeOffset ReceiveTime);
```

```csharp
namespace CryptoIndicatorApp.Domain.Context;

public sealed record ContextTile(
    DateTimeOffset BucketStart,
    DateTimeOffset BucketEnd,
    decimal RawDelta,
    decimal? NormalizedDelta,
    ContextDirection Direction,
    double Intensity,
    bool IsReady,
    string Status);
```

```csharp
namespace CryptoIndicatorApp.Domain.Context;

public sealed record RobustMagnitudeResult(bool IsReady, double Intensity, decimal Scale);

public sealed class RobustMagnitudeNormalizer
{
    private readonly TimeSpan _historyWindow;
    private readonly int _minimumSamples;
    private readonly decimal _floor;
    private readonly List<(DateTimeOffset Timestamp, decimal Value)> _history = new();

    public RobustMagnitudeNormalizer(TimeSpan historyWindow, int minimumSamples, decimal floor)
    {
        _historyWindow = historyWindow <= TimeSpan.Zero ? TimeSpan.FromHours(24) : historyWindow;
        _minimumSamples = Math.Max(1, minimumSamples);
        _floor = floor <= 0m ? 0.00000001m : floor;
    }

    public void Add(DateTimeOffset timestamp, decimal signedValue)
    {
        _history.Add((timestamp, signedValue));
        Prune(timestamp);
    }

    public RobustMagnitudeResult Normalize(DateTimeOffset timestamp, decimal signedValue)
    {
        Prune(timestamp);
        if (_history.Count < _minimumSamples)
        {
            return new RobustMagnitudeResult(false, 0d, 0m);
        }

        var magnitudes = _history.Select(item => Math.Abs(item.Value)).Order().ToArray();
        var median = Median(magnitudes);
        var deviations = magnitudes.Select(value => Math.Abs(value - median)).Order().ToArray();
        var mad = Median(deviations);
        var scale = Math.Max(_floor, median + (1.4826m * mad));
        var strength = Math.Abs(signedValue) / scale;
        var intensity = Math.Clamp((double)(strength / 3m), 0d, 1d);

        return new RobustMagnitudeResult(true, intensity, scale);
    }

    private void Prune(DateTimeOffset now)
    {
        var cutoff = now - _historyWindow;
        _history.RemoveAll(item => item.Timestamp < cutoff);
    }

    private static decimal Median(IReadOnlyList<decimal> sorted)
    {
        if (sorted.Count == 0)
        {
            return 0m;
        }

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2m;
    }
}
```

- [ ] **Step 4: Run Domain tests**

Run the same Domain test command.

Expected: PASS for the new normalizer tests and existing Domain tests.

---

## Task 2: Domain Bucket Calculators

**Files:**
- Create `CryptoIndicatorApp.Domain/Context/LiquidationContextCalculator.cs`
- Create `CryptoIndicatorApp.Domain/Context/OpenInterestContextCalculator.cs`
- Test: `CryptoIndicatorApp.Domain.Tests/LiquidationContextCalculatorTests.cs`
- Test: `CryptoIndicatorApp.Domain.Tests/OpenInterestContextCalculatorTests.cs`

- [ ] **Step 1: Write failing tests for liquidation and OI bucket deltas**

Add tests:

```csharp
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Domain.Tests;

public sealed class LiquidationContextCalculatorTests
{
    [Fact]
    public void Liquidation_bucket_uses_buy_minus_sell_notional_and_oi_denominator()
    {
        var calculator = new LiquidationContextCalculator(
            ContextFrame.FiveMinutes,
            visibleDuration: TimeSpan.FromMinutes(15),
            normalizationHistory: TimeSpan.FromHours(24),
            minimumNormalizationSamples: 1,
            normalizationFloor: 0.00000001m);

        var start = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        calculator.SetOpenInterestValue(100_000m);
        calculator.Add(new LiquidationEvent("XANUSDT", "BUY", 10m, 20m, start.AddMinutes(1), start.AddMinutes(1), start.AddMinutes(1)));
        calculator.Add(new LiquidationEvent("XANUSDT", "SELL", 5m, 10m, start.AddMinutes(2), start.AddMinutes(2), start.AddMinutes(2)));

        var tiles = calculator.Snapshot(start.AddMinutes(5));

        var tile = Assert.Single(tiles.Where(item => item.RawDelta != 0m));
        Assert.Equal(150m, tile.RawDelta);
        Assert.Equal(0.0015m, tile.NormalizedDelta);
        Assert.Equal(ContextDirection.Positive, tile.Direction);
        Assert.True(tile.IsReady);
    }

    [Fact]
    public void Liquidation_tile_is_not_ready_without_open_interest_value()
    {
        var calculator = new LiquidationContextCalculator(
            ContextFrame.FiveMinutes,
            visibleDuration: TimeSpan.FromMinutes(15),
            normalizationHistory: TimeSpan.FromHours(24),
            minimumNormalizationSamples: 1,
            normalizationFloor: 0.00000001m);

        var start = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        calculator.Add(new LiquidationEvent("XANUSDT", "BUY", 10m, 20m, start, start, start));

        var tile = Assert.Single(calculator.Snapshot(start.AddMinutes(5)).Where(item => item.RawDelta != 0m));
        Assert.Null(tile.NormalizedDelta);
        Assert.False(tile.IsReady);
        Assert.Equal("Waiting for OI", tile.Status);
    }
}
```

```csharp
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Domain.Tests;

public sealed class OpenInterestContextCalculatorTests
{
    [Fact]
    public void Open_interest_tiles_use_value_delta_percentage()
    {
        var calculator = new OpenInterestContextCalculator(
            ContextFrame.FiveMinutes,
            visibleDuration: TimeSpan.FromMinutes(15),
            normalizationHistory: TimeSpan.FromHours(24),
            minimumNormalizationSamples: 1,
            normalizationFloor: 0.00000001m);

        var start = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        calculator.LoadHistory(new[]
        {
            new OpenInterestPoint("XANUSDT", 1000m, 100_000m, start, start),
            new OpenInterestPoint("XANUSDT", 1000m, 105_000m, start.AddMinutes(5), start.AddMinutes(5)),
            new OpenInterestPoint("XANUSDT", 1000m, 102_900m, start.AddMinutes(10), start.AddMinutes(10))
        });

        var tiles = calculator.Snapshot(start.AddMinutes(10));

        Assert.Contains(tiles, tile => tile.RawDelta == 5_000m
            && tile.NormalizedDelta == 0.05m
            && tile.Direction == ContextDirection.Positive);
        Assert.Contains(tiles, tile => tile.RawDelta == -2_100m
            && tile.NormalizedDelta == -0.02m
            && tile.Direction == ContextDirection.Negative);
    }
}
```

- [ ] **Step 2: Run Domain tests**

Expected: FAIL because calculators do not exist.

- [ ] **Step 3: Implement minimal calculators**

Implement bucket alignment and output sorting newest-last:

```csharp
private static DateTimeOffset BucketStart(DateTimeOffset timestamp, TimeSpan frame)
{
    var ticks = timestamp.UtcTicks / frame.Ticks * frame.Ticks;
    return new DateTimeOffset(ticks, TimeSpan.Zero);
}
```

`LiquidationContextCalculator` stores signed notional per bucket, latest OI value, and creates `ContextTile` values. `OpenInterestContextCalculator` sorts OI points by timestamp, creates one delta tile per adjacent point pair, and stores latest OI value for liquidation normalization.

Do not reference Binance.Net, WPF, or Application from these Domain calculators.

- [ ] **Step 4: Run Domain tests**

Expected: PASS for Domain tests.

---

## Task 3: Application Context Session Boundary

**Files:**
- Create `CryptoIndicatorApp.Application/Context/IContextDataSource.cs`
- Create `CryptoIndicatorApp.Application/Context/ContextModuleSample.cs`
- Create `CryptoIndicatorApp.Application/Context/ContextModuleSession.cs`
- Test: `CryptoIndicatorApp.Application.Tests/ContextModuleSessionTests.cs`
- Modify: `CryptoIndicatorApp.Application.Tests/ApplicationBoundaryTests.cs` if needed to keep Infrastructure boundary explicit.

- [ ] **Step 1: Write failing Application tests**

Add tests:

```csharp
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

        var samples = await session.RunAsync(CancellationToken.None).TakeAsync(2);

        Assert.Contains(samples.Last().LiquidationTiles, tile => tile.RawDelta > 0m);
        Assert.Contains(samples.Last().OpenInterestTiles, tile => tile.RawDelta == 1_000m);
    }

    private sealed class FakeContextDataSource(
        IReadOnlyList<OpenInterestPoint> openInterestHistory,
        IReadOnlyList<LiquidationEvent> liquidationEvents) : IContextDataSource
    {
        public Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
            string symbol,
            ContextFrame frame,
            int limit,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(openInterestHistory);
        }

        public IAsyncEnumerable<LiquidationEvent> ReadLiquidationsAsync(
            string symbol,
            CancellationToken cancellationToken = default)
        {
            return liquidationEvents.ToAsyncEnumerable();
        }
    }
}
```

- [ ] **Step 2: Run Application tests**

Expected: FAIL because Application context types do not exist.

- [ ] **Step 3: Implement Application context boundary**

Use these signatures:

```csharp
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Application.Context;

public interface IContextDataSource
{
    Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
        string symbol,
        ContextFrame frame,
        int limit,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<LiquidationEvent> ReadLiquidationsAsync(
        string symbol,
        CancellationToken cancellationToken = default);
}
```

```csharp
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Application.Context;

public sealed record ContextModuleSample(
    string Symbol,
    ContextFrame Frame,
    DateTimeOffset Timestamp,
    IReadOnlyList<ContextTile> LiquidationTiles,
    IReadOnlyList<ContextTile> OpenInterestTiles,
    string LiquidationStatus,
    string OpenInterestStatus);
```

`ContextModuleSession` should:

```text
1. Load OI history once at startup.
2. Seed OpenInterestContextCalculator.
3. Pass latest OI value to LiquidationContextCalculator.
4. Emit an initial ContextModuleSample.
5. Consume liquidation events and emit a new sample after each event.
```

Keep OI history refresh polling out of this first Application task; add it in Desktop/Infrastructure wiring after the core boundary is tested.

- [ ] **Step 4: Run Application tests and boundary test**

Run:

```powershell
& '.dotnet\dotnet.exe' test CryptoIndicatorApp.Application.Tests\CryptoIndicatorApp.Application.Tests.csproj --no-restore
```

Expected: PASS. `ApplicationAssemblyDoesNotReferenceInfrastructure` must still pass.

---

## Task 4: Infrastructure Binance Context Mapping

**Files:**
- Create `CryptoIndicatorApp.Infrastructure/Binance/BinanceContextEventMapper.cs`
- Modify `CryptoIndicatorApp.Infrastructure/Binance/IBinanceUsdFuturesMarketDataClient.cs`
- Modify `CryptoIndicatorApp.Infrastructure/Binance/BinanceNetUsdFuturesMarketDataClient.cs`
- Test: `CryptoIndicatorApp.Infrastructure.Tests/BinanceContextMapperTests.cs`

- [ ] **Step 1: Write failing mapper tests**

Add tests:

```csharp
using CryptoIndicatorApp.Infrastructure.Binance;
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class BinanceContextMapperTests
{
    [Fact]
    public void Liquidation_mapper_normalizes_symbol_and_keeps_side()
    {
        var tradeTime = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        var receiveTime = tradeTime.AddMilliseconds(100);

        var item = BinanceContextEventMapper.ToLiquidation(
            "xanusdt",
            "BUY",
            averagePrice: 10m,
            quantityFilled: 2m,
            tradeTime,
            receiveTime);

        Assert.Equal("XANUSDT", item.Symbol);
        Assert.Equal("BUY", item.Side);
        Assert.Equal(20m, item.SignedNotional);
    }

    [Fact]
    public void Open_interest_mapper_normalizes_symbol_and_keeps_value()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        var receiveTime = timestamp.AddMilliseconds(80);

        var point = BinanceContextEventMapper.ToOpenInterest(
            "xanusdt",
            sumOpenInterest: 123m,
            sumOpenInterestValue: 456m,
            timestamp,
            receiveTime);

        Assert.Equal("XANUSDT", point.Symbol);
        Assert.Equal(456m, point.SumOpenInterestValue);
    }
}
```

- [ ] **Step 2: Run Infrastructure tests**

Expected: FAIL because mapper does not exist.

- [ ] **Step 3: Implement mapper**

```csharp
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Infrastructure.Binance;

public static class BinanceContextEventMapper
{
    public static LiquidationEvent ToLiquidation(
        string symbol,
        string side,
        decimal averagePrice,
        decimal quantityFilled,
        DateTimeOffset tradeTime,
        DateTimeOffset receiveTime)
    {
        return new LiquidationEvent(
            NormalizeSymbol(symbol),
            side.Trim().ToUpperInvariant(),
            averagePrice,
            quantityFilled,
            tradeTime,
            tradeTime,
            receiveTime);
    }

    public static OpenInterestPoint ToOpenInterest(
        string symbol,
        decimal sumOpenInterest,
        decimal sumOpenInterestValue,
        DateTimeOffset timestamp,
        DateTimeOffset receiveTime)
    {
        return new OpenInterestPoint(
            NormalizeSymbol(symbol),
            sumOpenInterest,
            sumOpenInterestValue,
            timestamp,
            receiveTime);
    }

    private static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim().ToUpperInvariant();
    }
}
```

- [ ] **Step 4: Extend Binance client interface**

Add to `IBinanceUsdFuturesMarketDataClient`:

```csharp
Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
    string symbol,
    ContextFrame frame,
    int limit,
    CancellationToken cancellationToken = default);

Task<IAsyncDisposable> SubscribeLiquidationsAsync(
    string symbol,
    Action<LiquidationEvent> onLiquidation,
    CancellationToken cancellationToken = default);
```

Implementation notes for `BinanceNetUsdFuturesMarketDataClient`:

```csharp
var period = frame == ContextFrame.FiveMinutes
    ? PeriodInterval.FiveMinutes
    : PeriodInterval.FifteenMinutes;

var result = await _restClient.UsdFuturesApi.ExchangeData.GetOpenInterestHistoryAsync(
    NormalizeSymbol(symbol),
    period,
    limit,
    null,
    null,
    cancellationToken);
```

```csharp
var result = await _socketClient.UsdFuturesApi.ExchangeData.SubscribeToLiquidationUpdatesAsync(
    NormalizeSymbol(symbol),
    update =>
    {
        var data = update.Data;
        onLiquidation(BinanceContextEventMapper.ToLiquidation(
            data.Symbol,
            data.Side.ToString(),
            data.AveragePrice,
            data.QuantityFilled,
            ToDateTimeOffset(data.Timestamp),
            ToDateTimeOffset(update.ReceiveTime)));
    },
    cancellationToken);
```

If compile fails on `data.Side.ToString()` or `data.QuantityFilled`, inspect `.nuget/packages/binance.net/12.12.0/lib/net8.0/Binance.Net.xml` around `BinanceFuturesStreamLiquidation` properties and adjust only inside Infrastructure.

- [ ] **Step 5: Run Infrastructure tests**

Expected: PASS.

---

## Task 5: Desktop Configuration And ViewModel Projection

**Files:**
- Create `CryptoIndicatorApp.Desktop/Configuration/ContextOptions.cs`
- Modify `CryptoIndicatorApp.Desktop/Configuration/DashboardOptions.cs`
- Modify `CryptoIndicatorApp.Desktop/appsettings.json`
- Create `CryptoIndicatorApp.Desktop/ViewModels/ContextTileViewModel.cs`
- Modify `CryptoIndicatorApp.Desktop/ViewModels/DashboardViewModel.cs`
- Test: `CryptoIndicatorApp.Desktop.Tests/DashboardConfigurationTests.cs`
- Test: `CryptoIndicatorApp.Desktop.Tests/DashboardViewModelTests.cs`

- [ ] **Step 1: Write failing Desktop config/ViewModel tests**

Add assertions:

```csharp
Assert.Equal(ContextFrame.FifteenMinutes, options.Context.Frame);
Assert.Equal(150, options.Context.VisibleMinutes);
Assert.Equal(24, options.Context.NormalizationHistoryHours);
Assert.Equal(12, options.Context.MinimumNormalizationBuckets);
```

Add ViewModel test:

```csharp
[Fact]
public void Context_tile_projection_keeps_direction_and_gradient_intensity()
{
    var viewModel = new DashboardViewModel(new DashboardOptions());
    var start = DateTimeOffset.Parse("2026-05-26T12:00:00Z");

    viewModel.ApplyContextSample(new ContextModuleSample(
        "XANUSDT",
        ContextFrame.FifteenMinutes,
        start,
        new[]
        {
            new ContextTile(start, start.AddMinutes(15), 100m, 0.001m, ContextDirection.Positive, 0.75d, true, "Ready")
        },
        new[]
        {
            new ContextTile(start, start.AddMinutes(15), -200m, -0.002m, ContextDirection.Negative, 0.5d, true, "Ready")
        },
        "Ready",
        "Ready"));

    Assert.Single(viewModel.LiquidationTiles);
    Assert.Equal("Ready", viewModel.LiquidationStatus);
    Assert.Equal("Ready", viewModel.OpenInterestStatus);
    Assert.Equal(0.75d, viewModel.LiquidationTiles[0].Intensity);
}
```

- [ ] **Step 2: Run Desktop tests**

Expected: FAIL because context config/ViewModel properties do not exist.

- [ ] **Step 3: Implement config**

```csharp
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Desktop.Configuration;

public sealed class ContextOptions
{
    public ContextFrame Frame { get; set; } = ContextFrame.FifteenMinutes;
    public int VisibleMinutes { get; set; } = 150;
    public int NormalizationHistoryHours { get; set; } = 24;
    public int MinimumNormalizationBuckets { get; set; } = 12;
    public decimal NormalizationFloor { get; set; } = 0.00000001m;
    public int OpenInterestHistoryLimit { get; set; } = 288;

    public TimeSpan VisibleDuration => TimeSpan.FromMinutes(VisibleMinutes > 0 ? VisibleMinutes : 150);
    public TimeSpan NormalizationHistory => TimeSpan.FromHours(NormalizationHistoryHours > 0 ? NormalizationHistoryHours : 24);

    public void Normalize()
    {
        if (Frame is not ContextFrame.FiveMinutes and not ContextFrame.FifteenMinutes)
        {
            Frame = ContextFrame.FifteenMinutes;
        }

        if (VisibleMinutes <= 0)
        {
            VisibleMinutes = 150;
        }

        if (NormalizationHistoryHours <= 0)
        {
            NormalizationHistoryHours = 24;
        }

        if (MinimumNormalizationBuckets <= 0)
        {
            MinimumNormalizationBuckets = 12;
        }

        if (NormalizationFloor <= 0m)
        {
            NormalizationFloor = 0.00000001m;
        }
    }
}
```

Add `public ContextOptions Context { get; set; } = new();` to `DashboardOptions` and call `Context.Normalize()` from `Normalize()`.

Update `appsettings.json`:

```json
"Context": {
  "Frame": "FifteenMinutes",
  "VisibleMinutes": 150,
  "NormalizationHistoryHours": 24,
  "MinimumNormalizationBuckets": 12,
  "NormalizationFloor": 0.00000001,
  "OpenInterestHistoryLimit": 288
}
```

- [ ] **Step 4: Implement ViewModel projection**

`ContextTileViewModel`:

```csharp
using System.Globalization;
using System.Windows.Media;
using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Desktop.ViewModels;

public sealed record ContextTileViewModel(
    string Label,
    string ValueText,
    ContextDirection Direction,
    double Intensity,
    Brush BackgroundBrush)
{
    public static ContextTileViewModel FromTile(ContextTile tile)
    {
        var label = tile.BucketStart.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        var value = tile.NormalizedDelta is null
            ? tile.Status
            : tile.NormalizedDelta.Value.ToString("0.0000%", CultureInfo.InvariantCulture);

        return new ContextTileViewModel(
            label,
            value,
            tile.Direction,
            tile.Intensity,
            CreateBrush(tile.Direction, tile.Intensity));
    }

    private static Brush CreateBrush(ContextDirection direction, double intensity)
    {
        var target = direction == ContextDirection.Positive
            ? Color.FromRgb(34, 197, 94)
            : direction == ContextDirection.Negative
                ? Color.FromRgb(239, 68, 68)
                : Color.FromRgb(229, 231, 235);

        var amount = direction is ContextDirection.Positive or ContextDirection.Negative
            ? Math.Clamp(0.12d + (intensity * 0.68d), 0d, 0.8d)
            : 0.4d;

        var brush = new SolidColorBrush(Blend(Color.FromRgb(255, 255, 255), target, amount));
        brush.Freeze();
        return brush;
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        static byte Channel(byte a, byte b, double amount) => (byte)Math.Round(a + ((b - a) * amount));
        return Color.FromRgb(Channel(from.R, to.R, amount), Channel(from.G, to.G, amount), Channel(from.B, to.B, amount));
    }
}
```

Add to `DashboardViewModel`:

```csharp
public IReadOnlyList<ContextFrame> ContextFrames { get; } =
    new[] { ContextFrame.FifteenMinutes, ContextFrame.FiveMinutes };

public IReadOnlyList<ContextTileViewModel> LiquidationTiles { get; private set; } = Array.Empty<ContextTileViewModel>();
public IReadOnlyList<ContextTileViewModel> OpenInterestTiles { get; private set; } = Array.Empty<ContextTileViewModel>();
public string LiquidationStatus { get; private set; } = "Not started";
public string OpenInterestStatus { get; private set; } = "Not started";

public void ApplyContextSample(ContextModuleSample sample)
{
    LiquidationTiles = sample.LiquidationTiles.Select(ContextTileViewModel.FromTile).ToArray();
    OpenInterestTiles = sample.OpenInterestTiles.Select(ContextTileViewModel.FromTile).ToArray();
    LiquidationStatus = sample.LiquidationStatus;
    OpenInterestStatus = sample.OpenInterestStatus;
    OnPropertyChanged(nameof(LiquidationTiles));
    OnPropertyChanged(nameof(OpenInterestTiles));
    OnPropertyChanged(nameof(LiquidationStatus));
    OnPropertyChanged(nameof(OpenInterestStatus));
}
```

- [ ] **Step 5: Run Desktop tests**

Expected: PASS after imports/properties are correct.

---

## Task 6: Desktop Composition And WPF Context Strips

**Files:**
- Create `CryptoIndicatorApp.Desktop/Composition/BinanceContextDataSource.cs`
- Modify `CryptoIndicatorApp.Desktop/MainWindow.xaml.cs`
- Modify `CryptoIndicatorApp.Desktop/MainWindow.xaml`
- Test: `CryptoIndicatorApp.Desktop.Tests/BinanceContextCompositionAdapterTests.cs`

- [ ] **Step 1: Write failing composition adapter test**

```csharp
using CryptoIndicatorApp.Application.Context;
using CryptoIndicatorApp.Desktop.Composition;

namespace CryptoIndicatorApp.Desktop.Tests;

public sealed class BinanceContextCompositionAdapterTests
{
    [Fact]
    public void Binance_context_source_implements_application_boundary()
    {
        Assert.True(typeof(IContextDataSource).IsAssignableFrom(typeof(BinanceContextDataSource)));
    }
}
```

- [ ] **Step 2: Implement composition adapter**

```csharp
using CryptoIndicatorApp.Application.Context;
using CryptoIndicatorApp.Domain.Context;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Desktop.Composition;

public sealed class BinanceContextDataSource : IContextDataSource, IDisposable
{
    private readonly BinanceNetUsdFuturesMarketDataClient _client;

    private BinanceContextDataSource(BinanceNetUsdFuturesMarketDataClient client)
    {
        _client = client;
    }

    public static BinanceContextDataSource Create(ProxyOptions? proxyOptions = null)
    {
        var options = new BinanceConnectionOptions
        {
            Proxy = BinanceProxyOptionsMapper.ToInfrastructure(proxyOptions)
        };

        return new BinanceContextDataSource(new BinanceNetUsdFuturesMarketDataClient(options));
    }

    public Task<IReadOnlyList<OpenInterestPoint>> GetOpenInterestHistoryAsync(
        string symbol,
        ContextFrame frame,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return _client.GetOpenInterestHistoryAsync(symbol, frame, limit, cancellationToken);
    }

    public async IAsyncEnumerable<LiquidationEvent> ReadLiquidationsAsync(
        string symbol,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<LiquidationEvent>();
        await using var lease = await _client.SubscribeLiquidationsAsync(
            symbol,
            item => channel.Writer.TryWrite(item),
            cancellationToken);

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
```

- [ ] **Step 3: Wire context session lifecycle in `MainWindow.xaml.cs`**

Add fields:

```csharp
private CancellationTokenSource? _contextCancellation;
private Task? _contextTask;
```

On live start, start context module in parallel with market session:

```csharp
_contextCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
_contextTask = StartContextSessionAsync(symbol, _contextCancellation.Token);
```

Implement:

```csharp
private async Task StartContextSessionAsync(string symbol, CancellationToken cancellationToken)
{
    try
    {
        using var source = BinanceContextDataSource.Create(_options.Proxy);
        var context = _options.Context;
        var session = new ContextModuleSession(
            symbol,
            context.Frame,
            source,
            context.VisibleDuration,
            context.NormalizationHistory,
            context.MinimumNormalizationBuckets,
            context.NormalizationFloor,
            context.OpenInterestHistoryLimit);

        await foreach (var sample in session.RunAsync(cancellationToken).WithCancellation(cancellationToken))
        {
            await Dispatcher.InvokeAsync(() => _viewModel.ApplyContextSample(sample));
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
        await Dispatcher.InvokeAsync(() => _viewModel.MarkContextError(ex.Message));
    }
}
```

Cancel and dispose `_contextCancellation` in `StopCurrentSessionAsync`, `CleanupCompletedSession`, and `MainWindow_Closed`.

Replay mode should not run live context modules in this slice. Show `Live context only` or `Not running` statuses.

- [ ] **Step 4: Add WPF strips**

Add below the existing chart or above path footer:

```xml
<Border Grid.Row="3"
        Background="#FFFFFF"
        BorderBrush="#DDE2E7"
        BorderThickness="1"
        CornerRadius="6"
        Padding="10"
        Margin="0,10,0,0">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <TextBlock Text="Liquidations" FontWeight="SemiBold" />
        <ItemsControl Grid.Row="0"
                      Margin="110,0,0,8"
                      ItemsSource="{Binding LiquidationTiles}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <UniformGrid Rows="1" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
        </ItemsControl>
        <TextBlock Grid.Row="1" Text="Open Interest" FontWeight="SemiBold" />
        <ItemsControl Grid.Row="1"
                      Margin="110,0,0,0"
                      ItemsSource="{Binding OpenInterestTiles}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <UniformGrid Rows="1" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
        </ItemsControl>
    </Grid>
</Border>
```

Use a compact `DataTemplate` for `ContextTileViewModel` with fixed width/height and text trimming. If 30 tiles at 5m do not fit at the current min width, use horizontal `ScrollViewer`; do not shrink text below readability.

- [ ] **Step 5: Run Desktop tests**

Expected: PASS.

---

## Task 7: Full Verification, Publish, And Live Smoke

**Files:**
- Modify `tasks/todo.md`

- [ ] **Step 1: Run full solution tests**

Run:

```powershell
$env:DOTNET_CLI_HOME='C:\Users\Steven Owl\Desktop\PRJCT-INDIC\.dotnet-home'
$env:APPDATA='C:\Users\Steven Owl\Desktop\PRJCT-INDIC\.dotnet-home\AppData'
$env:NUGET_PACKAGES='C:\Users\Steven Owl\Desktop\PRJCT-INDIC\.nuget\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& '.dotnet\dotnet.exe' test CryptoIndicatorApp.sln --no-restore
```

Expected: all tests pass. Existing `NU1900` warning may appear and is not a failure.

- [ ] **Step 2: Publish Desktop**

Before publish, check if the app is running:

```powershell
Get-Process -Name CryptoIndicatorApp.Desktop -ErrorAction SilentlyContinue
```

If a process is running, ask before stopping it. Then publish:

```powershell
& '.dotnet\dotnet.exe' publish CryptoIndicatorApp.Desktop\CryptoIndicatorApp.Desktop.csproj -c Release -o publish\desktop --no-restore
```

Expected: publish succeeds with 0 errors.

- [ ] **Step 3: Live smoke test**

Use GUI/manual or a small temporary debug run. For each of `ESPORTSUSDT`, `XANUSDT`, `PLAYUSDT`:

```text
1. Refresh symbols.
2. Select symbol.
3. Start Live.
4. Verify OI strip loads historical 5m/15m tiles.
5. Verify liquidation strip shows warm-up/no events if no forceOrder arrives.
6. If a forceOrder arrives, verify green for BUY and red for SELL with brightness based on normalized magnitude.
7. Stop.
```

Do not require liquidation events to occur during smoke. Success criteria for the liquidation module is source subscription without error plus correct rendering if events arrive.

- [ ] **Step 4: Record results**

Append to `tasks/todo.md`:

```markdown
## Liquidation And Open Interest Context Modules Results

- HOFI/TFI formula unchanged.
- Added separate context models, calculators, Binance source mapping, Application context session, and WPF strips.
- Verification command: `dotnet test CryptoIndicatorApp.sln --no-restore`; record the exact passed test count from the terminal output.
- Publish command: `dotnet publish CryptoIndicatorApp.Desktop\CryptoIndicatorApp.Desktop.csproj -c Release -o publish\desktop --no-restore`; record whether it completed with 0 errors.
- Live smoke symbols: `ESPORTSUSDT`, `XANUSDT`, `PLAYUSDT`; record whether OI history loaded and whether liquidation subscription connected without error for each symbol.
- Known limitation: Binance liquidation stream reports only the largest liquidation snapshot per 1000 ms interval, not complete liquidation history.
```

---

## Self-Review Checklist

- HOFI/TFI formula untouched: no changes to `TcDnHofi3Engine`, `IndicatorParameters`, `IndicatorSample`, or `SignalState`.
- Application still has no Infrastructure reference.
- Binance DTOs stay inside Infrastructure.
- OI direction means increase/decrease only, not long/short.
- Liquidation data is labeled as observed liquidation pressure, not total market liquidations.
- 5m/15m frame switch exists, default is 15m.
- Normalization separates direction from intensity.
- Warm-up prevents false bright tiles when history or OI denominator is missing.
- Replay mode does not pretend to have live context in this slice.
