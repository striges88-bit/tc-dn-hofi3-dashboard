# Data Sources

The MVP uses public Binance USDS-M Futures market data.

## Hot Path

- Diff depth stream: `<symbol>@depth@100ms`.
- Aggregate trade stream: `<symbol>@aggTrade`.
- REST depth snapshot is used only for initial local order book construction and explicit resync.

REST must not be used for subsecond feature calculation.

## Slow Context

- Observed liquidation snapshots use the symbol force-order stream.
- Open interest context uses Binance REST open-interest endpoints on a conservative refresh interval.
- These context modules are risk/background data, not subsecond entry triggers.

## Local Recordings

JSONL recordings are local data artifacts. They may contain large market data captures and are ignored by Git by default.

Keep only documentation such as `recordings/README.md` in the repository unless a small deterministic fixture is intentionally added under a separate test fixture path.
