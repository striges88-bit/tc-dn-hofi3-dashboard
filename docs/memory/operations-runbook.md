# Memory Operations Runbook

This is the normal operator flow for project memory. All commands are manual. Do not enable Codex auto-retain, post-commit rebuild, Cloud retain, raw JSONL import, secrets import, or background refresh.

## Daily Snapshot

Run this first when you want a cheap status check:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-daily-check.ps1 -PlanOnly
```

Good result: the report is written, `needs_refresh=false` when memory is already current, and LanceDB eval is still the latest `11/11` evidence if present.

If the Memory CLI is unavailable because restore/build prerequisites or NuGet locks are broken, the report should say `CLI unavailable` and `needs_refresh unknown`. Treat that as an environment/tooling issue to fix, not as proof that memory is stale.

## After A Durable Commit

If the commit changed ADRs, formulas, memory docs, lessons, scripts, tests, or architecture rules, refresh from committed `HEAD`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh-all.ps1
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- status --project-root . --json
```

Good result: `needs_refresh=false`, `working_tree_dirty=false` unless you intentionally started the next source edit, SQLite stale-check has no issues, and LanceDB eval is `11/11`.

## Semantic Dependency Doctor

Run this before a LanceDB rebuild/eval on a fresh machine, after Python/uv repair, or after dependency-cache cleanup:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-semantic-doctor.ps1
```

Good result: `status=ok`, `lancedb==0.34.0`, `pyarrow==24.0.0`, `fastembed==0.8.0`, cache/model paths outside the repo, and offline runtime checks pass. Memory gates use `uv --offline`, so missing dependencies should fail as a preflight issue instead of downloading in the background.

## Shell Compatibility

Checked-in scripts remain Windows PowerShell 5.1-compatible and the documented gate commands use `powershell.exe`. PowerShell 7.6.3 is optional for interactive use; it is not required by the scripts or CI. When diagnosing a compatibility issue, rerun the same command with `powershell.exe` before treating it as a memory-system failure.

## Troubleshooting

### `uv-unavailable` Or Missing Semantic Cache

Check discovery without changing memory:

```powershell
Get-Command uv.exe -All -ErrorAction SilentlyContinue
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-semantic-doctor.ps1 -PlanOnly
```

On a fresh machine, dependency and model downloads must be explicit. First prepare the pinned Python packages, then prepare the FastEmbed model cache, and finally prove that the normal path works offline:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-semantic-doctor.ps1 -AllowNetworkPreflight
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command preflight -AllowNetworkPreflight
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-semantic-doctor.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command eval
```

The preflight commands are the only semantic commands allowed to use the network. Normal rebuild/search/eval commands force both uv packages and FastEmbed model access offline.

### Memory CLI Unavailable Or NuGet/MSBuild Lock

Check for an existing build/test process before starting another one:

```powershell
Get-Process dotnet,msbuild,vstest.console -ErrorAction SilentlyContinue
```

Let the owning command finish or close its terminal, then restore and build serially:

```powershell
.\.dotnet\dotnet.exe restore CryptoIndicatorApp.sln --disable-parallel
.\.dotnet\dotnet.exe build CryptoIndicatorApp.sln --no-restore
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- status --project-root . --json
```

Do not infer `needs_refresh=true` from a NuGet lock, inaccessible MSBuild temp directory, or `CLI unavailable`; freshness is unknown until the CLI runs successfully.

### Probe Versus Eval Reports

`probe` checks configuration and writes `docs/memory/generated/lancedb-probe-report.json`. It does not run retrieval and is not quality evidence:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command probe
```

`eval` runs the 11-case semantic quality gate and writes `lancedb-sidecar-report.json` plus `lancedb-eval-report.md`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\lancedb-sidecar.ps1 -Command eval
```

`memory-pre-push-check.ps1` reads only the eval reports. A fresh probe must never overwrite or substitute for eval evidence.

## CI Order

CI keeps failure causes separated:

1. `.NET build and tests` verifies the application and tooling.
2. `Lightweight canonical memory` runs Memory CLI status, commit-addressed SQLite refresh, stale-check, and final status without Python/LanceDB.
3. `Cached semantic memory quality` runs only after the lightweight job. It restores pinned uv and FastEmbed caches, performs the two explicit preflights, proves offline availability, rebuilds LanceDB from SQLite, and runs eval.

CI does not run `memory-refresh-all`, install hooks, import curated retain data, call Cloud, or enable Codex auto-retain. A semantic dependency/cache failure therefore cannot be reported as SQLite staleness.

## Before Push Or PR

Run the manual evidence gate:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-pre-push-check.ps1
```

Good result: `status=passed`, every item in `checks` is passed, the LanceDB eval detail reports `passed_count=11` and `failed_count=0`, no Cloud, no hooks installed by the command, and no generated exports used as source.

## Controlled Local Retain Lifecycle

Canonical refresh uses `project-memory.sqlite`; the commands below use the separate `project-retained.sqlite` by default. Do not pass `--db` during the normal lifecycle.

After reviewing a schema-v2 redacted subset, import it from committed `HEAD`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-import.ps1 -InputReportPath docs\memory\generated\curated-retain-redacted-subset-report.json -Commit HEAD
```

Find a unique retained phrase, export the complete local retained set, then delete the selected source:

```powershell
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- retain-search --project-root . --query "unique retained phrase" --json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-export.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-delete.ps1 -SourcePath docs/memory/example.md
```

Repeat `retain-search` with the same unique phrase. Lifecycle proof is complete only when the result is absent, while the source file still exists. Re-importing the same source path replaces its prior searchable version; it does not retain both versions.

On upgrade, the first default retain command migrates legacy rows from `project-memory.sqlite` into the isolated store. If it reports that both stores contain rows, no data was deleted. Diagnose each store explicitly before deciding what to re-import or delete:

```powershell
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- retain-search --project-root . --db docs\memory\generated\project-memory.sqlite --query "known legacy phrase" --json
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- retain-search --project-root . --db docs\memory\generated\project-retained.sqlite --query "known retained phrase" --json
```

Do not resolve a two-store conflict by deleting either SQLite file. Export the required rows, regenerate a reviewed subset from committed sources, and use normal retain import/delete commands.

## Local Recovery

Use this when local generated memory looks corrupt or stale:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-rebuild-from-head.ps1 -PlanOnly
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-rebuild-from-head.ps1
```

Good result: only allowlisted generated memory artifacts under `docs/memory/generated/` are deleted and recreated; source files, raw JSONL, secrets, `.hindsight/`, `bin/`, `obj/`, `publish/`, hooks, Cloud data, and Codex memory are not touched.

## Clone-Like Recovery Proof

Use this after a recovery or before trusting a fresh machine setup:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-clone-recovery-check.ps1 -PlanOnly
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-clone-recovery-check.ps1
```

Good result: the real run starts from a clean working tree, clones the committed `HEAD` to a temporary directory, runs `memory-rebuild-from-head.ps1` inside the clone, verifies clone `memory status` with `needs_refresh=false`, then deletes the temporary clone unless `-KeepClone` is passed.

## Before `/compact`

Stop at a clean handoff point:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-daily-check.ps1 -PlanOnly
.\.dotnet\dotnet.exe run --no-restore --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- status --project-root . --json
```

If source changes are already committed and `needs_refresh=true`, run `memory-refresh-all.ps1`. If source work is mid-slice, do not force refresh; update `tasks/todo.md` with the next command and compact there.

## Never Do By Default

- Do not enable Codex auto-retain.
- Do not add a post-commit rebuild hook.
- Do not import raw JSONL, raw dumps, secrets, local proxy details, generated exports, `.hindsight/`, `bin/`, `obj/`, or `publish/`.
- Do not treat generated reports as source of truth; they are evidence only.
- Do not run refresh before committing durable source changes that should be indexed.
