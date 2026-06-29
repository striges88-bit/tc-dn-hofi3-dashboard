# Project Memory

This folder prepares the project for a multi-layer memory workflow. SQLite FTS5 is the canonical local generated memory store. LanceDB is an active local semantic sidecar and production-candidate semantic quality layer below SQLite. Hindsight is now a historical/failed spike, and GBrain is kept only as a historical/secondary candidate.

The application runtime must not depend on these files or generated stores.

## Files

- `contract.md`: source priority, SQLite schema, node/edge schema, retrieval protocol, and staleness rules.
- `generated-memory.schema.json`: schema for generated memory indexes.
- `glossary.md`: stable project terms.
- `entities.md`: domain and architecture entities that graph tools may later ingest.
- `rules.md`: typed current/superseded project rules used by SQLite/LanceDB quality gates.
- `project-map.md`: high-level module map.
- `hindsight-spike.md`: confirmed upstream Hindsight surface and why it is now historical/failed for this MVP.
- `lancedb-spike.md`: active local semantic sidecar rules and spike gates.
- `hindsight-install-spike.md`: selected Python/uvx embedded daemon install-spike path and safety gates.
- `gbrain-spike.md`: confirmed upstream GBrain CLI/API surface and current local availability.
- `open-questions.md`: unresolved questions that should not be silently encoded as facts.

Generated graph, SQLite, or memory exports belong in `docs/memory/generated/`, which is ignored by Git. Use `scripts/memory-refresh-all.ps1` for a full local rebuild, `tools/Memory` for the canonical local SQLite store, and `scripts/memory-refresh.ps1` only for the legacy JSON refresh report.

## Commands

Run the full manual memory refresh sequence:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh-all.ps1
```

This runs legacy JSON refresh, SQLite refresh, SQLite stale-check, LanceDB cleanup, LanceDB rebuild, and LanceDB `eval` in order. It writes an ignored report to `docs/memory/generated/memory-refresh-all-report.json`. It does not install hooks, enable Codex auto-retain, use Cloud, crawl project files directly for LanceDB, or import raw JSONL/generated exports/secrets/local proxy details/build artifacts.

Refresh the local generated index:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh.ps1
```

Refresh the canonical SQLite FTS5 memory store:

```powershell
.\.dotnet\dotnet.exe run --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- refresh --project-root . --json
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

Generate the pre-install Hindsight curated import manifest without calling Hindsight:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\hindsight-curated-import.ps1
```

Generate the safe Hindsight install-spike report without installing packages or starting daemons:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\hindsight-install-spike.ps1
```

Run the retrieval/staleness contract tests:

```powershell
.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter MemoryContractTests
```
