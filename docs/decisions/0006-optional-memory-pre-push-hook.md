# 0006: Optional Memory Pre-Push Hook

Date: 2026-06-29

## Decision

Allow an optional local Git `pre-push` hook for the memory gate, installed only by an explicit command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\install-memory-pre-push-hook.ps1 -Confirm
```

The installer writes only a managed hook marked by `scripts/install-memory-pre-push-hook.ps1`. The hook calls `scripts/memory-pre-push-check.ps1` and does not run `scripts/memory-refresh-all.ps1`, rebuild memory, enable Cloud, enable Codex auto-retain, or add post-commit automation.

Disable the managed hook with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\install-memory-pre-push-hook.ps1 -Disable -Confirm
```

Do not add automatic installation, post-commit hooks, after-save hooks, or background refresh. Do not overwrite an existing unmanaged `pre-push` hook.

## Rationale

The manual gate is still the source of operational truth: run `memory-refresh-all`, review generated evidence, then run `memory-pre-push-check`. A local `pre-push` wrapper can prevent accidental pushes after stale or failed memory evidence, but it must not hide rebuild work inside Git. Hidden rebuilds can index mixed worktree states and make stale facts look fresh.

## Consequences

- The hook is opt-in and local; repository checkout alone does not install it.
- The hook validates existing reports only, so users still run `scripts/memory-refresh-all.ps1` intentionally.
- The installer refuses unmanaged hooks instead of replacing user automation.
- The disable path removes only the managed hook.
- Generated install reports stay under ignored `docs/memory/generated/`.
