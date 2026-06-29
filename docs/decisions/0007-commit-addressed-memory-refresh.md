# 0007: Commit-Addressed Memory Refresh

Date: 2026-06-30

## Decision

Use commit-addressed memory refresh for the generated SQLite/LanceDB memory layer.

`memory refresh-from-commit --commit HEAD` reads the Git commit tree and stores `commit_sha`, `tree_sha`, `source_blob_sha`, and `indexed_at` metadata. It must not read uncommitted working-tree edits as source facts.

`memory status` reports current `HEAD`, the indexed commit/tree, `indexed_at`, marker state, whether memory needs refresh, and whether the working tree is dirty.

`scripts/memory-refresh-all.ps1` uses `refresh-from-commit --commit HEAD` for the canonical SQLite refresh step. LanceDB remains a sidecar below SQLite and copies commit/source metadata from SQLite records.

The optional post-commit hook is marker-only. It may write `docs/memory/generated/memory-needs-refresh.marker.json`, but it must not run rebuild, `memory-refresh-all`, LanceDB rebuild/eval, curated retain, Cloud, or Codex auto-retain.

Curated retain remains a separate future stage behind a redaction/delete/export policy. The intended allowlist is `AGENTS.md`, `docs/decisions/*.md`, `docs/formulas.md`, `TC-DN-HOFI3.md`, `docs/memory/*.md`, and `tasks/lessons.md`. Denylisted material remains excluded: `recordings/*.jsonl`, `docs/memory/generated/`, `.hindsight/`, secrets, `bin/`, `obj/`, `publish/`, local proxy details, and raw experiment dumps.

## Rationale

Working-tree refresh can index intermediate local edits as fresh memory. Commit-addressed refresh makes generated memory reproducible: the database can say exactly which Git commit and blob each fact came from.

A marker-only hook keeps the operator aware that memory is stale after commit without hiding a rebuild inside Git. The actual refresh/eval remains explicit and reviewable.

## Consequences

- Generated memory may lag behind uncommitted local edits by design.
- Operators should commit durable docs/code first, then run `memory-refresh-all` and `memory-pre-push-check` before push/PR.
- If `memory status` shows `needs_refresh=true`, retrieval should treat generated SQLite/LanceDB results as stale until refresh passes.
- If `working_tree_dirty=true`, generated memory may still be valid for `HEAD`, but it does not include uncommitted changes.
- Any future auto-retain feature needs a separate ADR plus redaction, export, delete, allowlist, and denylist tests.
