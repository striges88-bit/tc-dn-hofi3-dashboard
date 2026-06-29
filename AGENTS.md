# AGENTS.md

## General Working Rules

1. Respond in Russian unless the user explicitly asks for another language.
2. Criticize, do not just agree: analyze ideas for risks and weaknesses, and suggest improvements.
3. Think before acting: clarify assumptions, uncertainties, and tradeoffs before implementation.
4. Plan non-trivial work: for 3+ step tasks or architecture decisions, write and maintain checkboxes in `tasks/todo.md`.
5. Delegate when useful: use subagents for focused research, exploration, or parallel analysis when the task justifies it.
6. Keep solutions simple: prefer minimal, obvious changes, fix root causes, and avoid hacks.
7. Verify before responding: verify facts, calculations, code, and logic; state uncertainty clearly.
8. Keep responses brief: answer only what was asked, with no flattery or filler.
9. Keep a learning loop: maintain `tasks/lessons.md` after feedback or fixes.

Project-specific rules in this file override generic imported or global instructions when they conflict.

## Git Commit Cadence

- After a coherent, verified work slice is complete, commit it proactively instead of letting reviewed changes accumulate.
- Commit only changes that are in scope for the current task and have passed the narrowest meaningful verification.
- Do not auto-commit secrets, raw recordings, generated memory exports, local machine state, or unrelated user changes.
- If the worktree is mixed, verification failed, or the commit boundary is unclear, stop at a clean point, record status in `tasks/todo.md`, and ask before committing.

## Memory Management Reminder Triggers

These are reminder rules only. Do not add Codex auto-retain hooks, git post-commit refresh hooks, after-save hooks, or background memory refresh until a separate ADR approves retention, deletion, export, and stale-fact controls. A marker-only post-commit hook is allowed only when explicitly installed by the user.

- Before `push` or PR creation, remind the user to run `.\.dotnet\dotnet.exe run --project tools\Memory\CryptoIndicatorApp.Memory.csproj -- status --project-root . --json`; if `needs_refresh=true`, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh-all.ps1`, then run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-pre-push-check.ps1`.
- Around commits that update ADRs, formulas, experiments, regressions, lessons, or memory rules, remind the user that `memory-refresh-all` indexes `HEAD`: commit the durable source first, then run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh-all.ps1`.
- After an architectural decision or formula decision, remind the user to update the human-authored source (`docs/decisions/*`, `TC-DN-HOFI3.md`, `docs/formulas.md`, or `docs/memory/*`), commit it, and then run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh-all.ps1`.
- Before `/compact`, remind the user to stop at a clean handoff point, update `tasks/todo.md`, run `memory status`, and run `memory-refresh-all` only when the source changes to index are already committed; if the work is mid-slice, record the next command instead of forcing refresh.
- After a failed experiment or regression, remind the user to update `tasks/lessons.md` or an experiment summary, then run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh-all.ps1`.
- Keep these reminders limited to explicit `commit`, `push`, PR, `/compact`, ADR, formula, experiment, and regression moments.

## Project Scope

This project is a Windows analytics application for the TC-DN-HOFI3 Binance USDS-M Futures indicator.

Default MVP scope:

- Single-symbol USDS-M Futures live display.
- Local order book from Binance diff depth stream.
- Aggregate trades for trade-flow confirmation.
- JSONL raw event recording.
- Deterministic replay through the same internal event pipeline.
- WPF/.NET single-process desktop app unless the stack decision is explicitly reopened.

MVP means limited approved scope, not throwaway quality. Within the approved scope, implement robust error handling, deterministic tests, and maintainable boundaries.

Out of scope unless separately designed and approved:

- Automated trading.
- Order placement.
- Portfolio/risk execution.
- Multi-symbol scanning.
- Per-symbol parameter optimization UI.
- Docker-only, web-only, database-backed, or browser-first architecture unless the stack decision is explicitly reopened.

## Required Skill

Use `$binance-indicator-dev` for work on this project, especially when touching:

- Binance stream ingestion.
- Order-book sequencing.
- Indicator formula or filters.
- JSONL recording/replay.
- Latency logging.
- WPF UI connected to live or replay data.
- Validation tests and dry runs.

If the skill is not available in the active skill list, try to read it from `%USERPROFILE%\.codex\skills\binance-indicator-dev\SKILL.md`. If it is missing, state that explicitly and continue from this `AGENTS.md`, `TC-DN-HOFI3.md`, and the existing implementation.

## Skill Selection Policy

For this project, `$binance-indicator-dev` is the project-specific default. Use it before broader engineering workflow skills whenever Binance ingestion, order-book sequencing, indicator calculation, replay, latency, WPF live display, or validation is involved.

Prefer these global skills when they directly apply:

- `$api-and-interface-design` for internal event contracts, JSONL envelopes, indicator outputs, and module boundaries.
- `$source-driven-development` when Binance, .NET, WPF, or third-party library behavior depends on current official documentation.
- `$documentation-and-adrs` for durable architecture or data-pipeline decisions.
- `$incremental-implementation` for changes spanning multiple files or subsystems.
- `$code-simplification` for behavior-preserving refactors.
- `$security-and-hardening` for external stream payloads, JSONL replay input, config, file paths, and other untrusted boundaries.
- `$performance-optimization` only after there is a measured latency, throughput, allocation, or UI responsiveness problem.
- `$code-review-and-quality` for major changes or pre-merge review.

Avoid duplicate process skills in this project unless explicitly requested. Use the existing Superpowers equivalents instead of imported duplicates for brainstorming/specs, task planning, TDD, debugging, agent orchestration, and completion verification. In particular, do not prefer imported `using-agent-skills`, `spec-driven-development`, `planning-and-task-breakdown`, `test-driven-development`, or `debugging-and-error-recovery` over the Superpowers workflow.

Browser-specific skills are not part of the default workflow because the application target is WPF desktop. Use browser tooling only for documentation sites, local web artifacts, or explicitly browser-based tasks.

## Architecture Guardrails

- Keep related functionality close, but preserve the established layered boundaries: Domain, Application, Infrastructure, Desktop, and tests.
- Keep dependencies explicit. Do not add hidden coupling through global state, service locators, static mutable state, or cross-layer shortcuts.
- Avoid deep nesting and large orchestration methods. Prefer small methods with clear names over clever patterns.
- Avoid generic dumping-ground folders such as `Services`, `Models`, or `Helpers` when a domain-specific folder/name is possible.
- Keep non-generated source files near or below 500 lines. Documentation and generated files are exempt. If a touched source file is already near or above that size, avoid adding new behavior there unless it is a narrow bugfix; extract focused code instead.
- Use named constants or typed configuration for meaningful numbers, time windows, limits, thresholds, and protocol values.
- Add comments only for non-obvious intent, invariants, protocol quirks, or edge cases. Do not comment obvious one-liners.
- Do not add broad abstraction layers unless they reduce real duplication or protect a concrete boundary already present in the design.

## Data Pipeline Rules

- Do not use REST in the hot path for depth/trade feature calculation.
- Live mode and replay mode must feed the same internal event types into the indicator engine.
- Keep Binance client DTOs outside the indicator engine.
- Version JSONL event envelopes from the first implementation.
- Record exchange time, receive time, calculation time, signal time, sequence/update IDs, resync count, and book health flags.
- Treat replay files, Binance payloads, config files, and user-selected paths as untrusted inputs.

## Indicator Rules

- Treat TC-DN-HOFI3 as a research/analytics hypothesis, not a proven trading signal.
- Do not change the formula, thresholds, filters, or sampling cadence without explicit approval.
- Funding, liquidation, and open interest are slow regime/risk context, not subsecond entry triggers.
- Avoid UI-editable research knobs unless there is a specific validation reason.

## Config And Error Handling

- Do not invent defaults for required business data or integration config. If a required symbol, path, endpoint, sequence value, or payload field is missing, fail fast with a diagnosable error.
- Optional UI-only display values may have harmless fallbacks, but required calculation, stream, replay, and recording data may not.
- Store secrets and truly environment-specific values in environment variables or user secrets. Store indicator parameters, timeouts, runtime options, and non-secret app behavior in project config files.
- Catch exceptions at boundaries when adding context, logging, cleanup, or user-facing messages. Re-throw or propagate the failure; do not swallow errors or return fake success.
- Retries must be explicit, bounded, logged, and limited to idempotent transient operations.
- User-facing errors in the WPF app should be Russian, plain-language, and free of stack traces or raw protocol noise. Internal logs may include technical details.
- Prefer stable error codes for recurring failure classes, especially config, replay, Binance connection, JSONL parsing, and order-book sync failures.

## Legacy And Compatibility

- Do not preserve obsolete code paths, duplicate modules, adapters, aliases, or compatibility shims by default.
- Before removing or changing behavior that affects persisted JSONL recordings, app config, published UI behavior, or existing tests, identify the compatibility impact.
- Keep backward compatibility only when there is a concrete need such as persisted recordings, user config, shipped behavior, or an external consumer.
- If compatibility is needed, isolate it, test it, document why it exists, and define what can remove it later.

## Testing Rules

- Indicator behavior needs deterministic unit or replay tests.
- Live stream observation is not enough unless replay can reproduce the relevant outputs.
- For behavior changes, prefer a test-first loop: write or update the narrowest meaningful failing test, implement, run, fix, and repeat.
- Use risk-based coverage: prioritize order-book sequencing, JSONL replay/recording, indicator math, config validation, external payload mapping, UI state transitions, and regression-prone paths.
- Do not write tests for purely cosmetic WPF styling unless visual behavior is business-critical or previously regressed.
- Mock external Binance/API boundaries in unit tests. Use live dry runs only as additional verification, not as the only proof.
- Test retries, timeouts, malformed payloads, unsupported schema versions, and invalid config only when the code implements those behaviors.

## Verification Rules

- Before claiming completion, run the narrowest meaningful tests available and report what was not verified.
- For shared or high-risk changes, run the relevant project tests and build the solution.
- For WPF visual or interaction changes, verify both code-level behavior and actual rendered behavior when the environment allows it. If Codex cannot see a real WPF window, say so and leave visual smoke as user-side.
- For Binance or network behavior, distinguish sandbox/network failures from application bugs. Rerun outside the sandbox only with explicit approval when required.
- Review uncommitted changes yourself before finishing, even when no external review tool is used.
