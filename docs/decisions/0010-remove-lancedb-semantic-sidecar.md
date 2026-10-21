# 0010: Remove the LanceDB Semantic Sidecar

Date: 2026-10-21

## Status

Accepted. Supersedes the sidecar clauses of ADR 0003 and vacates the LanceDB eval-gate references in ADRs 0005, 0007, and 0008.

## Decision

Remove the LanceDB semantic sidecar entirely. Deleted: `tools/MemorySemantic/`, `scripts/lancedb-sidecar.ps1`, `scripts/memory-semantic-doctor.ps1`, `docs/memory/lancedb-spike.md`, the sidecar test file, and every sidecar reference in the memory docs. The sidecar steps are gone from the operator paths: `memory-refresh-all` runs three steps (legacy JSON refresh, SQLite `refresh-from-commit --commit HEAD`, SQLite stale-check), `memory-pre-push-check` runs four refresh-report checks, `memory-daily-check` no longer reports a LanceDB eval status, and `memory-rebuild-from-head` no longer lists sidecar delete targets. `memory refresh` reports `semantic_sidecar: none`.

SQLite FTS5 stays the canonical memory store (ADR 0003). The manual-gate, commit-addressed-refresh, and marker-only-hook decisions themselves remain in force.

## Rationale

The sidecar failed on environment, not logic: `uv` discovery outside PATH, a per-session ephemeral `UV_CACHE_DIR` in sandboxed terminals that evicted the offline package cache, and preflight timeouts around the Python 3.12 plus `lancedb`/`pyarrow`/`fastembed` download. Every incident was operational plumbing for a second runtime, while the sidecar's own retrieval logic passed its eval.

The retrieval value did not justify that cost. The 11/11 eval baseline measured FastEmbed recall over records that SQLite already owns; SQLite FTS5 already backs the retrieval protocol the contract defines, and the remaining no-answer/low-confidence cases were decided by lexical confidence thresholds rather than by vector distances. A mandatory gate with a fragile third-party cache is worse than no gate.

Steven chose full removal over demoting the sidecar to an optional diagnostic or stabilizing the cache on 2026-10-21. The eval case list and reranking lessons stay recorded in `tasks/lessons.md` and `tasks/todo.md` history for reuse if a later ADR reintroduces semantic search.

## Consequences

- Memory gates require only the .NET runtime; no Python, `uv`, or offline package cache.
- `memory-refresh-all` runs three steps instead of six.
- Local leftover artifacts under `docs/memory/generated/lancedb*` are ignored by Git and safe to delete manually; no script references them anymore.
- Semantic retrieval is SQLite FTS5 only. A future semantic sidecar needs a new ADR, must stay below SQLite status/source metadata, and must not sit inside a mandatory gate.