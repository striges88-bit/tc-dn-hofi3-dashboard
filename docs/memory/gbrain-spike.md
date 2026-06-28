# GBrain CLI/API Spike

Status: historical/secondary candidate. Upstream CLI confirmed; local installation unavailable in the current Windows session. Hindsight replaced GBrain as the preferred external memory candidate on 2026-06-28.

Verified at: 2026-06-28

## Sources

- `https://github.com/garrytan/gbrain`
- `https://github.com/garrytan/gbrain/blob/master/package.json`
- `https://github.com/garrytan/gbrain/blob/master/docs/mcp/CODEX.md`

## Confirmed Upstream Surface

- Repository: `garrytan/gbrain`.
- Package name: `gbrain`.
- CLI entrypoint: package `bin` maps `gbrain` to `src/cli.ts`.
- Runtime requirement: Bun `>=1.3.10`.
- Local storage option: PGlite is supported through `gbrain init --pglite`.
- Standalone install path documented upstream: `bun install -g github:garrytan/gbrain`.
- Basic local lifecycle documented upstream:
  - `gbrain init --pglite`
  - `gbrain doctor`
  - `gbrain import <path>`
  - `gbrain search <query>`
  - `gbrain think <query>`
  - `gbrain capture ...`
- MCP surface documented upstream:
  - Local stdio MCP: `gbrain serve`
  - HTTP MCP: `gbrain serve --http`
  - Codex local wiring: `codex mcp add gbrain -- gbrain serve`
  - Remote Codex wiring: `gbrain connect <mcp-url> --token <token> --agent codex --install`

## Current Local Availability

Checked in `C:\Users\MECHREVO\Desktop\PRJCT-INDIC`:

- `where.exe gbrain`: not found.
- `where.exe bun`: not found.
- `Get-Command gbrain`: not found.
- `Get-Command bun`: not found.

This means GBrain is real upstream, but it is not currently usable as a local project tool in this session.

## Project Decision

- Keep GBrain as a secondary fallback reference, not the roadmap-preferred external memory tool.
- Do not rank GBrain above `docs/memory/hindsight-spike.md` when selecting a future external memory candidate unless a later ADR explicitly supersedes that decision.
- Keep GBrain optional until a separate install spike verifies Bun, Windows behavior, `gbrain init --pglite`, `gbrain doctor`, import scope, MCP wiring, and export/backup behavior locally.
- Do not add GBrain as a required dependency for WPF/.NET runtime, tests, or build.
- Do not make GBrain a source of truth. It can only be a retrieval/cache layer below code, tests, ADRs, formula docs, and human-authored memory docs.
- Do not import raw JSONL recordings, generated memory exports, secrets, or local proxy details into GBrain.
- Do not store GBrain tokens or local databases in Git. Existing ignore rules for `.gbrain/` and `*.gbrain` remain required.

## Remaining Gaps

- Local Windows install and `gbrain init --pglite` have not been executed.
- Actual local MCP tool list has not been inspected from a running `gbrain serve`.
- Export/backup format is not confirmed.
- How GBrain should ingest only curated project memory docs, without duplicating generated indexes or stale historical notes, still needs a small import policy.
