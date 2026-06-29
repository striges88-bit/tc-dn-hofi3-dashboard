# LanceDB Semantic Sidecar Spike

Status: deferred sidecar candidate; not installed in the SQLite FTS5 MVP slice.

## Decision

LanceDB may be evaluated later as an embedded semantic sidecar for embeddings, hybrid search, metadata filtering, cleanup, versioning, and reranking. It must not store canonical status. SQLite remains authoritative for `current`, `proposed`, `superseded`, `failed`, `source_path`, and `source_hash`.

## Guardrails

- Do not import raw JSONL recordings, generated exports, secrets, local proxy details, build artifacts, or unreviewed experiment dumps.
- Import only records exported from SQLite with source metadata and status.
- Do not rank LanceDB semantic matches above SQLite current records without a freshness check.
- Confirm the real OSS embedded explain/analyze surface before promising LanceDB query-plan diagnostics.
- Keep LanceDB outside the WPF/.NET runtime, build, and application tests.

## Acceptance For Future Spike

- Local-path embedded store is created under ignored local/generated memory paths.
- Hybrid search and metadata filters are verified on SQLite-exported records.
- Reindexing and cleanup behavior are deterministic.
- Explain/analyze behavior is confirmed from the real API surface.
- Retrieval tests prove a superseded SQLite record is not returned as current through LanceDB.
