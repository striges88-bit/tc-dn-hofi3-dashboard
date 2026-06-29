# 0008: Curated Retain And Memory Lifecycle Policy

Date: 2026-06-30

## Decision

Adopt a curated retain policy before any external memory retain, Hindsight retain, or Codex auto-retain is enabled.

`docs/memory/retain-policy.md` is the lifecycle policy for retain/import operations. It defines redaction before retain, the allowlist, the denylist, review requirements, export policy, and delete policy.

Codex auto-retain remains disabled. External retain remains disabled until the project has:

- a generated manifest from the approved allowlist;
- a redaction review before retain;
- a tested export policy;
- a tested delete policy;
- a human-reviewed report showing retained source paths and hashes.

The approved allowlist is `AGENTS.md`, `docs/decisions/*.md`, `docs/formulas.md`, `TC-DN-HOFI3.md`, `docs/memory/*.md`, and `tasks/lessons.md`.

The denylist remains excluded from all retain/import flows: `recordings/*.jsonl`, `docs/memory/generated/`, `.hindsight/`, secrets, `bin/`, `obj/`, `publish/`, local proxy details, and raw experiment dumps.

The optional post-commit marker hook remains marker-only. It may write a stale-memory marker, but it must not run rebuild, retain, Cloud calls, `memory-refresh-all`, LanceDB rebuild/eval, Hindsight retain, or Codex auto-retain.

## Rationale

Auto-retain is dangerous before lifecycle controls exist. It can preserve local paths, secrets, proxy details, raw recordings, failed experiment noise, or stale facts as if they were current project memory.

The project already has a canonical local SQLite/LanceDB path. Curated retain should therefore be a controlled export/import action below source docs and generated indexes, not a hidden runtime behavior.

## Consequences

- Retain/import stays manual and review-gated.
- The curated manifest may list allowed files, but it must not call external retain APIs by itself.
- Redaction, export, and delete behavior must be tested before enabling external or Codex retain.
- Generated memory and semantic sidecars remain reproducible from Git sources; external memory remains a cache below the repo source of truth.
