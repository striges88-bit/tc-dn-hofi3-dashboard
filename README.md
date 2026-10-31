# TC-DN-HOFI3 Dashboard

Active project: **PRJCT-INDICv2**. The older PRJCT-INDIC checkout and its chats belong to a separate project. See [STATUS.md](STATUS.md) for the current preparation stage and deferred work.

Windows WPF analytics application for the TC-DN-HOFI3 Binance USDS-M Futures indicator.

The project is research and monitoring software. It does not place orders, manage positions, or provide automated trading execution.

Agent map: [docs/index.md](docs/index.md). Layer map: [docs/architecture.md](docs/architecture.md).

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
- `CryptoIndicatorApp.Infrastructure/`: Binance adapters, JSONL reader/writer, and the shared port adapters in `Composition`. This project references Application.
- `CryptoIndicatorApp.Desktop/`: WPF UI and configuration binding. It calls `Infrastructure/Composition` instead of owning those adapters.
- `*.Tests/`: deterministic unit and boundary tests.
- `tools/`: non-GUI utilities. `tools/LiveDryRun` calls the same `Infrastructure/Composition` adapters as the window.
- `docs/`: durable project knowledge and architecture decisions.
- `tasks/`: working todo/results log and lessons learned.
- `recordings/`: local JSONL recordings; recording data is intentionally ignored by Git.

## Basic Verification

`dotnet build` fails on violations of the IDE rules enabled in `.editorconfig` (braces, modifier order, accessibility modifiers), because `TreatWarningsAsErrors` is on. Layer references are enforced by allow-list tests in `CryptoIndicatorApp.Application.Tests/ApplicationBoundaryTests.cs`.

```powershell
dotnet test CryptoIndicatorApp.sln --no-restore
dotnet build CryptoIndicatorApp.sln --no-restore
```

If dependencies were changed or assets are stale, run restore before using `--no-restore`.
