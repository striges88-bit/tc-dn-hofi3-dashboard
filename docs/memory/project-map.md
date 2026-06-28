# Project Map

```text
Desktop
  composes Infrastructure adapters
  binds Application samples into WPF state

Application
  owns live/replay/session orchestration
  exposes chart and context samples

Domain
  owns market semantics and indicator calculation
  has no Binance or WPF dependencies

Infrastructure
  owns Binance, JSONL, and external payload translation
```

Important invariant: live and replay modes must feed the same internal event types into the indicator pipeline.
