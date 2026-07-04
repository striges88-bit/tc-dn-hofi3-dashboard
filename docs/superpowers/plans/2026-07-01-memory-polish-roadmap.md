# Memory Polish Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the project memory system sharp enough for routine use without enabling unsafe auto-retain, hidden rebuilds, or stale external memory.

**Architecture:** Keep SQLite FTS5 as canonical generated memory and LanceDB as a semantic sidecar. Curated retain remains disabled until redaction, export, and delete controls are implemented and tested. Automation is introduced only as explicit local helper commands, never as silent rebuild or retain.

**Tech Stack:** PowerShell scripts, .NET/xUnit guardrail tests, `tools/Memory` SQLite CLI, local LanceDB/FastEmbed sidecar, generated reports under `docs/memory/generated/`.

---

## Slice 1: Curated Retain Report Quality

**Purpose:** Turn the current dry-run report from a noisy scanner into a usable review artifact.

**Files:**
- Modify: `scripts/curated-retain-dry-run.ps1`
- Modify: `CryptoIndicatorApp.Infrastructure.Tests/CuratedRetainDryRunTests.cs`
- Modify: `docs/memory/README.md`
- Modify: `scripts/README.md`
- Modify: `tasks/todo.md`

- [x] **Step 1: Add RED tests for report quality fields**

Add test coverage that expects:
- `summary.findings_by_severity`
- `summary.findings_by_type`
- severity values `critical`, `review`, and `info`
- de-duplicated findings by `type + source_path + line + rule`
- `policy_reference=true` for documentation that says secrets/raw dumps/generated exports must not be retained
- Markdown output at `docs/memory/generated/curated-retain-dry-run-report.md`

Run:

```powershell
.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter CuratedRetainDryRunTests
```

Expected: FAIL because the current script emits only the v1 JSON shape and no Markdown report.

- [x] **Step 2: Implement minimal report quality model**

Update `scripts/curated-retain-dry-run.ps1` to:
- classify likely real secret values as `critical`
- classify local paths/proxy/raw dump references as `review`
- classify policy-only references as `info`
- set `policy_reference`
- emit summary counts by type and severity
- de-duplicate identical findings
- write a compact Markdown report next to the JSON report

- [x] **Step 3: Verify Slice 1**

Run:

```powershell
.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter CuratedRetainDryRunTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\curated-retain-dry-run.ps1
.\.dotnet\dotnet.exe build CryptoIndicatorApp.sln --no-restore
git diff --check
```

Expected: tests pass, generated JSON and Markdown reports are ignored outputs, build passes, diff hygiene passes.

## Slice 2: Retain Export/Delete Policy Tooling

**Purpose:** Prove future retained memory can be exported and deleted before any external or Codex retain path is enabled.

**Files:**
- Create: `scripts/curated-retain-export-dry-run.ps1`
- Create: `scripts/curated-retain-delete-dry-run.ps1`
- Modify: `CryptoIndicatorApp.Infrastructure.Tests/CuratedRetainPolicyTests.cs`
- Modify: `docs/memory/retain-policy.md`
- Modify: `docs/memory/contract.md`
- Modify: `scripts/README.md`
- Modify: `tasks/todo.md`

- [ ] **Step 1: Add RED tests for lifecycle gate**

Expected behavior:
- export dry-run reads only the curated retain report and allowlisted source metadata
- delete dry-run removes nothing and writes a deletion plan report only
- external/Codex retain remains disabled if export/delete reports are missing or stale
- denylist paths are never export/delete sources

- [ ] **Step 2: Implement provider-neutral dry-run scripts**

Both scripts must write generated reports only under `docs/memory/generated/`, call no Cloud provider, call no Hindsight/Codex retain, and install no hooks.

- [ ] **Step 3: Verify Slice 2**

Run relevant Infrastructure tests, real dry-runs, full build, and `git diff --check`.

## Slice 3: Post-Commit Marker Local Install Validation

**Purpose:** Make the optional marker-only hook safe for users who want reminders without hidden rebuilds.

**Files:**
- Modify: `scripts/install-memory-post-commit-marker-hook.ps1`
- Modify: `scripts/memory-mark-needs-refresh.ps1`
- Modify: `CryptoIndicatorApp.Infrastructure.Tests/ManualMemoryGateTests.cs`
- Modify: `docs/memory/README.md`
- Modify: `scripts/README.md`
- Modify: `tasks/todo.md`

- [ ] **Step 1: Add RED tests for local install status**

Expected behavior:
- default and `-PlanOnly` install nothing
- `-Confirm` installs only a managed marker hook
- hook writes only `memory-needs-refresh.marker.json`
- hook does not run `memory-refresh-all`, LanceDB rebuild, retain, Cloud, or Codex auto-retain
- `-Disable -Confirm` removes only the managed hook

- [ ] **Step 2: Implement any missing status/report polish**

Prefer reporting and safety checks over new behavior. Do not install the hook during automated verification.

- [ ] **Step 3: Verify Slice 3**

Run hook tests, `-PlanOnly`, memory status, build, and `git diff --check`.

## Slice 4: FastEmbed/LanceDB Warning Baseline

**Purpose:** Turn the current mean-pooling warning into an explicit, tested semantic baseline decision.

**Files:**
- Modify: `tools/MemorySemantic/lancedb_sidecar.py`
- Modify: `tools/MemorySemantic/lancedb_sidecar_tests.py`
- Modify: `docs/memory/lancedb-spike.md`
- Modify: `docs/memory/contract.md`
- Modify: `tasks/todo.md`

- [ ] **Step 1: Add RED tests for embedding baseline metadata**

Expected behavior:
- LanceDB reports the FastEmbed package version
- LanceDB reports the embedding model
- report includes `embedding_pooling_baseline`
- eval report keeps passing with the selected baseline

- [ ] **Step 2: Choose the low-risk baseline**

Default recommendation: accept current FastEmbed `0.8.0` mean-pooling behavior as the baseline only if eval remains `11/11`; do not downgrade unless retrieval quality drops.

- [ ] **Step 3: Verify Slice 4**

Run Python sidecar tests, LanceDB rebuild/eval, relevant .NET tests, build, and `git diff --check`.

## Slice 5: Memory Operator UX

**Purpose:** Give the human operator one reliable command surface for routine memory checks.

**Files:**
- Create: `scripts/memory-daily-check.ps1`
- Modify: `CryptoIndicatorApp.Infrastructure.Tests/ManualMemoryGateTests.cs`
- Modify: `docs/memory/README.md`
- Modify: `scripts/README.md`
- Modify: `tasks/todo.md`

- [ ] **Step 1: Add RED tests for `memory-daily-check.ps1 -PlanOnly`**

Expected behavior:
- reports current branch, `HEAD`, indexed commit, `needs_refresh`, marker status, latest eval status, and open generated reports
- does not refresh, rebuild, retain, install hooks, or call Cloud

- [ ] **Step 2: Implement the helper**

The helper should wrap existing commands and reports; it must not become a new source of truth.

- [ ] **Step 3: Verify Slice 5**

Run helper tests, `-PlanOnly`, memory status, build, and `git diff --check`.

## Completion Criteria

- [ ] All five slices are merged into `main`.
- [ ] `memory status` reports `needs_refresh=false` on `main`.
- [ ] `memory-refresh-all` passes with SQLite stale-check clean and LanceDB eval passing.
- [ ] Curated retain remains disabled until explicit later approval.
- [ ] No hook runs rebuild, retain, Cloud, or Codex auto-retain.
- [ ] Operator docs explain routine commands and failure handling.
