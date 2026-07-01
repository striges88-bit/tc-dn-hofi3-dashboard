# Memory Futureproof Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove that curated project memory can be imported, searched, exported, deleted, rebuilt, and guarded by quality checks without enabling unsafe auto-retain or hidden rebuilds.

**Architecture:** Keep Git and reviewed docs as the source of truth. SQLite remains the canonical local generated store, LanceDB remains a semantic sidecar, and optional automation is limited to marker-only hooks. Controlled retain must read from a committed Git tree plus reviewed allowlist metadata, not from a dirty working directory.

**Tech Stack:** .NET `tools/Memory` CLI, SQLite FTS5, PowerShell helper scripts, xUnit guardrail tests, local LanceDB/FastEmbed eval reports, generated reports under `docs/memory/generated/`.

---

## Slice 1: Controlled Curated Retain Import

**Purpose:** Move from dry-run-only retain to a local controlled import that is still below repo source of truth and never calls Cloud, Hindsight, or Codex retain.

**Files:**
- Modify: `tools/Memory/MemoryCliOptions.cs`
- Modify: `tools/Memory/MemoryCli.cs`
- Modify: `tools/Memory/MemoryModels.cs`
- Modify: `tools/Memory/MemorySchema.cs`
- Modify: `tools/Memory/MemoryStore.cs`
- Modify: `tools/Memory/GitCommitMemoryIndexer.cs`
- Modify: `tools/Memory.Tests/MemoryCliTests.cs`
- Create: `scripts/curated-retain-import.ps1`
- Modify: `docs/memory/retain-policy.md`
- Modify: `docs/memory/contract.md`
- Modify: `scripts/README.md`
- Modify: `tasks/todo.md`

- [x] **Step 1: Add RED tests**

Expected behavior:
- `retain-import --input-report <report> --commit HEAD` imports only allowlisted, redaction-clean sources.
- Import reads source text from the requested Git commit/tree, not from dirty working-tree files.
- Imported records keep `source_path`, `source_hash`, `source_blob_sha`, `commit_sha`, `tree_sha`, `retained_at`, `redaction_status`, and `provider=local-sqlite`.
- Denylisted paths, stale hashes, missing source paths, and redaction review findings block import.
- Report flags show no Cloud, Hindsight, Codex retain, hooks, refresh-all, or rebuild.

Run:

```powershell
.\.dotnet\dotnet.exe test tools\Memory.Tests\CryptoIndicatorApp.Memory.Tests.csproj --no-restore --filter MemoryCliTests
```

Expected: FAIL before implementation because `retain-import` does not exist.

- [x] **Step 2: Implement minimal local import**

Add CLI command:

```powershell
.\.dotnet\dotnet.exe run --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- retain-import --input-report docs\memory\generated\curated-retain-dry-run-report.json --commit HEAD --json
```

The command creates local SQLite retained-memory tables, validates the dry-run report, imports only clean allowlisted sources from the Git commit tree, and returns a JSON summary. It must not modify app code, human docs, hooks, LanceDB, external stores, or generated reports except through the SQLite DB.

- [x] **Step 3: Add script wrapper**

Add `scripts/curated-retain-import.ps1` as a thin operator wrapper around the CLI. It must pass through `-Commit`, default to `HEAD`, write a generated report, and keep external retain disabled.

- [x] **Step 4: Verify Slice 1**

Run:

```powershell
.\.dotnet\dotnet.exe test tools\Memory.Tests\CryptoIndicatorApp.Memory.Tests.csproj --no-restore --filter MemoryCliTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-import.ps1 -ProjectRoot . -Commit HEAD
.\.dotnet\dotnet.exe build CryptoIndicatorApp.sln --no-restore
git diff --check
```

Expected: tests pass; real repo import may be `blocked` while redaction findings remain; no Cloud, hooks, retain providers, or rebuild run.

## Slice 2: End-To-End Retain Export/Delete Lifecycle

**Purpose:** Prove that imported local retained memory can be found, exported, deleted, and verified absent after deletion.

**Files:**
- Modify: `tools/Memory/MemoryCliOptions.cs`
- Modify: `tools/Memory/MemoryCli.cs`
- Modify: `tools/Memory/MemoryModels.cs`
- Modify: `tools/Memory/MemoryStore.cs`
- Modify: `tools/Memory.Tests/MemoryCliTests.cs`
- Create: `scripts/curated-retain-export.ps1`
- Create: `scripts/curated-retain-delete.ps1`
- Modify: `docs/memory/retain-policy.md`
- Modify: `docs/memory/README.md`
- Modify: `scripts/README.md`
- Modify: `tasks/todo.md`

- [x] **Step 1: Add RED lifecycle tests**

Expected behavior:
- `retain-search --query <text>` finds imported local retained items.
- `retain-export --output <path>` writes generated JSON/Markdown with retained item ids, source metadata, commit metadata, redaction status, provider metadata, and retained text only for local redaction-clean test data.
- `retain-delete --source-path <path>` deletes matching local retained rows and FTS rows.
- A second `retain-search` proves the deleted content is absent.

- [x] **Step 2: Implement lifecycle commands**

Keep commands local-only:

```powershell
retain-search --query "..."
retain-export --output docs\memory\generated\curated-retain-export-report.json
retain-delete --source-path docs/memory/example.md --json
```

Do not delete source files, generated dry-run reports, hooks, LanceDB data, Cloud data, or Codex memory.

- [x] **Step 3: Verify Slice 2**

Run Memory CLI tests, real wrapper scripts against a clean fixture or blocked repo state, full build, and `git diff --check`.

## Slice 3: Retrieval Quality Gate Expansion

**Purpose:** Raise trust in exact/FTS + semantic retrieval before any stronger automation.

**Files:**
- Modify: `tools/Memory.Tests/MemoryCliTests.cs`
- Modify: `tools/MemorySemantic/lancedb_eval_report.py`
- Modify: `tools/MemorySemantic/lancedb_sidecar_tests.py`
- Modify: `docs/memory/contract.md`
- Modify: `docs/memory/README.md`
- Modify: `tasks/todo.md`

- [x] **Step 1: Add RED eval cases**

Required retrieval cases:
- current OFI formula
- formula owner
- funding-source rationale
- Binance DTO boundary
- REST hot path ban
- live/replay shared event pipeline
- funding as slow context
- exchange adapter impact
- superseded/failed exclusion

- [x] **Step 2: Implement report fields**

Generated eval reports must include query, expected ids/types, rank, source path, confidence, freshness/gap notes, and exclusion result for superseded/failed facts.

- [x] **Step 3: Verify Slice 3**

Run Memory CLI tests, Python sidecar tests, LanceDB rebuild/eval, build, and diff hygiene.

## Slice 4: Recovery/Rebuild Proof

**Purpose:** Prove memory can be destroyed locally and rebuilt from Git `HEAD` without hidden machine state.

**Files:**
- Create: `scripts/memory-rebuild-from-head.ps1`
- Modify: `scripts/memory-refresh-all.ps1`
- Modify: `CryptoIndicatorApp.Infrastructure.Tests/MemoryRefreshAllTests.cs`
- Modify: `docs/memory/README.md`
- Modify: `scripts/README.md`
- Modify: `tasks/todo.md`

- [x] **Step 1: Add RED recovery tests**

Expected behavior:
- deleting SQLite DB and LanceDB generated index is recoverable from committed `HEAD`;
- rebuild re-creates SQLite status, LanceDB index, stale-check report, and eval report;
- rebuild does not import raw JSONL, secrets, generated exports, `.hindsight/`, `bin/`, `obj/`, or `publish/`;
- rebuild does not install hooks or call Cloud/Codex retain.

- [x] **Step 2: Implement recovery wrapper**

Add a clear operator command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-rebuild-from-head.ps1
```

It may delete only approved generated local memory artifacts under `docs/memory/generated/` and must verify final `memory status needs_refresh=false`.

- [x] **Step 3: Verify Slice 4**

Run recovery tests, real plan/report mode, real rebuild only after confirming generated-only paths, build, and `git diff --check`.

## Slice 5: Optional Marker-Only Automation Hardening

**Purpose:** Keep automation useful but low-risk: marker-only hook, explicit install, explicit disable, no rebuild, no retain.

**Files:**
- Modify: `scripts/install-memory-post-commit-marker-hook.ps1`
- Modify: `scripts/memory-mark-needs-refresh.ps1`
- Modify: `CryptoIndicatorApp.Infrastructure.Tests/ManualMemoryGateTests.cs`
- Modify: `docs/memory/README.md`
- Modify: `scripts/README.md`
- Modify: `tasks/todo.md`

- [x] **Step 1: Add RED hardening tests**

Expected behavior:
- installer requires `-Confirm`;
- `-Disable -Confirm` removes only the managed hook;
- hook writes only `memory-needs-refresh.marker.json`;
- timeout/lock/report are present;
- hook never runs `memory-refresh-all`, LanceDB rebuild, retain import/export/delete, Cloud, Hindsight, or Codex auto-retain.

- [x] **Step 2: Polish reports/docs**

Prefer status/report clarity over new automation. Do not install the real hook during CI or default verification.

- [x] **Step 3: Verify Slice 5**

Run manual gate tests, helper `-PlanOnly`, build, diff hygiene, memory refresh, memory status, and pre-push gate.

## Completion Criteria

- [x] Controlled local retain import exists and is blocked unless sources are allowlisted, redaction-clean, and commit-grounded.
- [x] Imported retained items can be searched, exported, deleted, and verified absent.
- [x] Retrieval quality gate covers the expanded cases and excludes superseded/failed facts.
- [x] A documented rebuild path restores SQLite and LanceDB from Git `HEAD`.
- [x] Optional automation remains marker-only, explicit, disableable, and documented.
- [x] No workflow calls Cloud, Hindsight, Codex auto-retain, post-commit rebuild, raw JSONL import, generated export import, secrets import, or build-artifact import.
