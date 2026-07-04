# Curated Retain Policy

This policy controls any future retain/import into external memory tools or Codex memory for TC-DN-HOFI3.

Codex auto-retain and external retain must not be enabled until this policy is implemented by reviewed tooling and tests. A curated manifest may be generated for review, but it must not call retain/import APIs by itself.

## Source Priority

Retained memory is never a source of truth. The priority remains:

1. `code/tests/config`
2. `AGENTS.md`, ADRs, `TC-DN-HOFI3.md`, and `docs/formulas.md`
3. `docs/memory/*.md`
4. generated SQLite/LanceDB indexes
5. external retained memory

## Allowlist

Only these sources may be considered for curated retain:

- `AGENTS.md`
- `docs/decisions/*.md`
- `docs/formulas.md`
- `TC-DN-HOFI3.md`
- `docs/memory/*.md`
- `tasks/lessons.md`

The allowlist is a maximum set, not an automatic import order. Each retained item still needs source path, source hash, commit SHA when available, redaction status, and review status.

## Denylist

These sources must not be retained or imported:

- `recordings/*.jsonl`
- `docs/memory/generated/`
- `.hindsight/`
- secrets
- `bin/`
- `obj/`
- `publish/`
- local proxy details
- raw experiment dumps

Also deny files or text containing API keys, tokens, credentials, `.env` data, local-only profile paths, local proxy endpoints, raw JSONL, generated exports, or unreviewed live/replay dumps.

## Redaction Before Retain

Redaction before retain is mandatory. Before any external retain or Codex auto-retain:

- remove secrets, tokens, credentials, and local `.env` values;
- remove local proxy details and machine-local endpoint details;
- remove raw recordings, raw experiment dumps, and bulk generated output;
- replace machine-specific absolute paths with repo-relative paths where possible;
- keep enough source context to preserve traceability.

If redaction status is unknown, the item must not be retained.

## Export Policy

Export policy must exist and be tested before enabling external retain or Codex auto-retain.

At minimum, export must provide:

- retained item id;
- source path;
- source hash;
- commit SHA or explicit no-commit reason;
- retained text after redaction;
- created/updated timestamps;
- status and confidence;
- tool/provider metadata.

The export must be reviewable without calling Cloud services.

MVP dry-run implementation is `scripts/curated-retain-export-dry-run.ps1`. It reads the curated retain dry-run report plus allowlisted source metadata, validates source hashes, rejects denylisted paths even if an input report contains them, and writes JSON/Markdown reports under `docs/memory/generated/`.

The export dry-run does not include source text. This is intentional: exporting source text before redaction would create another leak surface. Reviewed local retained-text export is allowed only after a redacted subset report is produced and imported into local SQLite.

## Delete Policy

Delete policy must exist and be tested before enabling external retain or Codex auto-retain.

At minimum, delete must support:

- deleting a retained item by id;
- deleting all items sourced from a path;
- deleting all retained items for this project profile;
- producing an auditable deletion report;
- proving that deleted items do not appear in subsequent recall/search results.

If delete cannot be verified, external retain and Codex auto-retain must not be enabled.

MVP dry-run implementation is `scripts/curated-retain-delete-dry-run.ps1`. It reads the export dry-run report, validates that sources are still allowlisted and current, and writes a deletion plan report only. It does not delete retained items, source files, generated reports, provider data, hooks, or local stores.

## Enablement Gate

External retain and Codex auto-retain must not be enabled until all gates pass:

- allowlist manifest generated from reviewed sources only;
- denylist scan passes;
- redaction before retain is complete;
- export policy is tested;
- delete policy is tested;
- no Cloud retain path is used unless separately approved;
- post-commit marker remains marker-only and does not run retain, rebuild, or Cloud calls.
- missing or stale dry-run reports block retain;
- dry-run reports with denylisted sources block retain;
- dry-run reports with redaction findings block retain until reviewed and redacted.

The first implementation should be a dry-run report. Real retain/import is a later explicit decision.

## Controlled Local Import

The first non-dry-run retain implementation is controlled local import into SQLite only. It is still below repository source of truth and must not call external memory providers or Codex memory.

Controlled local import uses `scripts/curated-retain-import.ps1`, which wraps `tools/Memory` command `retain-import`. Import reads source text from the requested Git commit tree, default `HEAD`, and uses the curated dry-run report as review metadata. A dirty working tree must not change imported text.

Use `scripts/curated-retain-redacted-subset.ps1 -SourcePath <repo/path.md>` to turn explicitly selected, reviewed dry-run entries into a local redacted subset report. The script rejects sources changed since the dry-run report, replaces risky lines with `[REDACTED:<finding-types>]`, preserves the original source hash for commit verification, records `original_finding_count`, and writes ignored JSON/Markdown reports under `docs/memory/generated/`. It does not import, retain, rebuild, install hooks, call Cloud, or call Codex retain.

Import is allowed only when every source is allowlisted, current for the selected commit, and either a redaction-clean `candidate` with zero findings or reviewed `redacted` with `redacted_text`. Denylisted paths, stale hashes, missing source metadata, missing redacted text, or redaction review findings block the whole batch. The generated report is `docs/memory/generated/curated-retain-import-report.json`.

Controlled local import does not enable external retain, Codex auto-retain, Cloud, Hindsight, hooks, refresh wrappers, LanceDB rebuild, raw JSONL import, generated export import, or build-artifact import.

## Controlled Local Export And Delete

After controlled local import, retained SQLite rows must be lifecycle-testable before any external memory is considered. Use `scripts/curated-retain-export.ps1` to export local retained rows into `docs/memory/generated/curated-retain-export-report.json`.

Use `scripts/curated-retain-delete.ps1 -SourcePath <repo/path.md>` to delete local retained rows for one source path. Delete must not remove source files, generated dry-run reports, hooks, LanceDB data, Cloud data, or Codex memory. The proof is: import a clean source, find it through `retain-search`, export it, delete it, then verify the phrase is absent from retain-search.

These lifecycle commands are local SQLite operations. They do not change repository source of truth and do not enable external retain or Codex auto-retain.
