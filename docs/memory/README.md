# Project Memory

This folder prepares the project for a future multi-layer memory workflow. The current preferred external semantic memory candidate is Hindsight, with GBrain kept only as a historical/secondary candidate and Graphify still unverified for code graph export.

For now, it is documentation only. The application runtime must not depend on these files.

## Files

- `contract.md`: source priority, node/edge schema, retrieval protocol, and staleness rules.
- `generated-memory.schema.json`: schema for generated memory indexes.
- `glossary.md`: stable project terms.
- `entities.md`: domain and architecture entities that graph tools may later ingest.
- `project-map.md`: high-level module map.
- `hindsight-spike.md`: confirmed upstream Hindsight surface, local availability, curated import policy, and MVP restrictions.
- `gbrain-spike.md`: confirmed upstream GBrain CLI/API surface and current local availability.
- `open-questions.md`: unresolved questions that should not be silently encoded as facts.

Generated graph or memory exports belong in `docs/memory/generated/`, which is ignored by Git until an export policy is approved. Use `scripts/memory-refresh.ps1` for the current manual refresh spike.

## Commands

Refresh the local generated index:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh.ps1
```

Run the retrieval/staleness contract tests:

```powershell
.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter MemoryContractTests
```
