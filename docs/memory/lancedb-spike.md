# LanceDB Semantic Sidecar Spike

Status: active production-candidate semantic quality layer. This replaces the earlier token-hash-only spike, but LanceDB is still not a canonical store.

## Decision

Evaluate LanceDB as a local Python embedded semantic sidecar under `docs/memory/generated/lancedb`. The sidecar reads only canonical SQLite `search_documents` records with `status IN ('current', 'proposed')`, valid `source_path`, and valid `source_hash`. It must not crawl project files directly.

The current production-candidate semantic quality layer uses local FastEmbed/ONNX embeddings by default:

- Provider: `fastembed`.
- Model: `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2`.
- Runtime model: `embedding_runtime_model=tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean`.
- Package pin: `fastembed==0.8.0`.
- Pooling: `embedding_pooling=mean`.
- Pooling baseline: `embedding_pooling_baseline=mean-pooling`.
- Warning policy: `embedding_warning_policy=production-custom-alias-no-suppression`.
- Baseline gate: `embedding_baseline_eval_gate=lancedb-eval-9-of-9`.
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

The sidecar now uses local FastEmbed/ONNX for semantic recall quality testing. The quality gate is explicit: `eval` must pass the required retrieval cases before any hook or background automation is considered. `eval` writes compact generated JSON and Markdown reports with query, expected ids/types, matched rank, source_path, confidence, and gap notes. Diagnostic commands write command-specific reports and must not overwrite the eval JSON evidence used by `memory-pre-push-check`.

## Embedding Baseline

The accepted low-risk baseline is FastEmbed `0.8.0` with logical model `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` and explicit mean-pooling behavior. FastEmbed `TextEmbedding` emits a warning when the upstream model name is used directly because the package moved it from older CLS behavior to mean pooling. The production sidecar avoids warning suppression by registering a local FastEmbed custom-model alias, `tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean`, with `PoolingType.MEAN`. Reports keep the logical `embedding_model`, the actual `embedding_runtime_model`, `embedding_pooling=mean`, and `embedding_warning_policy=production-custom-alias-no-suppression`.

This baseline is current only while LanceDB `eval` passes `9/9`. Changing the package pin, model, pooling behavior, dimensions, or provider requires rerun cleanup/rebuild/eval, updating the generated JSON/Markdown eval reports, and updating this document before relying on the new semantic results.

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
- Indexed rows record `embedding_provider=fastembed`, `embedding_model=sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2`, `embedding_runtime_model=tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean`, `embedding_pooling=mean`, `embedding_dimensions=384`, and `embedding_package_version=0.8.0`.
- Reports record `embedding_package_pin=fastembed==0.8.0`, `embedding_pooling_baseline=mean-pooling`, `embedding_warning_policy=production-custom-alias-no-suppression`, `embedding_baseline_status=accepted-if-eval-passes`, and `embedding_baseline_eval_gate=lancedb-eval-9-of-9`.
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
- `exclude_superseded_rule`: do not return `superseded` or `failed` facts for current retrieval.

Required eval report fields:

- Query text.
- Expected ids/types and source constraints.
- Matched rank and matched source_path.
- Matched confidence.
- Gap notes for failed or incomplete cases.
- FastEmbed provider/model/package metadata.
- `embedding_runtime_model`, `embedding_pooling`, `embedding_pooling_baseline`, `embedding_warning_policy`, `embedding_baseline_status`, and `embedding_baseline_eval_gate`.

## Limitations

- FastEmbed model files may need a first-run local download into the Python/model cache. This is still local embedded execution, but it is not zero-install.
- FastEmbed `0.8.0` warns when the upstream model name is used directly because this model now uses mean pooling instead of older CLS behavior. The production sidecar uses an explicit custom runtime alias with `PoolingType.MEAN` instead of suppressing the warning. The behavior is accepted only as the documented `mean-pooling` baseline while eval remains `9/9`, and changing it later requires a fresh eval baseline.
- Raw vector distance alone ranked generic chunks above the current formula record during the first smoke. The sidecar keeps a small local reranker that prefers typed records such as `formula_version` and ADRs over generic chunks when exact-token overlap is present.
- This does not replace SQLite FTS5 retrieval tests. It adds a semantic quality gate below SQLite status/source metadata.
