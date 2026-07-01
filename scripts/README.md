# Scripts

Place small repository maintenance scripts here when they become necessary.

Do not add one-off commands as scripts unless they are repeatable and documented.

Use `memory-daily-check.ps1 -PlanOnly` as the quick read-only operator snapshot. It reports branch, `HEAD`, indexed commit, marker status, LanceDB eval status, and generated report presence. It does not rebuild memory, run `memory-refresh-all`, install hooks, import retain data, call Cloud, call Hindsight, or call Codex retain.

Use `memory-refresh-all.ps1` for the full manual project-memory rebuild. It orchestrates the legacy JSON refresh, canonical SQLite `refresh-from-commit --commit HEAD`/stale-check, and LanceDB cleanup/rebuild/eval sequence without installing hooks or enabling background automation.

Use `memory-pre-push-check.ps1` after `memory-refresh-all.ps1` as a manual evidence gate before push or PR review. It validates the generated refresh/eval reports and does not install hooks, run post-commit automation, or rebuild memory by itself.

Use `install-memory-pre-push-hook.ps1 -Confirm` only when you explicitly want a local managed Git `pre-push` hook. The hook calls `memory-pre-push-check.ps1` and does not rebuild memory. Disable it with `install-memory-pre-push-hook.ps1 -Disable -Confirm`. The installer refuses to overwrite unmanaged hooks.

Use `install-memory-post-commit-marker-hook.ps1 -Confirm` only when you explicitly want a local managed Git `post-commit` hook that writes a refresh-needed marker. The hook calls `memory-mark-needs-refresh.ps1` and does not run rebuild, `memory-refresh-all`, LanceDB, curated retain, Cloud, or Codex auto-retain. Disable it with `install-memory-post-commit-marker-hook.ps1 -Disable -Confirm`. For validation, pass a temporary custom `-HookPath` and review the report fields `targets_default_repo_hook=false`, `custom_hook_path=true`, and `actual_repo_hook_touched=false`; do not use the real `.git/hooks/post-commit` path during tests.

Use `curated-retain-dry-run.ps1` to generate ignored provider-neutral retain preflight reports under `docs/memory/generated/`. It enumerates only approved retain candidates, scans for redaction risks, classifies findings by severity, marks policy-only references, writes JSON plus Markdown, and does not call Hindsight, Cloud, Codex retain, hooks, or rebuild commands.

Use `curated-retain-export-dry-run.ps1` after the curated retain dry-run to generate ignored export lifecycle reports. It validates allowlisted source metadata and hashes, rejects denylisted paths, writes JSON plus Markdown, and does not include source text, call external providers, install hooks, or rebuild memory.

Use `curated-retain-delete-dry-run.ps1` after the export dry-run to generate ignored deletion-plan reports. It validates export metadata and planned selectors, but does not delete files, retained items, provider data, hooks, local stores, or generated reports.

Use `curated-retain-import.ps1` only after reviewing the curated retain dry-run report. It wraps local SQLite `retain-import`, reads source text from the selected Git commit tree, defaults to `HEAD`, and writes `docs/memory/generated/curated-retain-import-report.json`. It imports only allowlisted redaction-clean sources and keeps external retain, Codex auto-retain, Cloud, hooks, refresh wrappers, rebuilds, raw JSONL, generated exports, secrets, and build artifacts disabled.

Use `curated-retain-export.ps1` to export local SQLite retained rows into `docs/memory/generated/curated-retain-export-report.json`. This is local lifecycle evidence for reviewed retained rows; it does not call external providers, install hooks, or rebuild memory.

Use `curated-retain-delete.ps1 -SourcePath <repo/path.md>` to delete local SQLite retained rows for one source path and write `docs/memory/generated/curated-retain-delete-report.json`. It removes retained rows only and does not remove source files, generated reports, hooks, LanceDB data, provider data, or build artifacts.
