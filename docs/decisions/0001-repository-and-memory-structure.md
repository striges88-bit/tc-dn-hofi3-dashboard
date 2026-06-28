# 0001: Repository And Memory Structure

Date: 2026-06-28

## Decision

Use a private GitHub repository named `tc-dn-hofi3-dashboard` with `main` as the stable branch and `feature/*` branches for new work.

Keep the current .NET solution layout at the repository root instead of moving projects into `src/` and tests into `tests/`.

Prepare future `gbrain + graphify` work through documentation under `docs/memory/`, without runtime integration in the current app.

## Rationale

The existing solution already has clear layers: Domain, Application, Infrastructure, Desktop, and tests. A mass move to `src/`/`tests/` would mostly be cosmetic and could break project references, publish scripts, tests, and accumulated instructions.

The memory system is not specified enough to justify code integration. Documentation-first memory keeps the contract explicit while avoiding coupling agent memory to the market-data pipeline.

## Consequences

- Git history starts from the current working structure.
- Generated memory exports stay ignored until their format is approved.
- Durable knowledge belongs in `docs/`; active work logs remain in `tasks/`.
- A later restructure is allowed only when it solves a concrete maintenance problem.
