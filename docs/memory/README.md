# Project Memory

This folder prepares the project for a multi-layer memory workflow. SQLite FTS5 is the canonical local generated memory store and the only one. The LanceDB semantic sidecar was removed on 2026-10-21 (ADR 0010). Hindsight is now a historical/failed spike, and GBrain is kept only as a historical/secondary candidate.

The application runtime must not depend on these files or generated stores.

## Files

- `contract.md`: source priority, SQLite schema, node/edge schema, retrieval protocol, and staleness rules.
- `generated-memory.schema.json`: schema for generated memory indexes.
- `glossary.md`: stable project terms.
- `entities.md`: domain and architecture entities that graph tools may later ingest.
- `rules.md`: typed current/superseded project rules used by SQLite quality gates.
- `retain-policy.md`: curated retain lifecycle gates, including redaction, delete/export policy, allowlist, and denylist.
- `operations-runbook.md`: short operator flow for daily checks, commit refresh, push/PR gates, recovery, clone-like proof, and `/compact`.
- `project-map.md`: high-level module map.
- `hindsight-spike.md`: confirmed upstream Hindsight surface and why it is now historical/failed for this MVP.
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

This writes `docs/memory/generated/memory-daily-check-report.json` and reports the current branch, `HEAD`, indexed commit when the generated SQLite store already exists, `needs_refresh`, marker status, and generated report presence. If the Memory CLI is unavailable, the report says `CLI unavailable` and leaves `needs_refresh unknown` instead of pretending memory is stale. It does not run `memory-refresh-all`, rebuild memory, import curated retain, install hooks, call Cloud, call Hindsight, or call Codex retain.

Run the full manual memory refresh sequence:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh-all.ps1
```

This runs legacy JSON refresh, SQLite refresh from commit (`refresh-from-commit --commit HEAD`), and SQLite stale-check in order. It writes an ignored wrapper report to `docs/memory/generated/memory-refresh-all-report.json`. It does not install hooks, enable Codex auto-retain, use Cloud, or import raw JSONL/generated exports/secrets/local proxy details/build artifacts.

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

The helper validates the generated refresh report and writes `docs/memory/generated/memory-pre-push-check-report.json`. It does not rebuild memory by default, install hooks, enable post-commit automation, or treat generated exports as source memory.

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

The optional hook calls `scripts/memory-mark-needs-refresh.ps1` only. It writes `docs/memory/generated/memory-needs-refresh.marker.json` after a commit and does not run rebuild, `memory-refresh-all`, curated retain, Cloud, or Codex auto-retain. `-TimeoutSeconds` must be positive, and marker reports include the lock path used for coordination. Local validation should use a custom temporary `-HookPath`/`-OutputPath`; the generated installer report records `targets_default_repo_hook`, `custom_hook_path`, and `actual_repo_hook_touched` so reviewers can confirm the real `.git/hooks/post-commit` was not touched. Disable it with:

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
