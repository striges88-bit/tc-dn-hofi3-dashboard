# Memory Rules

Format: `id | status | active_scope | text`.

- rule.rest-hot-path-ban | current | data-pipeline | REST must not be used in the hot path for subsecond depth/trade feature calculation; REST depth snapshots are allowed only for initial local book construction and explicit resync.
- rule.binance-dto-boundary | current | architecture | Binance client DTOs and third-party API models stay inside Infrastructure and must not leak into Domain, Application, or the indicator engine.
- rule.live-replay-same-pipeline | current | replay | Live mode and replay mode must feed the same internal event types into the same indicator pipeline.
- rule.funding-slow-context | current | formula-context | Funding, liquidation, and open-interest data are slow regime/risk context, not subsecond entry triggers or formula changes without a separate approved formula version.
- rule.legacy-superseded | superseded | historical-test | legacy superseded-only phrase
