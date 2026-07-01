# Memory Operations Polish Runbook Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the memory system easier to operate day to day and prove recovery from a fresh clone-like checkout.

**Architecture:** Keep memory tooling outside WPF runtime. Add one local wrapper that clones the committed Git tree into a temporary directory, runs the existing `memory-rebuild-from-head.ps1` recovery flow there, and writes an ignored evidence report. Add a short human runbook that tells the operator which commands to run at daily, commit, push/PR, recovery, and compact moments.

**Tech Stack:** PowerShell 5.1-compatible scripts, Git CLI, project-local or PATH `dotnet`, existing .NET xUnit guardrail tests, generated reports under `docs/memory/generated/`.

---

### Task 1: Active Plan

**Files:**
- Modify: `tasks/todo.md`
- Create: `docs/superpowers/plans/2026-07-01-memory-operations-polish-runbook.md`

- [x] **Step 1: Start from clean `main`**

Run: `git checkout main`, `git pull`, `git checkout -b codex/memory-operations-polish-runbook`.

- [x] **Step 2: Record this slice**

Add an active todo block and this plan file before code/script edits.

### Task 2: Clone-Like Recovery Check

**Files:**
- Create: `scripts/memory-clone-recovery-check.ps1`
- Modify: `CryptoIndicatorApp.Infrastructure.Tests/MemoryRefreshAllTests.cs`

- [x] **Step 1: Add RED plan-only test**

Test intent: `memory-clone-recovery-check.ps1 -PlanOnly` must write a safe report, not create a clone, not run rebuild, not install hooks, and must require a clean working tree for real runs.

- [x] **Step 2: Implement wrapper**

The script must:
- resolve repo root;
- read `HEAD`, tree SHA, branch, and dirty state;
- write `docs/memory/generated/memory-clone-recovery-check-report.json`;
- in `-PlanOnly`, avoid clone/rebuild;
- in real mode, block dirty working trees;
- clone the current repository to a temporary path outside the repo;
- checkout the exact `HEAD` detached in the clone;
- prepend the source repo `.dotnet` directory to `PATH` when present;
- run the clone's `scripts/memory-rebuild-from-head.ps1`;
- run clone `memory status`;
- delete the temporary clone unless `-KeepClone` is passed;
- keep Cloud, hooks, Codex retain, raw JSONL, secrets, generated exports as source, and build artifacts disabled.

- [x] **Step 3: Run narrow test**

Run: `.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter "CloneLikeRecovery"`

### Task 3: Operator Runbook

**Files:**
- Create: `docs/memory/operations-runbook.md`
- Modify: `docs/memory/README.md`
- Modify: `scripts/README.md`
- Modify: `CryptoIndicatorApp.Infrastructure.Tests/ManualMemoryGateTests.cs`

- [x] **Step 1: Add runbook docs test**

Test intent: the runbook must include the daily snapshot, after-commit refresh, before-push gate, clone-like recovery check, compact handoff, and the ban on auto-retain/post-commit rebuild.

- [x] **Step 2: Write short runbook**

The runbook should be short and operator-facing: when to run which command, what a good result looks like, and what not to enable.

### Task 4: Real Verification

**Files:**
- Modify: `tasks/todo.md`

- [x] **Step 1: Run plan-only clone recovery**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-clone-recovery-check.ps1 -PlanOnly`

- [x] **Step 2: Run related tests**

Run the narrow clone recovery test, runbook docs test, related memory guardrail tests, and solution build.

- [ ] **Step 3: Review diff and commit source slice**

Run `git diff --check`, review diff, then commit.

- [ ] **Step 4: Run committed-source memory gate**

After commit only: run `scripts\memory-refresh-all.ps1`, `memory status`, and `scripts\memory-pre-push-check.ps1`.

### Task 5: Optional Real Clone-Like Recovery

**Files:**
- Generated only: `docs/memory/generated/memory-clone-recovery-check-report.json`

- [ ] **Step 1: Run real clone-like recovery after commit**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-clone-recovery-check.ps1`

Expected: report status `completed`, clone deleted by default, clone memory status `needs_refresh=false`.

- [ ] **Step 2: Push/PR**

Run `memory-pre-push-check`, push branch, and open draft PR when clean.
