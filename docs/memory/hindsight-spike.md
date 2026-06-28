# Hindsight External Memory Spike

Status: preferred external memory candidate; upstream surface confirmed; local installation unavailable in the current Windows session.

Verified at: 2026-06-28

## Sources

- `https://github.com/vectorize-io/hindsight`
- `https://github.com/vectorize-io/hindsight/blob/main/hindsight-docs/docs/developer/installation.md`
- `https://github.com/vectorize-io/hindsight/blob/main/hindsight-docs/docs/developer/mcp-server.md`
- `https://github.com/vectorize-io/hindsight/blob/main/hindsight-docs/docs/sdks/cli.md`
- `https://github.com/vectorize-io/hindsight/blob/main/hindsight-docs/docs-integrations/codex.md`
- `https://github.com/vectorize-io/hindsight/blob/main/hindsight-integrations/codex/README.md`
- `https://github.com/vectorize-io/hindsight/blob/main/hindsight-api/pyproject.toml`
- `https://github.com/vectorize-io/hindsight/blob/main/hindsight-embed/pyproject.toml`
- `https://github.com/vectorize-io/hindsight/blob/main/hindsight-cli/Cargo.toml`

## Confirmed Upstream Surface

- Repository: `vectorize-io/hindsight`.
- Project language: Python, with a Rust CLI binary named `hindsight`.
- Python packages `hindsight-api` and `hindsight-embed` require Python `>=3.11`.
- `hindsight-api` exposes scripts `hindsight-api`, `hindsight-worker`, `hindsight-local-mcp`, and `hindsight-admin`.
- `hindsight-embed` exposes the local embedded daemon command `hindsight-embed`.
- The CLI docs expose `hindsight memory retain`, `hindsight memory retain-files`, `hindsight memory recall`, `hindsight memory reflect`, bank commands, document commands, entity commands, and audit commands.
- The MCP server is mounted at `/mcp` and supports per-bank endpoints such as `/mcp/{bank_id}/`.
- MCP memory tools include `retain`, `sync_retain`, and `recall`; tools support tags and metadata.
- Codex integration exists through hooks for `SessionStart`, `UserPromptSubmit`, and `Stop`.
- Codex integration supports auto-recall, auto-retain, dynamic per-project bank IDs, and local daemon mode through `uvx hindsight-embed`.

## Current Local Availability

Checked in `C:\Users\MECHREVO\Desktop\PRJCT-INDIC`:

- `where.exe hindsight`: not found.
- `where.exe hindsight-api`: not found.
- `where.exe uvx`: not found.
- `where.exe docker`: not found.
- `python --version`: failed because only the Windows Store alias is visible in PATH.

This means Hindsight is confirmed upstream, but it is not currently usable as a local project tool in this session.

## Project Decision

- Make Hindsight the preferred future external memory candidate, replacing GBrain in the roadmap priority.
- Keep Hindsight outside the WPF/.NET application runtime, build, and test dependencies.
- Keep source priority unchanged: `code/tests/config` -> `AGENTS.md`/ADRs/formula docs -> `docs/memory/*` -> generated indexes -> Hindsight.
- Do not use Hindsight as a source of truth. It is a retrieval/cache layer under current code, tests, ADRs, formula docs, and human-authored memory docs.
- Do not enable Codex auto-retain during MVP. Auto-retain can store stale hypotheses, raw transcript noise, local paths, proxy details, or accidental secrets before review.
- Use curated import first: `docs/memory/*.md`, `docs/decisions/*.md`, `docs/formulas.md`, `AGENTS.md`, and `tasks/lessons.md`.
- Do not import raw JSONL recordings, generated memory exports, secrets, local proxy details, build artifacts, or unreviewed experiment dumps.
- Do not store Hindsight API tokens, local databases, or daemon state in Git.
- Treat Cloud, Docker, Python/uvx, and external PostgreSQL modes as separate install-spike options.

## Curated Import Manifest

- `scripts/hindsight-curated-import.ps1` generates `docs/memory/generated/hindsight-curated-import-manifest.json`.
- The script only lists approved project files; it does not install Hindsight, call a Hindsight API, start a daemon, or enable Codex hooks.
- Approved sources are `docs/memory/*.md`, `docs/decisions/*.md`, `docs/formulas.md`, `AGENTS.md`, and `tasks/lessons.md`.
- Denied sources include raw JSONL recordings, generated memory exports, secrets, local proxy details, build artifacts, and unreviewed experiment dumps.
- This manifest is a pre-install safety gate. The actual Hindsight `retain-files` command, bank ID, auth, and retention behavior still require the install spike.

## Remaining Gaps

- Local install mode is not selected: Cloud, Docker, Python/uvx embedded daemon, or external PostgreSQL.
- Local Windows install has not been executed.
- The Codex hook behavior has not been tested in this desktop environment.
- The exact Hindsight retain/import command using the curated manifest has not been tested yet.
- Export/backup format and deletion/retention policy are not confirmed.
- Authentication and bank naming policy are not defined.
