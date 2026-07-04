# Project Memory

This folder prepares the project for a multi-layer memory workflow. SQLite FTS5 is the canonical local generated memory store. LanceDB is an active local semantic sidecar and production-candidate semantic quality layer below SQLite. Hindsight is now a historical/failed spike, and GBrain is kept only as a historical/secondary candidate.

The application runtime must not depend on these files or generated stores.

## Files

- `contract.md`: source priority, SQLite schema, node/edge schema, retrieval protocol, and staleness rules.
- `generated-memory.schema.json`: schema for generated memory indexes.
- `glossary.md`: stable project terms.
- `entities.md`: domain and architecture entities that graph tools may later ingest.
- `rules.md`: typed current/superseded project rules used by SQLite/LanceDB quality gates.
- `retain-policy.md`: curated retain lifecycle gates, including redaction, delete/export policy, allowlist, and denylist.
- `operations-runbook.md`: short operator flow for daily checks, commit refresh, push/PR gates, recovery, clone-like proof, and `/compact`.
- `project-map.md`: high-level module map.
- `hindsight-spike.md`: confirmed upstream Hindsight surface and why it is now historical/failed for this MVP.
- `lancedb-spike.md`: active local semantic sidecar rules and spike gates.
- `hindsight-install-spike.md`: selected Python/uvx embedded daemon install-spike path and safety gates.
- `gbrain-spike.md`: confirmed upstream GBrain CLI/API surface and current local availability.
- `open-questions.md`: unresolved questions that should not be silently encoded as facts.

Generated graph, SQLite, or memory exports belong in `docs/memory/generated/`, which is ignored by Git. Use `docs/memory/operations-runbook.md` for the short operator flow, `scripts/memory-refresh-all.ps1` for a full local rebuild from `HEAD`, `scripts/memory-rebuild-from-head.ps1` when local generated memory artifacts must be deleted and recreated from committed sources, `tools/Memory` for the canonical local SQLite store, and `scripts/memory-refresh.ps1` only for the legacy JSON refresh report.

The SQLite code-memory layer indexes C# source as typed facts in addition to generic chunks. It extracts namespace/type/method symbols, `owns` relations, xUnit test-method events, `requires_symbol=` references for stale-check, `TODO` markers, and `experiment_outcome=` notes. This stays tooling-only; it does not add a WPF runtime dependency or require MSBuild workspace loading.

## Commands

Memory CLI examples use `dotnet run --no-restore`. Run a normal restore/build first on a fresh checkout; operator checks should not hide NuGet restore or lock problems inside memory status.

Run a read-only operator snapshot before deciding whether a heavier refresh/check is needed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-daily-check.ps1 -PlanOnly
```

This writes `docs/memory/generated/memory-daily-check-report.json` and reports the current branch, `HEAD`, indexed commit when the generated SQLite store already exists, `needs_refresh`, marker status, latest LanceDB eval status, and generated report presence. If the Memory CLI is unavailable, the report says `CLI unavailable` and leaves `needs_refresh unknown` instead of pretending memory is stale. It does not run `memory-refresh-all`, rebuild memory, import curated retain, install hooks, call Cloud, call Hindsight, or call Codex retain.

Run the full manual memory refresh sequence:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh-all.ps1
```

This runs legacy JSON refresh, SQLite refresh from commit (`refresh-from-commit --commit HEAD`), SQLite stale-check, LanceDB cleanup, LanceDB rebuild, and LanceDB `eval` in order. It writes an ignored wrapper report to `docs/memory/generated/memory-refresh-all-report.json`; LanceDB diagnostic steps write command-specific generated JSON reports, and the LanceDB eval step writes `docs/memory/generated/lancedb-sidecar-report.json` plus `docs/memory/generated/lancedb-eval-report.md`. It does not install hooks, enable Codex auto-retain, use Cloud, crawl project files directly for LanceDB, or import raw JSONL/generated exports/secrets/local proxy details/build artifacts.

Rebuild local generated memory artifacts from committed `HEAD` after local store corruption or recovery testing:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-rebuild-from-head.ps1
```

Use `-PlanOnly` first to review the delete plan. The wrapper may delete only allowlisted generated memory artifacts under `docs/memory/generated/`, then runs `scripts/memory-refresh-all.ps1` and checks that `memory status` ends with `needs_refresh=false`. It does not delete source files, raw JSONL, secrets, `.hindsight/`, `bin/`, `obj/`, `publish/`, hooks, Cloud data, Codex memory, or external retain data.

Prove the same recovery behavior in a fresh clone-like checkout:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-clone-recovery-check.ps1 -PlanOnly
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-clone-recovery-check.ps1
```

The wrapper requires a clean working tree for real runs, clones the current committed `HEAD` to a temporary directory, runs the clone's `scripts/memory-rebuild-from-head.ps1`, verifies clone `memory status needs_refresh=false`, and deletes the temporary clone unless `-KeepClone` is passed. It writes `docs/memory/generated/memory-clone-recovery-check-report.json` and does not install hooks, call Cloud, enable Codex auto-retain, import raw JSONL, import generated exports, import secrets, or touch build artifacts.

Run the manual pre-push evidence gate after `memory-refresh-all`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-pre-push-check.ps1
```

The helper validates the generated refresh/eval reports and writes `docs/memory/generated/memory-pre-push-check-report.json`. It does not rebuild memory by default, install hooks, enable post-commit automation, or treat generated exports as source memory.

Optionally install a local managed `pre-push` hook after reviewing the manual gate:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\install-memory-pre-push-hook.ps1 -Confirm
```

The optional hook calls `scripts/memory-pre-push-check.ps1` only. It does not run `memory-refresh-all`, rebuild memory, install itself automatically, or add post-commit automation. Disable the managed hook with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\install-memory-pre-push-hook.ps1 -Disable -Confirm
```

Optionally install a local managed `post-commit` marker hook:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\install-memory-post-commit-marker-hook.ps1 -Confirm
```

The optional hook calls `scripts/memory-mark-needs-refresh.ps1` only. It writes `docs/memory/generated/memory-needs-refresh.marker.json` after a commit and does not run rebuild, `memory-refresh-all`, LanceDB, curated retain, Cloud, or Codex auto-retain. `-TimeoutSeconds` must be positive, and marker reports include the lock path used for coordination. Local validation should use a custom temporary `-HookPath`/`-OutputPath`; the generated installer report records `targets_default_repo_hook`, `custom_hook_path`, and `actual_repo_hook_touched` so reviewers can confirm the real `.git/hooks/post-commit` was not touched. Disable it with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\install-memory-post-commit-marker-hook.ps1 -Disable -Confirm
```

Refresh the local generated index:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh.ps1
```

Refresh the canonical SQLite FTS5 memory store:

```powershell
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- refresh --project-root . --json
```

Refresh the canonical SQLite FTS5 memory store from the current Git commit:

```powershell
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- refresh-from-commit --commit HEAD --project-root . --json
```

Check whether generated memory is stale against `HEAD`:

```powershell
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- status --project-root . --json
```

Search current memory facts:

```powershell
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- search --project-root . --query "actual OFI formula" --json
```

Explain a SQLite query plan and write `query_log`:

```powershell
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- explain --project-root . --query "actual OFI formula" --json
```

Run stale checks:

```powershell
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- stale-check --project-root . --json
```

Probe the local LanceDB sidecar without installing hooks, importing raw files, or using Cloud:

Script path: `scripts/lancedb-sidecar.ps1`.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command probe
```

The default probe report is `docs/memory/generated/lancedb-probe-report.json`; it must not overwrite `docs/memory/generated/lancedb-sidecar-report.json`, which is reserved for eval evidence.

Check local semantic dependency readiness before a rebuild/eval gate:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-semantic-doctor.ps1
```

Use `-PlanOnly` for a report-only preview. The doctor records `uv` discovery, `lancedb==0.34.0`, `pyarrow==24.0.0`, `fastembed==0.8.0`, cache/venv policy, and whether the pinned runtime is available offline. It does not rebuild memory, import retain data, install hooks, call Cloud, or call Codex retain. Memory gates use `uv --offline`; if the local cache is missing, run an explicit dependency preflight instead of letting `memory-refresh-all` download packages or models in the background.

Rebuild, query, and evaluate the local LanceDB sidecar from SQLite `search_documents` only:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command rebuild
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command search -Query "actual OFI formula"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command explain -Query "actual OFI formula"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command eval
```

The LanceDB candidate uses local FastEmbed/ONNX embeddings by default with logical model `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` through pinned `lancedb==0.34.0`, `pyarrow==24.0.0`, and `fastembed==0.8.0`, plus typed/exact-token reranking and an explicit `eval` gate. The production runtime registers the explicit custom alias `embedding_runtime_model=tc-dn-hofi3/paraphrase-multilingual-MiniLM-L12-v2-mean` with `embedding_pooling=mean` instead of suppressing FastEmbed's upstream mean-pooling warning. The accepted semantic baseline records `embedding_pooling_baseline=mean-pooling` and `embedding_warning_policy=production-custom-alias-no-suppression`; it is current only while LanceDB `eval` passes `11/11`. The old token-hash provider is fallback/test-only.

The generated eval reports are evidence artifacts for hook/automation review. They include query, expected ids/types, matched rank, source path, confidence, no-answer/low-confidence cases, and gap notes, but they are not a source of truth. `search` and `explain` reports also include `freshness_check`, `minimum_retrieval_confidence`, and top-level `gap_notes`; a low-confidence query should return no results with a gap note instead of a random current fact.

Non-eval LanceDB commands write command-specific generated JSON reports (`lancedb-search-report.json`, `lancedb-explain-report.json`, `lancedb-cleanup-report.json`, and `lancedb-rebuild-report.json`) so diagnostics cannot silently replace the latest eval evidence.

Generate the pre-install Hindsight curated import manifest without calling Hindsight:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\hindsight-curated-import.ps1
```

Generate the provider-neutral curated retain dry-run and redaction report without calling Hindsight, Cloud, Codex retain, hooks, or memory rebuild:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-dry-run.ps1
```

The reports are written to ignored generated files:

- `docs/memory/generated/curated-retain-dry-run-report.json`
- `docs/memory/generated/curated-retain-dry-run-report.md`

They list only allowlisted source candidates and redaction findings. Findings include severity (`critical`, `review`, `info`), type counts, severity counts, de-duplication, and `policy_reference` markers so policy docs do not look like real leaked secrets. These reports are review evidence, not import or retain operations.

Generate provider-neutral export/delete lifecycle dry-runs:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-export-dry-run.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-delete-dry-run.ps1
```

These reports are written to ignored generated files:

- `docs/memory/generated/curated-retain-export-dry-run-report.json`
- `docs/memory/generated/curated-retain-export-dry-run-report.md`
- `docs/memory/generated/curated-retain-delete-dry-run-report.json`
- `docs/memory/generated/curated-retain-delete-dry-run-report.md`

The export dry-run records source metadata and hash freshness only; it does not include source text. The delete dry-run writes a deletion plan only; it does not remove source files, generated reports, local stores, hooks, provider data, or retained items. Missing, stale, denylisted, or redaction-review reports keep external retain and Codex auto-retain disabled.

Generate a reviewed local redacted subset for explicitly selected allowlisted sources:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-redacted-subset.ps1 -SourcePath docs/memory/retain-policy.md
```

This reads the dry-run report, rejects sources changed since that dry-run, keeps the original source hash for Git commit verification, replaces risky lines with `[REDACTED:<finding-types>]`, and writes ignored JSON/Markdown reports under `docs/memory/generated/`. It does not import, retain, rebuild, install hooks, call Cloud, or call Codex retain.

Run controlled local retain import only after reviewing the dry-run report:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-import.ps1 -InputReportPath docs\memory\generated\curated-retain-redacted-subset-report.json -Commit HEAD
```

This writes `docs/memory/generated/curated-retain-import-report.json` and imports only allowlisted clean candidates or reviewed redacted entries into local SQLite. Candidate text is read from the selected Git commit tree, and redacted entries store reviewed `redacted_text` while verifying the original source hash against the selected commit. A blocked report is expected while unreviewed redaction findings remain.

Export or delete locally retained rows for lifecycle proof:

```powershell
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- retain-search --project-root . --query "controlled local import into SQLite" --json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-export.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-delete.ps1 -SourcePath docs/memory/retain-policy.md
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- retain-search --project-root . --query "controlled local import into SQLite" --json
```

Delete removes only local SQLite retained rows for the selected source path. It does not remove repository files.

Review curated retain policy before any external retain or Codex auto-retain:

```text
docs/memory/retain-policy.md
```

External retain and Codex auto-retain stay disabled until redaction before retain, delete/export policy, allowlist, denylist, and dry-run reports are tested. The post-commit marker hook remains marker-only and does not run rebuild, retain, Cloud, or `memory-refresh-all`.

Generate the safe Hindsight install-spike report without installing packages or starting daemons:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\hindsight-install-spike.ps1
```

Run the retrieval/staleness contract tests:

```powershell
.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter MemoryContractTests
```
