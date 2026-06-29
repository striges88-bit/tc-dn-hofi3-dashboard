# LanceDB Semantic Sidecar Spike

Status: active local spike. This replaces the earlier deferred status, but LanceDB is still not a canonical store.

## Decision

Evaluate LanceDB as a local Python embedded semantic sidecar under `docs/memory/generated/lancedb`. The sidecar reads only canonical SQLite `search_documents` records with `status IN ('current', 'proposed')`, valid `source_path`, and valid `source_hash`. It must not crawl project files directly.

SQLite remains authoritative for `current`, `proposed`, `superseded`, `failed`, `source_path`, and `source_hash`. LanceDB can copy that metadata for filtering, freshness checks, and reranking, but it cannot own or mutate canonical status.

## Current Tooling

- Wrapper: `scripts/lancedb-sidecar.ps1`.
- Python script: `tools/MemorySemantic/lancedb_sidecar.py`.
- Store path: `docs/memory/generated/lancedb`.
- Report path: `docs/memory/generated/lancedb-sidecar-report.json`.
- Commands: `probe`, `rebuild`, `search`, `explain`, and `cleanup`.
- Runtime mode: local Python embedded through `uv`; no Cloud, no service account, no OpenAI key, no Codex auto-retain.

The spike uses deterministic local token-hash vectors to verify LanceDB mechanics without external models or Cloud calls. That is enough for clean rebuild/delete/reindex and API smoke testing, but it is not enough to judge final semantic recall quality.

## Guardrails

- Do not import raw JSONL recordings, generated exports, secrets, local proxy details, build artifacts, or unreviewed experiment dumps.
- Import only records exported from SQLite with source metadata and status.
- Do not rank LanceDB semantic matches above SQLite current records without a freshness check.
- Do not install a git post-commit hook or after-save auto-refresh until clean rebuild/delete/reindex behavior is proven.
- Keep LanceDB outside the WPF/.NET runtime, build, and application tests.
- Treat generated facts without `source_path`/`source_hash` as invalid and skip them.

## Clean Rebuild/Delete/Reindex

The first accepted behavior is clean rebuild/delete/reindex:

1. Refresh SQLite with `tools/Memory`.
2. Run `scripts/lancedb-sidecar.ps1 -Command cleanup`.
3. Run `scripts/lancedb-sidecar.ps1 -Command rebuild`.
4. Run `scripts/lancedb-sidecar.ps1 -Command search -Query "actual OFI formula"`.
5. Run `scripts/lancedb-sidecar.ps1 -Command explain -Query "actual OFI formula"`.

No commit hook or background automation is allowed until this sequence is repeatable.

## Local Smoke Result

Date: 2026-06-29.

- `uv run --python 3.12 --with lancedb --with pyarrow ...` downloaded and ran local embedded LanceDB successfully.
- `rebuild` created `docs/memory/generated/lancedb` from SQLite and indexed `271` current/proposed records.
- `cleanup` deleted the generated LanceDB store with `deleted_existing_store=true`.
- A second `rebuild` recreated the store and indexed the same `271` records.
- `search "actual OFI formula"` returned `formula_version.tc-dn-hofi3.current` first after local typed/exact-token reranking.
- `explain "actual OFI formula"` returned LanceDB `explain_plan` and `analyze_plan`; the plan shows `KNNVectorDistance`, `LanceRead`, and `TopK`.

## Limitations

- The current embedding strategy is deterministic token hashing, not a production semantic embedding model.
- Raw vector distance alone ranked generic chunks above the current formula record during the first smoke. The sidecar now applies a small local reranker that prefers typed records such as `formula_version` and ADRs over generic chunks when exact-token overlap is present.
- This does not replace SQLite FTS5 retrieval tests. It only proves that local LanceDB rebuild/search/explain mechanics are viable.
