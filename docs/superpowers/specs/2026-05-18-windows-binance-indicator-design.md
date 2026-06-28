# Windows Binance Indicator Design

## Goal

Build a Windows-only analytical desktop application that connects to Binance USDS-M Futures public market data, records raw market events to JSONL, replays recorded data deterministically, calculates TC-DN-HOFI3 indicator values, and displays current values plus a 60-second chart.

This is not a trading bot. The app does not place, simulate, or recommend orders in the MVP.

## Scope

In scope for MVP:

- C# desktop application using WPF.
- One configured USDS-M Futures symbol per run.
- Live mode using Binance USDS-M Futures public streams.
- JSONL recording from the first version.
- Replay mode from JSONL files.
- Local order book maintenance from snapshot plus diff depth updates.
- TC-DN-HOFI3 calculation based on the formula in `TC-DN-HOFI3.md`.
- Simple dashboard with current indicator values, health status, latency, recording status, and a 60-second chart.
- Unit tests for market event handling, order book sequencing, and indicator calculations.

Out of scope for MVP:

- Spot market support.
- Multiple symbols at once.
- Trading, order placement, alerts, or execution simulation.
- Parameter optimization grid, DSR/PBO validation, and full research backtesting.
- Parquet, database storage, or binary event logs.
- Complex charting dashboards beyond the last 60 seconds of `Z_OFI` and `TFI`.

## Technical Stack

- Language: C#.
- Runtime: .NET 8 LTS targeting Windows desktop (`net8.0-windows`).
- UI framework: WPF.
- Binance access: `Binance.Net` as a third-party C# client.
- Configuration: `Microsoft.Extensions.Configuration` with JSON config files.
- Recording format: JSONL.
- Tests: .NET test project with focused unit tests.

`Binance.Net` must be isolated behind infrastructure adapters. Domain and application code must not depend directly on Binance.Net models because it is a third-party dependency and may not match future Binance API changes.

## Solution Structure

Use explicit `Domain` and `Application` projects instead of a broad `Core` project:

```text
CryptoIndicatorApp/
  CryptoIndicatorApp.Desktop/
  CryptoIndicatorApp.Application/
  CryptoIndicatorApp.Domain/
  CryptoIndicatorApp.Infrastructure/
  CryptoIndicatorApp.Domain.Tests/
  CryptoIndicatorApp.Infrastructure.Tests/
  CryptoIndicatorApp.sln
```

Project responsibilities:

- `CryptoIndicatorApp.Desktop`: WPF views, ViewModels, XAML converters, and desktop composition.
- `CryptoIndicatorApp.Application`: live/replay session orchestration, app-level state, chart buffers, start/stop commands, and publication of indicator samples to the UI.
- `CryptoIndicatorApp.Domain`: internal market events, local order book sequencing, TC-DN-HOFI3 calculation, rolling windows, robust statistics, health flags, and latency/sample models.
- `CryptoIndicatorApp.Infrastructure`: Binance USDS-M Futures adapters, REST snapshot/resync, WebSocket subscriptions, JSONL reader/writer, configuration binding, and logging setup.
- `CryptoIndicatorApp.Domain.Tests`: deterministic tests for formulas, rolling windows, order book sequencing, trade classification, and replay calculation behavior.
- `CryptoIndicatorApp.Infrastructure.Tests`: serialization tests, JSONL reader/writer tests, adapter mapping tests, and non-live infrastructure behavior.

Avoid a generic `Core/Services/Models` structure in the MVP. Generic folders tend to hide the boundaries that matter most here: market-event contracts, order book sequencing, JSONL replay, and indicator calculation. Binance-specific interfaces must live at the infrastructure boundary; the domain should consume only project-owned market event types.

## Architecture

The MVP uses a layered single-process architecture:

```text
CryptoIndicatorApp.Desktop
        |
CryptoIndicatorApp.Application
        |
CryptoIndicatorApp.Domain
        |
CryptoIndicatorApp.Infrastructure
```

Responsibilities:

- WPF UI and ViewModels: bind current state to the screen, expose start/stop live mode, start replay mode, and show chart data.
- Application services: orchestrate live/replay sessions, recording, chart buffers, indicator sample publication, and error state.
- Domain engine: maintain order book state, calculate OFI/TFI/TC-DN-HOFI3, and evaluate health flags.
- Infrastructure adapters: connect to Binance, retrieve snapshots, subscribe to public streams, write/read JSONL, and load configuration.

Single-process keeps the MVP simple. The module boundaries are still strict so the market-data engine can become a separate process later if latency or stability requires it.

## Data Flow

Live mode:

```text
Binance WebSocket
  -> BinanceMarketDataSource
  -> internal market events
  -> JsonlRecorder
  -> OrderBook / IndicatorEngine
  -> DashboardViewModel
```

Replay mode:

```text
JSONL file
  -> JsonlReplaySource
  -> internal market events
  -> OrderBook / IndicatorEngine
  -> DashboardViewModel
```

Live and replay sources feed the same internal event types into the order book and indicator engine. This is a hard requirement. Replay must exercise the same calculation path as live mode.

## Market Data

MVP market scope is Binance USDS-M Futures only.

Required live data:

- Diff depth stream for the configured symbol using `<symbol>@depth@100ms`.
- Aggregate trade stream for the configured symbol using `<symbol>@aggTrade`.
- REST snapshot only for initial local order book construction and resync.

Hot-path indicator calculation must not depend on REST calls. REST is allowed for initial book snapshot and resync only.

The order book must track:

- sync state;
- last processed update id;
- stale or crossed book flags;
- resync count;
- receive timestamp;
- exchange event timestamp when available.

If sequence continuity fails, the book enters invalid/resync state and indicator samples must be marked unhealthy until the book is rebuilt.

## JSONL Event Model

Each JSONL row is a versioned envelope:

```json
{
  "schemaVersion": 1,
  "source": "binance-usdsm-futures",
  "stream": "depth",
  "eventType": "depthUpdate",
  "symbol": "BTCUSDT",
  "exchangeTime": "2026-05-18T12:00:00.123Z",
  "receiveTime": "2026-05-18T12:00:00.151Z",
  "payload": {}
}
```

MVP event types:

- `depthSnapshot`: initial or resync order book snapshot.
- `depthUpdate`: diff depth update from Binance public stream.
- `aggTrade`: aggregate trade event used for trade flow imbalance.

MVP payload contracts:

- `depthSnapshot.payload`: `lastUpdateId`, `bids`, `asks`.
- `depthUpdate.payload`: `firstUpdateId`, `finalUpdateId`, `previousFinalUpdateId`, `bids`, `asks`.
- `aggTrade.payload`: `aggregateTradeId`, `price`, `quantity`, `firstTradeId`, `lastTradeId`, `tradeTime`, `isBuyerMaker`.

JSONL stores market events, not just final indicator values. Indicator outputs can be logged later as a separate derived stream, but raw-ish market events are required for deterministic replay and debugging.

## Indicator Engine

The indicator engine implements TC-DN-HOFI3 from `TC-DN-HOFI3.md`.

MVP defaults:

- top levels: 3;
- top-heavy decay lambda: `0.8`;
- OFI window: rolling 250 ms;
- stability window: 1 second;
- depth reference: rolling median over 60 seconds;
- robust z-score window: rolling median/MAD over 180 seconds;
- default `thetaZ`: `2.0`;
- default `thetaStable`: `0.8`;
- default `thetaTFI`: `0.15`.

The engine updates rolling state on incoming events and emits an `IndicatorSample` every 100 ms while data is flowing.

Each `IndicatorSample` contains:

- timestamp;
- HOFI;
- NOFI;
- `Z_OFI`;
- TFI;
- signal state: neutral, long candidate, or short candidate;
- health flags;
- latency metrics if available.

Trade-side classification:

- Binance aggregate trade `m = true` means buyer is maker, so the aggressive side is sell.
- Binance aggregate trade `m = false` means buyer is taker, so the aggressive side is buy.

## UI

The MVP dashboard shows:

- configured symbol;
- mode: live or replay;
- connection status;
- recording status and current JSONL file path;
- book health: synced/stale/resync count;
- current `Z_OFI`;
- current TFI;
- current signal state;
- latency from exchange time to receive time where available;
- 60-second chart for `Z_OFI` and TFI.

The UI must not run indicator logic directly. It receives already calculated samples from application services.

## Configuration

Configuration is file-based.

Required configurable values:

- symbol;
- Binance market type fixed to USDS-M Futures for MVP;
- data directory;
- recording enabled by default;
- replay file path when running replay mode;
- indicator parameters listed in the MVP defaults section.

Formula parameters may be editable in config files. The MVP should avoid broad UI parameter editing because it encourages accidental overfitting before validation exists.

## Error Handling

The app must surface these states clearly:

- disconnected;
- connecting;
- live connected but book not synced;
- book resyncing;
- replay running;
- replay completed;
- recording failed;
- malformed JSONL row;
- unsupported schema version.

Malformed replay rows should stop replay with a clear error. Silent skipping would make deterministic verification unreliable.

## Testing

Required MVP tests:

- aggregate trade side classification test;
- CKS level OFI formula test;
- top-3 weighted HOFI test;
- TFI rolling window test;
- order book sequence continuity test;
- order book gap triggers invalid/resync state test;
- JSONL event envelope serialization/deserialization test;
- replay determinism test using a small fixture file.

The first implementation should prioritize deterministic unit tests over live integration tests. Live Binance tests are useful later but should not be required for every local test run.

## Risks And Pushbacks

- WPF is reasonable for Windows-only, but XAML/MVVM can slow a newcomer. Keep ViewModels small and avoid framework-heavy patterns until they pay for themselves.
- `Binance.Net` is practical, but it is not the domain model. Keep it behind adapters.
- JSONL is easy to debug but inefficient. That is acceptable for MVP; rotate files by session and defer compression/export.
- Single-symbol support is intentional. Multi-symbol support would multiply sequencing, UI, and performance risks before the core engine is verified.
- A live dashboard without replay is not trustworthy enough for this indicator. Recording and replay remain required from the first version.

## Acceptance Criteria

The MVP is acceptable when:

- the app starts on Windows and loads config;
- live mode can connect for one configured USDS-M Futures symbol;
- market events are recorded to JSONL with schema version `1`;
- replay mode can read the recorded JSONL and drive the same calculation path;
- the dashboard shows current `Z_OFI`, TFI, signal, book health, latency, and recording state;
- the chart shows the last 60 seconds of `Z_OFI` and TFI;
- deterministic tests pass for trade classification, OFI, TFI, JSONL, replay, and order book sequencing;
- no trading or order-placement capability exists in the MVP.
