# Hindsight External Memory Spike

Status: preferred external memory candidate; upstream surface confirmed; Python/uvx embedded daemon is locally running for the `tc-dn-hofi3` profile; retain/import, Codex hooks, and curated import remain disabled.

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

Initial check in the workspace root:

- `where.exe hindsight`: not found.
- `where.exe hindsight-api`: not found.
- `where.exe uvx`: not found.
- `where.exe docker`: not found.
- `python --version`: failed because only the Windows Store alias is visible in PATH.

Install-spike update:

- `uv` was installed user-scoped through WinGet as version `0.11.25`.
- `uvx hindsight-embed --help` runs and confirms embedded commands for profile management, daemon management, UI/control helpers, and forwarded memory/bank commands.
- The embedded default profile reports config under `%USERPROFILE%\.hindsight\embed` and port `8888`.
- A project profile named `tc-dn-hofi3` was created on port `9077`; Hindsight stores its profile config under `%USERPROFILE%\.hindsight\profiles\tc-dn-hofi3.env`.
- The project daemon is running at `http://127.0.0.1:9077`.
- Endpoint smoke: `/health`, `/mcp/`, and `/metrics` return HTTP `200`; `/` returns HTTP `404`.
- First daemon startup took longer than the wrapper timeout because it downloaded and initialized heavy dependencies, local embeddings/reranker, embedded PostgreSQL, and migrations. The daemon later became healthy.
- The OpenAI key is loaded only from ignored `.hindsight/tc-dn-hofi3.env` into process environment; secrets are not stored in Git and are not passed through CLI arguments.
- Hindsight LLM verification currently fails with OpenAI `billing_not_active`, so LLM-dependent retain/recall/reflect behavior is not usable yet.
- `hindsight-embed bank list` forwards to the separate Rust `hindsight` CLI; that CLI is still not installed locally, and its auto-installer failed in this environment.
- Embedded mode has not confirmed a `retain-files` equivalent.

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
- The selected install-spike path is Python/uvx embedded daemon, tracked in `docs/memory/hindsight-install-spike.md`.
- The approved local secret policy is repo-local ignored env storage under `.hindsight/`, loaded into process env only. Do not use `profile create --env KEY=VALUE` or `profile set-env` for secrets because command arguments and profile config are easier to leak.

## Curated Import Manifest

- `scripts/hindsight-curated-import.ps1` generates `docs/memory/generated/hindsight-curated-import-manifest.json`.
- The script only lists approved project files; it does not install Hindsight, call a Hindsight API, start a daemon, or enable Codex hooks.
- Approved sources are `docs/memory/*.md`, `docs/decisions/*.md`, `docs/formulas.md`, `AGENTS.md`, and `tasks/lessons.md`.
- Denied sources include raw JSONL recordings, generated memory exports, secrets, local proxy details, build artifacts, and unreviewed experiment dumps.
- This manifest is a pre-install safety gate. The actual Hindsight `retain-files` command, bank ID, auth, and retention behavior still require the install spike.

## Remaining Gaps

- OpenAI Platform billing/account activation is required before LLM-dependent Hindsight operations can be considered usable.
- The Codex hook behavior has not been tested in this desktop environment.
- The exact Hindsight retain/import command using the curated manifest has not been tested yet.
- Export/backup format and deletion/retention policy are not confirmed.
- Bank naming policy is not defined.
- The Rust `hindsight` CLI install path and embedded `retain-files` equivalent are not confirmed.
