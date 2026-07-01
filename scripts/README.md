# Scripts

Place small repository maintenance scripts here when they become necessary.

Do not add one-off commands as scripts unless they are repeatable and documented.

Use `memory-refresh-all.ps1` for the full manual project-memory rebuild. It orchestrates the legacy JSON refresh, canonical SQLite `refresh-from-commit --commit HEAD`/stale-check, and LanceDB cleanup/rebuild/eval sequence without installing hooks or enabling background automation.

Use `memory-pre-push-check.ps1` after `memory-refresh-all.ps1` as a manual evidence gate before push or PR review. It validates the generated refresh/eval reports and does not install hooks, run post-commit automation, or rebuild memory by itself.

Use `install-memory-pre-push-hook.ps1 -Confirm` only when you explicitly want a local managed Git `pre-push` hook. The hook calls `memory-pre-push-check.ps1` and does not rebuild memory. Disable it with `install-memory-pre-push-hook.ps1 -Disable -Confirm`. The installer refuses to overwrite unmanaged hooks.

Use `install-memory-post-commit-marker-hook.ps1 -Confirm` only when you explicitly want a local managed Git `post-commit` hook that writes a refresh-needed marker. The hook calls `memory-mark-needs-refresh.ps1` and does not run rebuild, `memory-refresh-all`, LanceDB, curated retain, Cloud, or Codex auto-retain. Disable it with `install-memory-post-commit-marker-hook.ps1 -Disable -Confirm`.

Use `curated-retain-dry-run.ps1` to generate the ignored provider-neutral retain preflight report under `docs/memory/generated/`. It enumerates only approved retain candidates, scans for redaction risks, and does not call Hindsight, Cloud, Codex retain, hooks, or rebuild commands.
