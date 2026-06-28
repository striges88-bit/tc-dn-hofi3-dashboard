# TC-DN-HOFI3 Dashboard

Windows WPF analytics application for the TC-DN-HOFI3 Binance USDS-M Futures indicator.

The project is research and monitoring software. It does not place orders, manage positions, or provide automated trading execution.

## Current Scope

- Single-symbol Binance USDS-M Futures live dashboard.
- Local order book from snapshot plus diff depth stream.
- Aggregate trades for trade-flow confirmation.
- JSONL raw market event recording.
- Deterministic replay through the same internal event pipeline.
- Slow context modules for observed liquidation snapshots and open interest.

## Repository Layout

- `CryptoIndicatorApp.Domain/`: market events, order book, indicator math, context calculators.
- `CryptoIndicatorApp.Application/`: live/replay orchestration, chart buffers, context sessions.
- `CryptoIndicatorApp.Infrastructure/`: Binance adapters, JSONL reader/writer, external data mapping.
- `CryptoIndicatorApp.Desktop/`: WPF UI, configuration binding, desktop composition.
- `*.Tests/`: deterministic unit and boundary tests.
- `tools/`: non-GUI utilities such as live dry-run/replay checks.
- `docs/`: durable project knowledge and architecture decisions.
- `tasks/`: working todo/results log and lessons learned.
- `recordings/`: local JSONL recordings; recording data is intentionally ignored by Git.

## Basic Verification

```powershell
dotnet test CryptoIndicatorApp.sln --no-restore
dotnet build CryptoIndicatorApp.sln --no-restore
```

If dependencies were changed or assets are stale, run restore before using `--no-restore`.
