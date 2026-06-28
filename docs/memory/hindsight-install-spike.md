# Hindsight Python/Uvx Install Spike

Status: selected install-spike path; safe report script added; `uv` installed user-scoped through WinGet; `uvx hindsight-embed --help` works; project profile and daemon endpoint are confirmed; import/retain remain blocked.

Verified at: 2026-06-28

## Scope

This spike covers the Python/uvx embedded daemon path only. It is project tooling, not a WPF/.NET runtime, build, or test dependency.

The spike must not:

- enable Codex auto-retain;
- import curated project files;
- run `retain`, `retain-files`, or hook-driven memory writes;
- store local Hindsight databases, generated exports, API tokens, or LLM keys in Git.

Codex auto-retain remains disabled during this spike.

## Confirmed Upstream Surface

- The documented local Codex path uses `uvx hindsight-embed`.
- `hindsight-embed` is the Python embedded daemon package.
- The Rust `hindsight` CLI docs expose `memory retain-files`, but embedded-daemon file import must be verified separately before using it.
- Upstream docs currently disagree on local daemon port examples: Codex/local daemon material references `9077`, while the `hindsight-embed` README references `localhost:8888`.

This means the next step is a runtime surface check, not a curated import.

## Local Report Script

Use:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\hindsight-install-spike.ps1
```

The script writes `docs/memory/generated/hindsight-install-spike-report.json`, which is ignored by Git.

Default behavior:

- probes local `python`, `py`, `uv`, `uvx`, `hindsight`, and `hindsight-embed` availability;
- detects only whether relevant secret environment variables are present, never their values;
- writes an install-spike report;
- does not install packages;
- does not call Hindsight APIs;
- does not start a daemon;
- does not run curated import;
- does not enable Codex hooks.

Optional package probe, after uvx is intentionally installed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\hindsight-install-spike.ps1 -ProbeUvxHelp
```

This may download package/runtime dependencies through `uvx`, so it is not the default path.

## Current Local Findings

Initial prerequisite check in this Codex session before install:

- `where.exe py`: not found.
- `where.exe python`: Windows Store alias at `%LOCALAPPDATA%\Microsoft\WindowsApps\python.exe`.
- `where.exe uv`: not found.
- `where.exe uvx`: not found.
- `where.exe hindsight`: not found.

Install-spike actions:

- Installed `uv` user-scoped through `winget install --id astral-sh.uv -e --source winget --scope user`.
- Installed version: `uv 0.11.25`.
- Current Codex shell PATH remained stale after install, but `scripts/hindsight-install-spike.ps1` can discover `uv.exe` and `uvx.exe` under `%LOCALAPPDATA%\Microsoft\WinGet\Packages\astral-sh.uv_*`.
- `uvx hindsight-embed --help` succeeded and downloaded managed `cpython-3.14.6-windows-x86_64-none` plus package dependencies.
- `hindsight-embed profile show -o json` reports the default embedded profile config at `%USERPROFILE%\.hindsight\embed` with port `8888`.
- `hindsight-embed daemon status` reports daemon not running and exits with code `1`.
- `hindsight-embed memory retain --help` and `hindsight-embed bank list --help` both fail before help output with `LLM API key is required`, so retain/import smoke tests need a secret-backed key first.
- `OPENAI_API_KEY`, `HINDSIGHT_API_TOKEN`, and `HINDSIGHT_API_LLM_API_KEY` were not present in this Codex process environment.

Profile/daemon update:

- A new OpenAI project API key named `TC-DN-HOFI3 Hindsight` was created through the encrypted setup flow and written to ignored repo-local `.hindsight/tc-dn-hofi3.env`.
- Secrets are loaded into process environment only; do not pass secret values through `profile create --env KEY=VALUE`, `profile set-env`, shell history, profile config, or committed files.
- Explicit project profile `tc-dn-hofi3` was created with port `9077`.
- Runtime profile output: config `%USERPROFILE%\.hindsight\profiles\tc-dn-hofi3.env`, port `9077`.
- `hindsight-embed -p tc-dn-hofi3 daemon status` reports `Daemon Running`.
- Confirmed local endpoints: `http://127.0.0.1:9077/health` HTTP `200`, `/mcp/` HTTP `200`, and `/metrics` HTTP `200`; `/` returns HTTP `404`.
- First `daemon start` exceeded the wrapper timeout while downloading/initializing heavy dependencies, but the API process later became healthy.
- Daemon log shows OpenAI verification fails with `billing_not_active`, so LLM-dependent operations are blocked until the OpenAI account/billing state is fixed.
- `hindsight-embed -p tc-dn-hofi3 bank list` attempts to use/install the separate Rust `hindsight` CLI and failed locally with `[WinError 2]`; bank/import behavior is still unverified.
- Curated import was not executed, `retain` was not executed, and Codex auto-retain remains disabled.

## Install Gate

Before starting any daemon or import:

1. Keep the secret LLM key in ignored `.hindsight/tc-dn-hofi3.env` and load it only into process environment.
2. Use explicit project profile `tc-dn-hofi3` on port `9077`.
3. Treat daemon `/health` and `/mcp/` as confirmed, but do not treat LLM operations as usable while OpenAI billing returns `billing_not_active`.
4. Confirm whether embedded mode supports a file import path or whether the Rust `hindsight` CLI is required for `retain-files`.
5. Define retention/export/delete policy.
6. Only then design the curated import execution path from the existing manifest.

Do not run `retain-files` during this spike. The existing curated import manifest is only the allowlist source for a later import step.

## Risks

- `uvx` can download Python/runtime/package dependencies into user-local caches, so package probing must be explicit and recorded.
- Hindsight needs a usable LLM provider key for useful retain/recall behavior; the current key exists but OpenAI returns `billing_not_active`.
- Embedded daemon and Rust CLI may not expose identical import surfaces.
- Codex auto-retain can persist stale transcript noise, local paths, proxy details, or accidental secrets before review.
