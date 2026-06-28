# Glossary

- TC-DN-HOFI3: research indicator based on top-level order-flow imbalance, depth normalization, robust z-score, and trade-flow confirmation.
- HOFI: top-heavy order-flow imbalance calculated from selected order book levels.
- NOFI: depth-normalized order-flow imbalance.
- TFI: trade-flow imbalance based on aggressive notional flow.
- JSONL recording: line-delimited market event envelope used for deterministic replay.
- Replay: deterministic processing of recorded market events through the same internal pipeline as live mode.
- Hot path: subsecond depth/trade processing used for indicator features.
- Slow context: lower-frequency risk/background data such as observed liquidations and open interest.
- Observed liquidation snapshot: Binance force-order event snapshot, not complete liquidation history.
