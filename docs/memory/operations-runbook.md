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

## Before Push Or PR

Run the manual evidence gate:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-pre-push-check.ps1
```

Good result: `status=passed`, `passed_count=9`, `failed_count=0`, no Cloud, no hooks installed by the command, and no generated exports used as source.

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
