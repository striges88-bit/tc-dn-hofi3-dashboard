# Open Questions

- Hindsight is the preferred external semantic memory candidate in `docs/memory/hindsight-spike.md`, but the local install mode is still undecided: Cloud, Docker, Python/uvx embedded daemon, or external PostgreSQL.
- Hindsight curated import still needs a small script/policy: import only `docs/memory/*.md`, `docs/decisions/*.md`, `docs/formulas.md`, `AGENTS.md`, and `tasks/lessons.md`; do not import raw JSONL, generated memory exports, secrets, local proxy details, build artifacts, or unreviewed experiment dumps.
- Hindsight Codex auto-retain must remain disabled during MVP. Decide later whether reviewed summaries can enable controlled retain.
- Hindsight auth, bank naming, export/backup, deletion, and retention policies are still undefined.
- GBrain upstream CLI and Codex MCP path are confirmed in `docs/memory/gbrain-spike.md`, but it is now a historical/secondary candidate; local Windows install, `gbrain init --pglite`, runtime MCP tools, and export/backup format remain unverified.
- Which exact Graphify commands, MCP tools, and export formats are available in this Windows environment?
- Should any generated memory export ever be committed, or should all generated indexes remain local and reproducible?
- What recall threshold is acceptable for retrieval tests once semantic search is added?
- Which human review cadence is enough for experiment summaries so failed live/replay observations do not become formula decisions?
