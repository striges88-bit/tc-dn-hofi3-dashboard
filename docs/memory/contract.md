# Agent Memory Contract

This contract defines project memory for agent work on TC-DN-HOFI3. It is tooling only, not application runtime, and the WPF/.NET app must not depend on these files or any generated memory index.

## Source Priority

Use this order when project memory conflicts:

1. `code/tests/config`: current source files, deterministic tests, app config, and ignored runtime artifacts when explicitly inspected.
2. `AGENTS.md`, ADRs, `docs/formulas.md`, and `TC-DN-HOFI3.md`: durable project rules, decisions, and canonical formula material.
3. `docs/memory/*.md`: human-authored memory map, glossary, entities, open questions, and this contract.
4. `docs/memory/generated/`: generated indexes and tool exports. These are cache artifacts and must be refreshed from sources.
5. External semantic/vector memory such as Hindsight, Mem0, Graphiti, GBrain, or another agent store.

Generated memory must never override current code, tests, ADRs, formula docs, or project instructions.

## Human And Generated Boundaries

- Human-authored source: `docs/memory/*.md`, `docs/decisions/*.md`, `tasks/lessons.md`, and approved design/spec docs.
- Generated source: only files under `docs/memory/generated/`; this directory stays ignored until a committed schema/export policy is approved.
- Experiments: live/replay/JSONL observations stay as separate experiment summaries with links to recordings or reports. Raw JSONL and bulk runtime observations do not belong in the project memory graph.
- Local stores: Hindsight, GBrain, Graphify, Mem0, Graphiti, embeddings, and local databases are optional caches until their schema and refresh behavior are approved.

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
- `created_at`
- `updated_at`
- `confidence`
- `valid_from`
- `valid_until`

Any generated node without `source_path` and `source_hash` is invalid for retrieval. `confidence` is evidence quality, not permission to override higher-priority sources.

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

## Retrieval Protocol

Retrieval is always staged:

1. Exact search / FTS over code, tests, config, and docs.
2. Generated graph/code index lookup.
3. Semantic search in optional external memory.
4. Graph traversal and reranking.
5. Freshness check before answering: source priority, source date/hash, contradiction status, confidence, and explicit gap notes.

Known retrieval facts that must remain easy to answer:

- REST is not allowed in the hot path for subsecond feature calculation.
- The canonical TC-DN-HOFI3 formula source is `TC-DN-HOFI3.md`, summarized by `docs/formulas.md`.
- `Application` must not reference `Infrastructure`.
- Raw JSONL recordings are ignored; only reviewed summaries may become memory facts.
- Binance DTO ownership stays at the Infrastructure boundary.

## Staleness And Contradictions

- A generated node without `source_path` or `source_hash` is stale.
- A `superseded` decision must not rank above a `current` ADR or current source code.
- A failed experiment outcome is a lesson or risk note, not a formula decision.
- A formula decision needs explicit approval and deterministic replay/test evidence before it can change formula, threshold, filter, or cadence memory.
- A contradiction must be recorded as `contradicts` or left as an `open_question`; do not silently merge incompatible facts.

## Refresh Rules

The MVP refresh mechanism is a manual refresh script. A git post-commit hook may be added later as a convenience wrapper, but it must not be the only update mechanism because Git/PATH availability is fragile on Windows.

The manual refresh script may write ignored files under `docs/memory/generated/`. It must not rewrite human-authored docs, app code, formulas, config, or tests.

## Tool Strategy

- Hindsight is the preferred external semantic memory candidate. Its upstream Codex, CLI, MCP, and embedded-daemon surfaces are confirmed in `docs/memory/hindsight-spike.md`, but local install mode, Windows behavior, retention policy, auth, and export/backup behavior remain spike work.
- Hindsight must stay below generated indexes in source priority and must not become a WPF/.NET runtime dependency.
- Codex auto-retain must stay disabled during MVP. Use `scripts/hindsight-curated-import.ps1` to generate a pre-install manifest for curated import sources: `docs/memory/*.md`, `docs/decisions/*.md`, `docs/formulas.md`, `AGENTS.md`, and `tasks/lessons.md`.
- Do not import raw JSONL recordings, generated memory exports, secrets, local proxy details, build artifacts, or unreviewed experiment dumps into Hindsight.
- GBrain upstream CLI and Codex MCP documentation are confirmed in `docs/memory/gbrain-spike.md`, but GBrain is now a historical/secondary candidate rather than the roadmap-preferred external memory tool.
- Graphify is still a spike target. Confirm its real CLI/API and export format before making it required.
- Mem0 may be used as a semantic cache with metadata filters, not as a source of truth.
- Graphiti is deferred until temporal contradictions justify graph database/LLM/embedding operations.
- LangGraph short-term state is only needed if this project later owns a custom agent runtime.
