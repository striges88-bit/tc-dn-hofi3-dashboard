# 0004: Funding Source Context

Date: 2026-06-29

## Decision

Funding-source data is slow regime and risk context for TC-DN-HOFI3, not a subsecond entry trigger and not part of the order-book/trade hot path.

The indicator hot path stays on local order-book diff depth and aggregate trade events. Funding context may be recorded, displayed, or replayed as a slower contextual source, but it must not change formula thresholds, cadence, or signal state without a separate formula version decision and deterministic replay evidence.

## Rationale

Funding changes on a slower cadence than the TC-DN-HOFI3 order-flow calculation. Mixing it into the hot path would blur the boundary between subsecond market microstructure inputs and slower regime filters, increasing latency and making replay validation harder.

## Consequences

- Funding-source changes require ADR/source-backed memory entries because they affect data semantics.
- Funding context remains below order-book and trade-flow data in retrieval priority for current formula behavior.
- Any future formula use of funding must create a new `formula_version` and pass deterministic replay tests.
