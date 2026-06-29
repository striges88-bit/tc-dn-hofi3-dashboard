# Project Memory

This folder prepares the project for a multi-layer memory workflow. SQLite FTS5 is the canonical local generated memory store. LanceDB is the deferred semantic sidecar. Hindsight is now a historical/failed spike, and GBrain is kept only as a historical/secondary candidate.

For now, it is documentation only. The application runtime must not depend on these files.

## Files

- `contract.md`: source priority, SQLite schema, node/edge schema, retrieval protocol, and staleness rules.
- `generated-memory.schema.json`: schema for generated memory indexes.
- `glossary.md`: stable project terms.
- `entities.md`: domain and architecture entities that graph tools may later ingest.
- `project-map.md`: high-level module map.
- `hindsight-spike.md`: confirmed upstream Hindsight surface and why it is now historical/failed for this MVP.
- `lancedb-spike.md`: deferred semantic sidecar rules and spike gates.
- `hindsight-install-spike.md`: selected Python/uvx embedded daemon install-spike path and safety gates.
- `gbrain-spike.md`: confirmed upstream GBrain CLI/API surface and current local availability.
- `open-questions.md`: unresolved questions that should not be silently encoded as facts.

Generated graph, SQLite, or memory exports belong in `docs/memory/generated/`, which is ignored by Git. Use `tools/Memory` for the canonical local SQLite store and `scripts/memory-refresh.ps1` for the legacy JSON refresh report.

## Commands

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
