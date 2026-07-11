# Scripts

Place small repository maintenance scripts here when they become necessary.

Do not add one-off commands as scripts unless they are repeatable and documented.

Use `memory-daily-check.ps1 -PlanOnly` as the quick read-only operator snapshot. It reports branch, `HEAD`, indexed commit, marker status, LanceDB eval status, and generated report presence. It does not rebuild memory, run `memory-refresh-all`, install hooks, import retain data, call Cloud, call Hindsight, or call Codex retain.

Memory wrappers that call `tools/Memory` use `dotnet run --no-restore`, so run restore/build explicitly on a fresh checkout. If `memory-daily-check.ps1` cannot run the Memory CLI, it reports `CLI unavailable` with `needs_refresh unknown`; fix the CLI/NuGet/build prerequisite instead of treating that report as a stale-memory finding.

Use `memory-refresh-all.ps1` for the full manual project-memory rebuild. It orchestrates the legacy JSON refresh, canonical SQLite `refresh-from-commit --commit HEAD`/stale-check, and LanceDB cleanup/rebuild/eval sequence without installing hooks or enabling background automation. LanceDB diagnostic commands write command-specific reports; only `eval` refreshes the JSON report consumed by `memory-pre-push-check.ps1`.

Use `memory-rebuild-from-head.ps1 -PlanOnly` to review the local recovery delete plan. Without `-PlanOnly`, it deletes only allowlisted generated memory artifacts under `docs/memory/generated/`, runs `memory-refresh-all.ps1`, and verifies `memory status needs_refresh=false`; it does not delete source files, raw JSONL, secrets, `.hindsight/`, `bin/`, `obj/`, `publish/`, hooks, Cloud data, Codex memory, or external retain data.

Use `memory-clone-recovery-check.ps1 -PlanOnly` to review the clone-like recovery proof. Without `-PlanOnly`, it requires a clean working tree, clones committed `HEAD` to a temporary directory outside the repository, runs the clone's `memory-rebuild-from-head.ps1`, verifies clone memory status, and deletes the temporary clone unless `-KeepClone` is passed. It does not install hooks, call Cloud, enable Codex auto-retain, import raw JSONL, use generated exports as source, touch secrets, or touch build artifacts.

Use `memory-pre-push-check.ps1` after `memory-refresh-all.ps1` as a manual evidence gate before push or PR review. It validates the generated refresh/eval reports and does not install hooks, run post-commit automation, or rebuild memory by itself.

Use `memory-semantic-doctor.ps1` before the LanceDB semantic gate on a fresh or repaired machine. It checks `uv` discovery, pinned local semantic dependencies (`lancedb==0.34.0`, `pyarrow==24.0.0`, `fastembed==0.8.0`), cache/venv policy, and offline runtime availability. On a fresh machine, run `memory-semantic-doctor.ps1 -AllowNetworkPreflight` for packages and `lancedb-sidecar.ps1 -Command preflight -AllowNetworkPreflight` for the FastEmbed model cache. Normal gate commands force both uv and model loading offline; hidden network downloads are not allowed inside `memory-refresh-all`, `memory-pre-push-check`, or hooks. Caches must stay outside the repository.

Use `install-memory-pre-push-hook.ps1 -Confirm` only when you explicitly want a local managed Git `pre-push` hook. The hook calls `memory-pre-push-check.ps1` and does not rebuild memory. Disable it with `install-memory-pre-push-hook.ps1 -Disable -Confirm`. The installer refuses to overwrite unmanaged hooks.

Use `install-memory-post-commit-marker-hook.ps1 -Confirm` only when you explicitly want a local managed Git `post-commit` hook that writes a refresh-needed marker. The hook calls `memory-mark-needs-refresh.ps1` and does not run rebuild, `memory-refresh-all`, LanceDB, curated retain, Cloud, or Codex auto-retain. `-TimeoutSeconds` must be positive; marker reports include the lock path used for coordination. Disable it with `install-memory-post-commit-marker-hook.ps1 -Disable -Confirm`. For validation, pass a temporary custom `-HookPath` and review the report fields `targets_default_repo_hook=false`, `custom_hook_path=true`, and `actual_repo_hook_touched=false`; do not use the real `.git/hooks/post-commit` path during tests.

Use `curated-retain-dry-run.ps1` to generate ignored provider-neutral retain preflight reports under `docs/memory/generated/`. It enumerates only approved retain candidates, scans for redaction risks, classifies findings by severity, marks policy-only references, writes JSON plus Markdown, and does not call Hindsight, Cloud, Codex retain, hooks, or rebuild commands.

Use `curated-retain-export-dry-run.ps1` after the curated retain dry-run to generate ignored export lifecycle reports. It validates allowlisted source metadata and hashes, rejects denylisted paths, writes JSON plus Markdown, and does not include source text, call external providers, install hooks, or rebuild memory.

Use `curated-retain-delete-dry-run.ps1` after the export dry-run to generate ignored deletion-plan reports. It validates export metadata and planned selectors, but does not delete files, retained items, provider data, hooks, local stores, or generated reports.

Use `curated-retain-redacted-subset.ps1 -SourcePath <repo/path.md>` after reviewing dry-run findings for selected allowlisted sources. Schema v2 keeps clean `candidate` entries as `content_kind=commit-source-reference` without embedding candidate/source text; only `redacted` entries use `content_kind=reviewed-redacted-text` and include reviewed `redacted_text` with `[REDACTED:<finding-types>]` markers. The report exposes literal `raw_source_text_included`, `source_derived_text_included`, `candidate_text_included`, and `redacted_text_included` flags. It rejects sources changed since the dry-run report and does not import, retain, rebuild memory, install hooks, call Cloud, call Hindsight, or call Codex retain.

Use `curated-retain-import.ps1` only after reviewing the curated retain dry-run report or generating a reviewed redacted subset report. It wraps local SQLite `retain-import`, reads clean candidate text from the selected Git commit tree, stores reviewed `redacted_text` for redacted entries, defaults to `HEAD`, and writes `docs/memory/generated/curated-retain-import-report.json`. It imports only allowlisted clean or reviewed-redacted sources and keeps external retain, Codex auto-retain, Cloud, hooks, refresh wrappers, rebuilds, raw JSONL, generated exports, secrets, and build artifacts disabled.

Use `curated-retain-export.ps1` to export local SQLite retained rows into `docs/memory/generated/curated-retain-export-report.json`. This is local lifecycle evidence for reviewed retained rows; it does not call external providers, install hooks, or rebuild memory.

Use `curated-retain-delete.ps1 -SourcePath <repo/path.md>` to delete local SQLite retained rows for one source path and write `docs/memory/generated/curated-retain-delete-report.json`. It removes retained rows only and does not remove source files, generated reports, hooks, LanceDB data, provider data, or build artifacts.
