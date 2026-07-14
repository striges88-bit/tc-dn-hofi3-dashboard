# 0009: Curated Retain Storage Boundary

Date: 2026-07-14

## Decision

Store generated project retrieval and controlled local retain in separate SQLite databases:

- `docs/memory/generated/project-memory.sqlite` is the disposable canonical generated index rebuilt from Git sources;
- `docs/memory/generated/project-retained.sqlite` is the local curated-retain lifecycle store changed only by retain import/delete operations.

The retain store contains `retained_items` and `retained_items_fts`. Canonical refresh and recovery must not drop, recreate, or delete it. Retain import keeps one current retained version per `source_path`: a reviewed re-import replaces the prior searchable row atomically while the new row retains its commit, tree, blob, hash, provider, redaction, and timestamp metadata. Git remains the history of source versions; the local retain store is not a second historical archive.

For compatibility, the first default retain command migrates legacy retained rows from `project-memory.sqlite` when the isolated store is empty. It copies the newest retained row for each source path, verifies the destination import, and only then removes the legacy tables. If both databases already contain retained rows, migration must fail closed and preserve both stores; it must not guess which version wins. An explicit `--db` bypasses automatic migration for diagnosis and manual recovery.

## Rationale

Canonical memory is reproducible from committed code and documentation, so destructive rebuild is expected. Reviewed retained text has a separate export/delete lifecycle and can include redactions that are not reproducible without another review. Keeping both in one database allowed normal canonical refresh to erase retained rows silently.

Separating the files makes the safety property structural: rebuilding `project-memory.sqlite` cannot modify `project-retained.sqlite`. Replacing by source path also prevents superseded text from remaining searchable after a later reviewed import.

## Consequences

- `refresh`, `refresh-from-commit`, `search`, `explain`, `stale-check`, and `status` default to `project-memory.sqlite`.
- `retain-import`, `retain-search`, `retain-export`, and `retain-delete` default to `project-retained.sqlite`.
- Explicit `--db` remains available for isolated tests, diagnostics, and legacy-conflict recovery.
- Recovery scripts may rebuild canonical SQLite and LanceDB, but must preserve the retained database.
- External retain, Cloud, Hindsight retain, and Codex auto-retain remain disabled.
