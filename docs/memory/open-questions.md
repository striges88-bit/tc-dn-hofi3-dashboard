# Open Questions

- SQLite FTS5 is now the canonical local memory store. `tools/Memory` owns `refresh`, `search`, `explain`, and `stale-check` for the MVP memory layer.
- LanceDB is an active local semantic sidecar spike below SQLite. Its wrapper is `scripts/lancedb-sidecar.ps1`, its generated store is `docs/memory/generated/lancedb`, and it imports only SQLite `search_documents` records with current/proposed status and valid source metadata.
- LanceDB automation remains disabled: no git post-commit hook, no after-save hook, and no background refresh until clean rebuild/delete/reindex behavior is repeatable.
- LanceDB semantic quality remains open. The current spike uses deterministic local token-hash vectors to prove local storage/search/explain mechanics without Cloud; a later decision is needed for production embeddings, hybrid ranking, and recall thresholds.
- Hindsight is historical/failed for MVP in `docs/memory/hindsight-spike.md`; Python/uvx embedded daemon status remains recorded for traceability only.
- The Hindsight install-spike report exists as ignored generated output. `uv` and `uvx hindsight-embed --help` are verified locally. Project profile `tc-dn-hofi3` exists on port `9077`; daemon `/health`, `/mcp/`, and `/metrics` endpoints answer HTTP `200`.
- Hindsight curated import has a pre-install manifest script, but the actual Hindsight retain/import command still needs install-mode verification. The allowlist is `docs/memory/*.md`, `docs/decisions/*.md`, `docs/formulas.md`, `AGENTS.md`, and `tasks/lessons.md`; raw JSONL, generated memory exports, secrets, local proxy details, build artifacts, and unreviewed experiment dumps remain denied.
- Hindsight Codex auto-retain must remain disabled during MVP. Decide later whether reviewed summaries can enable controlled retain.
- Hindsight secret handling decision: store the OpenAI key only in ignored repo-local `.hindsight/tc-dn-hofi3.env` and load it into process environment. Do not pass secret values through `profile create --env KEY=VALUE`, `profile set-env`, or committed config.
- Hindsight auth, bank naming, export/backup, deletion, and retention policies are still undefined and should not block the SQLite memory path.
- Hindsight LLM verification currently fails with OpenAI `billing_not_active`; fix account/billing state before treating retain/recall/reflect results as usable.
- Hindsight embedded `bank list` forwards to the separate Rust `hindsight` CLI; local auto-install failed with `[WinError 2]`, so MCP bank behavior and file import remain unverified.
- Hindsight upstream docs mention both `9077` and `8888` for local daemon examples; runtime project endpoint is confirmed as `http://127.0.0.1:9077`.
- GBrain upstream CLI and Codex MCP path are confirmed in `docs/memory/gbrain-spike.md`, but it is now a historical/secondary candidate; local Windows install, `gbrain init --pglite`, runtime MCP tools, and export/backup format remain unverified.
- Should Graphify still be spiked, or should SQLite `symbols`/`relations` cover the MVP code graph need first?
- Should any generated memory export ever be committed, or should all generated indexes remain local and reproducible?
- What recall threshold is acceptable for retrieval tests once semantic search is added?
- Which human review cadence is enough for experiment summaries so failed live/replay observations do not become formula decisions?
