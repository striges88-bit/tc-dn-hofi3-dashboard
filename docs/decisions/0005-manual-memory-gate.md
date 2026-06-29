# 0005: Manual Memory Gate

Date: 2026-06-29

## Decision

Use a manual memory gate before push or PR review:

1. Run `scripts/memory-refresh-all.ps1` manually.
2. Run `scripts/memory-pre-push-check.ps1` manually.
3. Review generated JSON/Markdown evidence under `docs/memory/generated/`.

Do not add post-commit memory refresh automation. A later commit-addressed stage may allow an explicit opt-in post-commit marker hook, but that hook must only write a refresh-needed marker and must not run rebuild, `memory-refresh-all`, LanceDB rebuild/eval, curated retain, Cloud, or Codex auto-retain. Do not install Git hooks automatically. ADR 0006 allows only an explicit opt-in local pre-push hook installer that wraps the manual helper without rebuilding memory.

## Rationale

Automatic post-commit or after-save memory refresh can index intermediate or mixed worktree states. That creates fresh but false memory facts, which is more dangerous than an obviously stale index.

The project already has a deterministic full rebuild wrapper and LanceDB eval reports. A separate manual pre-push helper should validate those reports and fail fast if stale-check or semantic quality gates are not clean.

## Consequences

- `memory-refresh-all` remains the manual rebuild command.
- `memory-pre-push-check` is a manual evidence validator, not a hook installer and not a rebuild runner.
- Optional pre-push hook installation is a separate explicit command, not part of the default memory gate.
- Generated reports remain ignored and do not become sources of truth.
- Raw JSONL, `.hindsight/`, secrets, generated exports as sources, local proxy details, and build artifacts stay outside memory ingestion.
- Post-commit rebuild automation stays disallowed for the MVP memory layer; a marker-only post-commit hook is acceptable only if it is explicit opt-in, disableable, and does not run rebuild.
