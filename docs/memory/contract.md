# Agent Memory Contract

This contract defines project memory for agent work on TC-DN-HOFI3. It is tooling only, not application runtime, and the WPF/.NET app must not depend on these files or any generated memory index.

## Source Priority

Use this order when project memory conflicts:

1. `code/tests/config`: current source files, deterministic tests, app config, and ignored runtime artifacts when explicitly inspected.
2. `AGENTS.md`, ADRs, `docs/formulas.md`, and `TC-DN-HOFI3.md`: durable project rules, decisions, and canonical formula material.
3. `docs/memory/*.md`: human-authored memory map, glossary, entities, open questions, and this contract.
4. `docs/memory/generated/`: generated indexes and tool exports. SQLite FTS5 is the canonical local memory store for generated status/index/retrieval metadata, but it is still refreshed from higher-priority sources.
5. No semantic/vector sidecar exists. The LanceDB sidecar was removed on 2026-10-21 (ADR 0010); any future sidecar needs a new ADR and must stay a cache below SQLite status/source metadata.
6. Historical external memory spikes such as Hindsight, GBrain, Mem0, Graphiti, or another agent store.

Generated memory must never override current code, tests, ADRs, formula docs, or project instructions.

## Human And Generated Boundaries

- Human-authored source: `docs/memory/*.md`, `docs/decisions/*.md`, `tasks/lessons.md`, and approved design/spec docs.
- Generated source: only files under `docs/memory/generated/`; this directory stays ignored until a committed schema/export policy is approved.
- Experiments: live/replay/JSONL observations stay as separate experiment summaries with links to recordings or reports. Raw JSONL and bulk runtime observations do not belong in the project memory graph.
- Local stores: SQLite FTS5 is the canonical local generated memory store. The LanceDB semantic sidecar was removed on 2026-10-21 (ADR 0010). Hindsight and GBrain are historical/secondary spikes, not sources of truth.

## Curated Retain Policy

`docs/memory/retain-policy.md` controls any future external retain, Hindsight retain, or Codex auto-retain. Retained memory is a cache below the repository source of truth and must not override code, tests, ADRs, formulas, or SQLite status.

Curated retain is disabled until redaction before retain, export policy, and delete policy are implemented and tested. Codex auto-retain remains disabled during MVP.

The approved curated retain allowlist is:

- `AGENTS.md`
- `docs/decisions/*.md`
- `docs/formulas.md`
- `TC-DN-HOFI3.md`
- `docs/memory/*.md`
- `tasks/lessons.md`

The denylist must stay excluded from all retain/import flows:

- `recordings/*.jsonl`
- `docs/memory/generated/`
- `.hindsight/`
- secrets
- `bin/`
- `obj/`
- `publish/`
- local proxy details
- raw experiment dumps

The allowlist is not permission to retain automatically. Each retained item still needs a source path, source hash, redaction status, review status, and export/delete coverage.

Use `scripts/curated-retain-dry-run.ps1` for the first provider-neutral retain preflight. It enumerates only the approved allowlist, excludes the denylist, scans for redaction risks, and writes ignored JSON/Markdown reports under `docs/memory/generated/`. Findings must include severity, type/severity summary counts, de-duplication, and `policy_reference` markers so policy text is not confused with leaked data. It must not call Hindsight, Cloud services, Codex retain, hooks, `memory-refresh-all`, or memory rebuild commands.

Use `scripts/curated-retain-export-dry-run.ps1` and `scripts/curated-retain-delete-dry-run.ps1` for provider-neutral lifecycle proof before any external/Codex retain path is enabled. They read generated dry-run metadata, validate allowlist/denylist and source hashes, and write ignored JSON/Markdown reports only. They must not include source text before redaction, delete files or provider data, call Cloud or retain APIs, install hooks, run `memory-refresh-all`, or rebuild memory. Missing, stale, denylisted, or redaction-review reports keep external retain and Codex auto-retain disabled.

Use `scripts/curated-retain-redacted-subset.ps1 -SourcePath <repo/path.md>` only after reviewing the dry-run findings for selected allowlisted sources. It rejects files whose current hash no longer matches the dry-run source hash, writes a local generated report with `redaction_status=redacted`, original source hashes, and reviewed `redacted_text`; it does not import, retain, rebuild, install hooks, call Cloud, or call Codex retain.

Use `scripts/curated-retain-import.ps1` only for controlled local import after dry-run review or reviewed redacted-subset generation. It wraps SQLite `retain-import`, reads clean candidates from the selected Git commit tree, and stores reviewed redacted text for redacted entries while preserving commit/source metadata. It writes an ignored generated report and must not call external retain providers, Codex memory, Cloud, hooks, raw JSONL, generated exports, secrets, or build artifacts.

## Node Schema

Allowed node types:

- `module`
- `type`
- `formula`
- `rule`
- `decision`
- `experiment`
- `open_question`
- `data_source`
- `config_option`

Required metadata:

- `id`
- `type`
- `status`: `current`, `proposed`, `superseded`, or `failed`
- `source_path`
- `source_hash`
- `commit_sha` for commit-addressed generated records
- `tree_sha` for commit-addressed generated records
- `source_blob_sha` for commit-addressed generated records
- `indexed_at`
- `created_at`
- `updated_at`
- `confidence`
- `valid_from`
- `valid_until`

Any generated node without `source_path` and `source_hash` is invalid for retrieval. Any commit-addressed generated node without `commit_sha`, `tree_sha`, `source_blob_sha`, and `indexed_at` is invalid for commit-grounded retrieval. `confidence` is evidence quality, not permission to override higher-priority sources.

## Edge Schema

Allowed edge relations:

- `depends_on`
- `owns`
- `feeds`
- `calculates`
- `records`
- `replays`
- `guards`
- `supersedes`
- `contradicts`
- `observed_in`

Edges need the same source grounding as nodes. A graph relation is a navigation hint, not proof by itself.

## SQLite Schema

SQLite FTS5 is the canonical local memory store for generated procedural, episodic, and code/project memory. It is tooling only and must stay out of the WPF/.NET application runtime dependency graph.

Required tables:

- `files`
- `symbols`
- `chunks`
- `rules`
- `adr`
- `formula_versions`
- `metrics`
- `experiments`
- `events`
- `relations`
- `sources`
- `todos`
- `search_documents`
- `search_documents_fts`
- `query_log`

Typed records must preserve source grounding: `id`, `status`, `source_path`, `source_hash`, `commit_sha`, `tree_sha`, `source_blob_sha`, `indexed_at`, `created_at` or `updated_at`, `valid_from`, `valid_until`, and `confidence` where applicable. Canonical status lives in SQLite. No sidecar copies it since the LanceDB sidecar was removed (ADR 0010).

The code-memory MVP extracts lightweight C# facts directly from indexed `.cs` files without loading MSBuild or Roslyn workspaces. It records namespace/type/method symbols in `symbols`, `owns` relations in `relations`, xUnit test-method and `requires_symbol=` markers in `events`, `TODO` markers in `todos`, and `experiment_outcome=` markers in `experiments`. These records are searchable typed documents in `search_documents`; generic chunks remain fallback context, not the preferred answer when a typed symbol/event/relation exists. Stale-check must validate `requires_symbol=` events against the extracted `symbols` table.

C# type identity includes generic arity on each generic owner segment (for example, ``Repro.Box`1.Nested``); arity-zero names are unchanged. The same identity is used by declarations, method parents, ownership, search and `requires_symbol=` references. `record` and `record class` share the `record` kind; `record struct` remains a distinct value-type kind. Generated indexes must be rebuilt after identity changes; they are disposable tooling data, not runtime recording contracts.

Matching partial types retain one logical symbol and every source declaration. A partial method may retain one declaring and one implementing declaration with matching syntactic signature, including return/ref-kind, parameter types/ref-kind, modifiers and generic constraints. Implementation-only `async` and `extern` modifiers are excluded from signature equality; `unsafe` must match. An `extern` declaration counts as the implementation even without a body. Duplicate ordinary methods, two definitions, two implementations and a third part fail indexing. Declaration-only methods remain indexed because an optional body can be absent or generated outside the indexed source set. This lightweight extractor does not bind aliases, resolve types or validate all C# language rules; it does not load `obj` or a compiler workspace to complete declarations.

## Retrieval Protocol

Retrieval is always staged:

1. Exact search / FTS over code, tests, config, and docs through SQLite FTS5.
2. Generated graph/code index lookup.
3. Graph traversal and reranking.
4. Freshness check before answering: source priority, source date/hash, contradiction status, confidence, and explicit gap notes.

Known retrieval facts that must remain easy to answer:

- REST is not allowed in the hot path for subsecond feature calculation.
- The canonical TC-DN-HOFI3 formula source is `TC-DN-HOFI3.md`, summarized by `docs/formulas.md`.
- `Application` must not reference `Infrastructure`.
- Raw JSONL recordings are ignored; only reviewed summaries may become memory facts.
- Binance DTO ownership stays at the Infrastructure boundary.

## Staleness And Contradictions

- A generated node without `source_path` or `source_hash` is stale.
- A commit-addressed generated node without `commit_sha`, `tree_sha`, `source_blob_sha`, or `indexed_at` is stale.
- A `superseded` decision must not rank above a `current` ADR or current source code.
- A failed experiment outcome is a lesson or risk note, not a formula decision.
- A formula decision needs explicit approval and deterministic replay/test evidence before it can change formula, threshold, filter, or cadence memory.
- A contradiction must be recorded as `contradicts` or left as an `open_question`; do not silently merge incompatible facts.
- A superseded rule must not appear in default current retrieval.
- A current `formula_version` without an owner is stale.
- A current/proposed rule without active scope is stale.
- A test reference to a missing symbol is stale.

## SQL Debugging

Use SQLite diagnostics only:

- `memory explain` must use `EXPLAIN QUERY PLAN`.
- Query timings and row counts must be stored in local `query_log`.
- PostgreSQL-only diagnostics are out of scope for the SQLite memory store.

## Refresh Rules

The MVP refresh mechanism is a manual or commit-addressed refresh command. Do not add a git post-commit hook as a memory refresh path because Git/PATH availability is fragile on Windows and hidden refresh can index mixed worktree states. A post-commit marker hook is allowed only as an explicit opt-in local helper that writes `docs/memory/generated/memory-needs-refresh.marker.json`; it must not run rebuild, `memory-refresh-all`, curated retain, Cloud, or Codex auto-retain.

Use `scripts/memory-refresh-all.ps1` as the preferred manual full rebuild wrapper. It runs legacy JSON refresh, SQLite `refresh-from-commit --commit HEAD`, and SQLite stale-check in order, then writes an ignored report under `docs/memory/generated/`.

Use `scripts/memory-rebuild-from-head.ps1` only for local recovery proof or local generated-store corruption. It must expose `-PlanOnly`, may delete only allowlisted generated memory artifacts under `docs/memory/generated/`, then runs `scripts/memory-refresh-all.ps1` and verifies `memory status needs_refresh=false`. It must not delete source files, raw JSONL recordings, secrets, `.hindsight/`, `bin/`, `obj/`, `publish/`, hooks, Cloud data, Codex memory, external retain data, or human-authored memory docs.

Use `scripts/memory-pre-push-check.ps1` as a manual evidence gate after `memory-refresh-all` and before push or PR review. It validates the generated refresh/eval reports, does not run a rebuild by default, does not install hooks, and keeps no post-commit refresh automation in the MVP flow.

`scripts/install-memory-pre-push-hook.ps1` is the approved optional pre-push hook installer. It requires `-Confirm`, refuses unmanaged existing hooks, installs a local managed `pre-push` hook that calls only `scripts/memory-pre-push-check.ps1`, and does not run `memory-refresh-all` inside the hook. Disable the managed hook with `scripts/install-memory-pre-push-hook.ps1 -Disable -Confirm`.

`scripts/install-memory-post-commit-marker-hook.ps1` is the approved optional post-commit marker hook installer. It requires `-Confirm`, refuses unmanaged existing hooks, installs a local managed `post-commit` hook that calls only `scripts/memory-mark-needs-refresh.ps1`, and does not run rebuild. Disable the managed hook with `scripts/install-memory-post-commit-marker-hook.ps1 -Disable -Confirm`.

The manual refresh script and `tools/Memory` CLI may write ignored files under `docs/memory/generated/`, including `project-memory.sqlite`. They must not rewrite human-authored docs, app code, formulas, config, or tests.

`memory-refresh-all` must not install hooks, enable Codex auto-retain, call Cloud services, or import raw JSONL recordings, generated exports, secrets, local proxy details, build artifacts, or unreviewed experiment dumps. Do not install a git post-commit refresh hook, after-save hook, or background updater.

Do not add post-commit auto-refresh for project memory. The allowed Git helpers are explicit opt-in only: the pre-push wrapper around `memory-pre-push-check`, and the post-commit marker helper that marks stale memory but does not run rebuild.

## Tool Strategy

- SQLite FTS5 is the canonical local memory store for generated retrieval/status metadata. Use `tools/Memory` for `refresh-from-commit`, `status`, `search`, `explain`, and `stale-check`. Keep plain `refresh` only as a working-tree diagnostic path.
- There is no semantic sidecar. ADR 0010 removed the LanceDB sidecar; semantic retrieval is SQLite FTS5 only, and a second runtime needs a new ADR that keeps it below SQLite and outside mandatory gates.
- Hindsight is a historical/failed spike. Its upstream Codex, CLI, MCP, and embedded-daemon surfaces are confirmed in `docs/memory/hindsight-spike.md`, but billing/auth, Rust CLI forwarding, retain/import, and operational complexity blocked MVP use.
- Hindsight must stay below SQLite and generated indexes in source priority and must not become a WPF/.NET runtime dependency.
- Codex auto-retain must stay disabled during MVP. Use `scripts/hindsight-curated-import.ps1` to generate a pre-install manifest for curated import sources: `AGENTS.md`, `docs/decisions/*.md`, `docs/formulas.md`, `TC-DN-HOFI3.md`, `docs/memory/*.md`, and `tasks/lessons.md`.
- Use `scripts/curated-retain-dry-run.ps1` before any future retain implementation to produce provider-neutral JSON/Markdown redaction reports from the same allowlist. The reports are generated evidence only and must not import, retain, rebuild, install hooks, or call external providers.
- Do not import raw JSONL recordings, generated memory exports, `.hindsight/`, secrets, local proxy details, build artifacts, or raw experiment dumps into Hindsight or any external retained memory.
- Python/uvx embedded daemon is the selected first Hindsight install-spike path. Track it through `docs/memory/hindsight-install-spike.md` and `scripts/hindsight-install-spike.ps1`; the install-spike report is generated under ignored `docs/memory/generated/`.
- Store Hindsight LLM secrets only in ignored `.hindsight/` env files and load them into process environment. Do not pass secret values through Hindsight profile `--env`, `profile set-env`, shell history, or committed config.
- Do not run Hindsight `retain`, `retain-files`, curated import, or Codex hook configuration until LLM billing/auth, Rust CLI versus embedded import surface, retention, export, and delete policy are confirmed.
- GBrain upstream CLI and Codex MCP documentation are confirmed in `docs/memory/gbrain-spike.md`, but GBrain is now a historical/secondary candidate rather than the roadmap-preferred external memory tool.
- Graphify is still a spike target. Confirm its real CLI/API and export format before making it required.
- Mem0 may be used as a semantic cache with metadata filters, not as a source of truth.
- Graphiti is deferred until temporal contradictions justify graph database/LLM/embedding operations.
- LangGraph short-term state is only needed if this project later owns a custom agent runtime.
