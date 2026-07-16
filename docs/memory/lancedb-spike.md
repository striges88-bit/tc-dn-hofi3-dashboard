# LanceDB Semantic Sidecar Spike

Status: active production-candidate semantic quality layer. This replaces the earlier token-hash-only spike, but LanceDB is still not a canonical store.

## Decision

Evaluate LanceDB as a local Python embedded semantic sidecar under `docs/memory/generated/lancedb`. The sidecar reads only canonical SQLite `search_documents` records with `status IN ('current', 'proposed')`, valid `source_path`, and valid `source_hash`. It must not crawl project files directly.

The current production-candidate semantic quality layer uses local FastEmbed/ONNX embeddings by default:

- Provider: `fastembed`.
- Model: `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2`.
- Runtime model: `embedding_runtime_model=tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean`.
- LanceDB package pin: `lancedb==0.34.0`.
- PyArrow package pin: `pyarrow==24.0.0`.
- Package pin: `fastembed==0.8.0`.
- Pooling: `embedding_pooling=mean`.
- Pooling baseline: `embedding_pooling_baseline=mean-pooling`.
- Warning policy: `embedding_warning_policy=production-custom-alias-no-suppression`.
- Baseline gate: `embedding_baseline_eval_gate=lancedb-eval-11-of-11`.
- Runtime: Python embedded through `uv`.
- Cloud: disabled.

The old deterministic token-hash vectors remain available only as an explicit fallback/test provider.

SQLite remains authoritative for `current`, `proposed`, `superseded`, `failed`, `source_path`, and `source_hash`. LanceDB can copy that metadata for filtering, freshness checks, and reranking, but it cannot own or mutate canonical status.

## Current Tooling

- Wrapper: `scripts/lancedb-sidecar.ps1`.
- Python script: `tools/MemorySemantic/lancedb_sidecar.py`.
- Store path: `docs/memory/generated/lancedb`.
- Eval JSON report path: `docs/memory/generated/lancedb-sidecar-report.json`.
- Eval Markdown report path: `docs/memory/generated/lancedb-eval-report.md`.
- Diagnostic JSON report paths: `docs/memory/generated/lancedb-probe-report.json`, `lancedb-search-report.json`, `lancedb-explain-report.json`, `lancedb-cleanup-report.json`, and `lancedb-rebuild-report.json`.
- Commands: `probe`, `rebuild`, `search`, `explain`, `eval`, and `cleanup`.
- Runtime mode: local Python embedded through `uv`; no Cloud, no service account, no OpenAI key, no Codex auto-retain.
- Dependency doctor: `scripts/memory-semantic-doctor.ps1`.

The sidecar now uses local FastEmbed/ONNX for semantic recall quality testing. The quality gate is explicit: `eval` must pass the required retrieval cases before any hook or background automation is considered. `eval` writes compact generated JSON and Markdown reports with query, expected ids/types, matched rank, source_path, confidence, and gap notes. Diagnostic commands write command-specific reports and must not overwrite the eval JSON evidence used by `memory-pre-push-check`.

`scripts/memory-semantic-doctor.ps1` is the dependency preflight. It reports the `uv` discovery path, dependency pins, cache policy, and whether the local offline runtime can import the pinned packages. On a fresh machine, `memory-semantic-doctor.ps1 -AllowNetworkPreflight` prepares packages and `lancedb-sidecar.ps1 -Command preflight -AllowNetworkPreflight` prepares the FastEmbed model cache. Normal rebuild/search/eval commands use `uv --offline`, `HF_HUB_OFFLINE=1`, and FastEmbed local-files-only loading. Hidden network downloads are therefore blocked inside `memory-refresh-all`, `memory-pre-push-check`, and hooks.

The local cache/venv policy is deliberately plain: `uv` may be discovered from `PATH`, `%APPDATA%/Python/Python312/Scripts/uv.exe`, or `%LOCALAPPDATA%/Microsoft/WinGet/Packages/**/uv.exe`; dependency and model caches must stay outside the repo; no project `.venv` is required for memory gates.

## Embedding Baseline

The accepted low-risk baseline is FastEmbed `0.8.0` with logical model `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` and explicit mean-pooling behavior. FastEmbed `TextEmbedding` emits a warning when the upstream model name is used directly because the package moved it from older CLS behavior to mean pooling. The production sidecar avoids warning suppression by registering a local FastEmbed custom-model alias, `tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean`, with `PoolingType.MEAN`. Reports keep the logical `embedding_model`, the actual `embedding_runtime_model`, `embedding_pooling=mean`, and `embedding_warning_policy=production-custom-alias-no-suppression`.

This baseline is current only while LanceDB `eval` passes `11/11`. Changing the package pin, model, pooling behavior, dimensions, or provider requires rerun cleanup/rebuild/eval, updating the generated JSON/Markdown eval reports, and updating this document before relying on the new semantic results.

## Guardrails

- Do not import raw JSONL recordings, generated exports, secrets, local proxy details, build artifacts, or unreviewed experiment dumps.
- Import only records exported from SQLite with source metadata and status.
- Do not rank LanceDB semantic matches above SQLite current records without a freshness check.
- Do not install a git post-commit hook or after-save auto-refresh until clean rebuild/delete/reindex behavior is proven.
- Keep LanceDB outside the WPF/.NET runtime, build, and application tests.
- Treat generated facts without `source_path`/`source_hash` as invalid and skip them.

## Clean Rebuild/Delete/Reindex

The preferred full manual path is:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh-all.ps1
```

For LanceDB-only diagnostics, the accepted clean rebuild/delete/reindex sequence is:

1. Refresh SQLite with `tools/Memory`.
2. Run `scripts/lancedb-sidecar.ps1 -Command cleanup`.
3. Run `scripts/lancedb-sidecar.ps1 -Command rebuild`.
4. Run `scripts/lancedb-sidecar.ps1 -Command search -Query "actual OFI formula"`.
5. Run `scripts/lancedb-sidecar.ps1 -Command explain -Query "actual OFI formula"`.
6. Run `scripts/lancedb-sidecar.ps1 -Command eval`.

No commit hook or background automation is allowed until this sequence is repeatable.

## Local Smoke Result

Date: 2026-06-29.

- `uv run --python 3.12 --with lancedb --with pyarrow ...` downloaded and ran local embedded LanceDB successfully.
- `rebuild` created `docs/memory/generated/lancedb` from SQLite and indexed `271` current/proposed records.
- `cleanup` deleted the generated LanceDB store with `deleted_existing_store=true`.
- A second `rebuild` recreated the store and indexed the same `271` records.
- `search "actual OFI formula"` returned `formula_version.tc-dn-hofi3.current` first after local typed/exact-token reranking.
- `explain "actual OFI formula"` returned LanceDB `explain_plan` and `analyze_plan`; the plan shows `KNNVectorDistance`, `LanceRead`, and `TopK`.

Update 2026-06-29:

- FastEmbed/ONNX candidate rebuild with `fastembed==0.8.0` and model `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` indexes the current/proposed SQLite record set; the ignored generated report records the exact per-run count.
- The dependency-stability baseline pins `lancedb==0.34.0`, `pyarrow==24.0.0`, and `fastembed==0.8.0`; the LanceDB gate path runs `uv --offline` so missing local cache is reported as a preflight problem instead of downloading during a gate.
- Indexed rows record `embedding_provider=fastembed`, `embedding_model=sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2`, `embedding_runtime_model=tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean`, `embedding_pooling=mean`, `embedding_dimensions=384`, and `embedding_package_version=0.8.0`.
- Reports record `embedding_package_pin=fastembed==0.8.0`, `embedding_pooling_baseline=mean-pooling`, `embedding_warning_policy=production-custom-alias-no-suppression`, `embedding_baseline_status=accepted-if-eval-passes`, and `embedding_baseline_eval_gate=lancedb-eval-11-of-11`.
- `search "actual OFI formula"` returned `formula_version.tc-dn-hofi3.current` first.
- `explain "actual OFI formula"` returned `KNNVectorDistance`, `LanceRead`, and `TopK`; the ignored generated report records the exact per-run scan count.
- The first FastEmbed `eval` passed `4/4`: current OFI formula, funding-source ADR, exchange adapter impact, and superseded-rule exclusion. This was a smoke baseline, not enough evidence for durable trust.
- The current OFI formula eval case caught a regression where a docs chunk describing the quality gate outranked the canonical `formula_version`; the reranker now applies query-aware typed bonuses so source-backed formula/ADR/relation records can beat self-referential memory docs.

Expanded quality gate update 2026-06-29:

- Added source-backed typed rules in `docs/memory/rules.md` so boundary/guardrail cases do not rely only on generic chunks.
- Rebuild indexed the current/proposed SQLite records into LanceDB; the ignored generated report records the exact count.
- `eval` passed `9/9`: current OFI formula, formula owner, funding-source ADR, Binance DTO boundary, REST hot-path ban, live/replay shared pipeline, funding slow context, exchange adapter impact, and superseded/failed exclusion.
- `eval` writes `docs/memory/generated/lancedb-sidecar-report.json` and `docs/memory/generated/lancedb-eval-report.md`; both are ignored generated evidence, not source-of-truth memory.
- `probe`, `search`, `explain`, `cleanup`, and `rebuild` write separate command-specific JSON reports so diagnostics cannot silently replace the latest eval evidence.
- `rebuild` is the only producer of `docs/memory/generated/lancedb-manifest.json`. The manifest binds the physical store to canonical `commit_sha`/`tree_sha`/`indexed_at`, `source_store=sqlite-fts5`, table name, non-negative `indexed_count`, and the exact embedding identity. Rebuild validates the physical row count before publishing it. Search, explain, and eval validate identity before opening the table, then validate `indexed_count == table.count_rows()` before embedding or query execution; manual pre-push and CI validate the same persisted contract after eval.

Retrieval quality update 2026-07-04:

- `eval` passed `11/11`: the previous 9 source-backed cases plus strict no-answer cases for historical-only and unrelated/low-confidence queries.
- Historical-only and low-confidence queries must return no results with gap notes rather than a random current fact.
- `search` and `explain` reports include `freshness_check`, `minimum_retrieval_confidence`, raw candidate count, returned count, and top-level `gap_notes`.
- Returned rows include source-backed freshness status and per-row gap notes. Rows with stale/incomplete source metadata or retrieval confidence below the threshold are rejected from the unified output.

## Semantic Quality Gate

Required `eval` cases:

- `current_ofi_formula`: return `formula_version.tc-dn-hofi3.current` at rank 1.
- `formula_owner`: return `formula_version.tc-dn-hofi3.current` at rank 1 for owner lookup.
- `funding_source_changed`: return `adr.0004-funding-source-context` within the accepted rank window.
- `binance_dto_boundary`: return `rule.binance-dto-boundary` within the accepted rank window.
- `rest_hot_path_ban`: return `rule.rest-hot-path-ban` within the accepted rank window.
- `live_replay_same_pipeline`: return `rule.live-replay-same-pipeline` within the accepted rank window.
- `funding_slow_context`: return `adr.0004-funding-source-context` within the accepted rank window.
- `exchange_adapter_impact`: return a `relation` sourced from `CryptoIndicatorApp.Infrastructure/Binance/`.
- `exclude_superseded_rule`: return no answer for historical-only retrieval instead of a random current result.
- `unknown_order_execution_approval`: return no answer when there is no approved source-backed project fact.
- `low_confidence_unrelated_query`: return no answer with low-confidence gap notes when the query is unrelated to current project memory.

Required eval report fields:

- Query text.
- Expected ids/types and source constraints.
- Matched rank and matched source_path.
- Matched confidence.
- Gap notes for failed or incomplete cases.
- FastEmbed provider/model/package metadata.
- `embedding_runtime_model`, `embedding_pooling`, `embedding_pooling_baseline`, `embedding_warning_policy`, `embedding_baseline_status`, and `embedding_baseline_eval_gate`.

## Limitations

- FastEmbed model files need an explicit first-run `preflight -AllowNetworkPreflight` download into an outside-repo model cache. Normal gates fail closed when that cache is missing.
- FastEmbed `0.8.0` warns when the upstream model name is used directly because this model now uses mean pooling instead of older CLS behavior. The production sidecar uses an explicit custom runtime alias with `PoolingType.MEAN` instead of suppressing the warning. The behavior is accepted only as the documented `mean-pooling` baseline while eval remains `11/11`, and changing it later requires a fresh eval baseline.
- Raw vector distance alone ranked generic chunks above the current formula record during the first smoke. The sidecar keeps a small local reranker that prefers typed records such as `formula_version` and ADRs over generic chunks when exact-token overlap is present.
- This does not replace SQLite FTS5 retrieval tests. It adds a semantic quality gate below SQLite status/source metadata.
