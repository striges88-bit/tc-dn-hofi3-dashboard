# 0003: SQLite FTS5 Canonical Memory Store

Date: 2026-06-28

## Decision

Use SQLite FTS5 as the canonical local generated memory store for agent tooling. Keep LanceDB as a deferred semantic sidecar candidate. Move Hindsight to historical/failed spike status and keep GBrain as historical/secondary.

The WPF/.NET application runtime must not depend on the memory store or semantic sidecars.

## Rationale

Hindsight proved too operationally heavy for the MVP memory layer: useful retain/recall is blocked by OpenAI `billing_not_active`, embedded bank commands forward to a separate Rust CLI that failed locally, and import/retention/export/delete behavior remains uncertain.

SQLite is embedded, inspectable, deterministic, easy to refresh from repo sources, and sufficient for procedural, episodic, and code/project memory when paired with FTS5, source hashes, statuses, and stale checks.

## Consequences

- `tools/Memory` owns `refresh`, `search`, `explain`, and `stale-check`.
- SQLite `EXPLAIN QUERY PLAN` plus local `query_log` are the SQL debugging tools.
- PostgreSQL-only diagnostics are not part of this architecture.
- Formula changes require new `formula_version` records.
- Architectural decisions require ADRs.
- Important experiments require experiment outcomes.
- Regressions require incident notes.
- LanceDB can only be added later as a sidecar below SQLite status/source metadata.
