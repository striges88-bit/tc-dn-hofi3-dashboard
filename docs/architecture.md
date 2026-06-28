# Architecture

The application uses a layered single-process WPF architecture:

```text
CryptoIndicatorApp.Desktop
  -> CryptoIndicatorApp.Application
  -> CryptoIndicatorApp.Domain

CryptoIndicatorApp.Desktop
  -> CryptoIndicatorApp.Infrastructure
```

`Application` depends on `Domain` only. `Infrastructure` is composed from the desktop boundary so Binance DTOs, JSONL storage details, and third-party client APIs do not leak into the indicator engine.

## Layers

- `Domain`: project-owned market events, local order book sequencing, TC-DN-HOFI3 calculation, rolling statistics, health flags, and context calculators.
- `Application`: live/replay sessions, event pipeline orchestration, recording coordination, chart buffers, and context refresh sessions.
- `Infrastructure`: Binance USDS-M Futures adapters, REST snapshot/resync, WebSocket stream mapping, JSONL read/write, and external payload boundaries.
- `Desktop`: WPF views, ViewModels, configuration binding, user-facing error/status text, and composition of concrete adapters.

## Data Flow

Live and replay modes feed the same internal event types into the same pipeline:

```text
Live Binance streams -> Infrastructure mapping -> Application pipeline -> Domain engine -> Desktop state
JSONL replay         -> Infrastructure mapping -> Application pipeline -> Domain engine -> Desktop state
```

This keeps live behavior reproducible through recorded JSONL files.

## Boundaries

- Do not add REST calls to the hot indicator path.
- Do not reference `Infrastructure` from `Application`.
- Do not expose Binance client DTOs to `Domain`.
- Keep generated memory/graph exports outside Git until their schema is approved.
