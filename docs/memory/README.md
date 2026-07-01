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
- `project-map.md`: high-level module map.
- `hindsight-spike.md`: confirmed upstream Hindsight surface and why it is now historical/failed for this MVP.
- `lancedb-spike.md`: active local semantic sidecar rules and spike gates.
- `hindsight-install-spike.md`: selected Python/uvx embedded daemon install-spike path and safety gates.
- `gbrain-spike.md`: confirmed upstream GBrain CLI/API surface and current local availability.
- `open-questions.md`: unresolved questions that should not be silently encoded as facts.

Generated graph, SQLite, or memory exports belong in `docs/memory/generated/`, which is ignored by Git. Use `scripts/memory-refresh-all.ps1` for a full local rebuild from `HEAD`, `tools/Memory` for the canonical local SQLite store, and `scripts/memory-refresh.ps1` only for the legacy JSON refresh report.

## Commands

Run the full manual memory refresh sequence:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh-all.ps1
```

This runs legacy JSON refresh, SQLite refresh from commit (`refresh-from-commit --commit HEAD`), SQLite stale-check, LanceDB cleanup, LanceDB rebuild, and LanceDB `eval` in order. It writes an ignored wrapper report to `docs/memory/generated/memory-refresh-all-report.json`; the LanceDB eval step also writes `docs/memory/generated/lancedb-sidecar-report.json` and `docs/memory/generated/lancedb-eval-report.md`. It does not install hooks, enable Codex auto-retain, use Cloud, crawl project files directly for LanceDB, or import raw JSONL/generated exports/secrets/local proxy details/build artifacts.

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

The optional hook calls `scripts/memory-mark-needs-refresh.ps1` only. It writes `docs/memory/generated/memory-needs-refresh.marker.json` after a commit and does not run rebuild, `memory-refresh-all`, LanceDB, curated retain, Cloud, or Codex auto-retain. Disable it with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\install-memory-post-commit-marker-hook.ps1 -Disable -Confirm
```

Refresh the local generated index:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh.ps1
```

Refresh the canonical SQLite FTS5 memory store:

```powershell
.\.dotnet\dotnet.exe run --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- refresh --project-root . --json
```

Refresh the canonical SQLite FTS5 memory store from the current Git commit:

```powershell
.\.dotnet\dotnet.exe run --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- refresh-from-commit --commit HEAD --project-root . --json
```

Check whether generated memory is stale against `HEAD`:

```powershell
.\.dotnet\dotnet.exe run --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- status --project-root . --json
```

Search current memory facts:

```powershell
.\.dotnet\dotnet.exe run --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- search --project-root . --query "actual OFI formula" --json
```

Explain a SQLite query plan and write `query_log`:

```powershell
.\.dotnet\dotnet.exe run --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- explain --project-root . --query "actual OFI formula" --json
```

Run stale checks:

```powershell
.\.dotnet\dotnet.exe run --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- stale-check --project-root . --json
```

Probe the local LanceDB sidecar without installing hooks, importing raw files, or using Cloud:

Script path: `scripts/lancedb-sidecar.ps1`.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command probe
```

Rebuild, query, and evaluate the local LanceDB sidecar from SQLite `search_documents` only:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command rebuild
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command search -Query "actual OFI formula"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command explain -Query "actual OFI formula"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command eval
```

The LanceDB candidate uses local FastEmbed/ONNX embeddings by default with model `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` through pinned `fastembed==0.8.0`, plus typed/exact-token reranking and an explicit `eval` gate. The old token-hash provider is fallback/test-only.

The generated eval reports are evidence artifacts for hook/automation review. They include query, expected ids/types, matched rank, source path, confidence, and gap notes, but they are not a source of truth.

Generate the pre-install Hindsight curated import manifest without calling Hindsight:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\hindsight-curated-import.ps1
```

Generate the provider-neutral curated retain dry-run and redaction report without calling Hindsight, Cloud, Codex retain, hooks, or memory rebuild:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-dry-run.ps1
```

The report is written to ignored `docs/memory/generated/curated-retain-dry-run-report.json`. It lists only allowlisted source candidates and redaction findings; it is review evidence, not an import or retain operation.

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
