# 0013: Remove the Memory CLI

Date: 2026-10-23

## Status

Accepted. Supersedes the SQLite-store decision in ADR 0003, the manual gate in ADR 0005, the optional pre-push hook in ADR 0006, commit-addressed refresh in ADR 0007, and the curated-retain lifecycle in ADR 0008. Supersedes the tooling-implementation clauses of ADR 0002. Vacates the operator clauses of ADR 0010 that left the SQLite CLI in place. ADR 0001's repository layout remains. The ban on putting agent memory inside the WPF runtime remains.

## Decision

Remove the Memory CLI and the Hindsight/GBrain spike tooling. Deleted: `tools/Memory/`, `tools/Memory.Tests/`, the `memory-*`, `install-memory-*`, `curated-retain-*`, and `hindsight-*` scripts under `scripts/`, the seven Infrastructure tests that executed those scripts, every file in `docs/memory/` except `glossary.md`, `rules.md`, and `entities.md`, `.cursor/rules/docs-are-contracts.mdc`, the two Memory projects in `CryptoIndicatorApp.sln`, and the `Test Memory CLI` step in `.github/workflows/ci.yml`.

This removal branches from `8bf925b`, the `main` commit that still contains the tooling.

## Rationale

The CLI was agent tooling. No Domain, Application, Infrastructure, Desktop, or LiveDryRun project referenced it. The three handwritten files that remain do not invoke it. Hindsight and GBrain were separate spikes, not part of the indicator. The maintenance surface was a second test assembly, a dedicated CI step, and a script manual for a store the dashboard does not read.

Steven chose removal on 2026-10-23 and included Hindsight and GBrain in the same change. He kept `glossary.md`, `rules.md`, and `entities.md` because agents read those files directly.

## Consequences

- The solution builds and tests without the Memory CLI. CI no longer runs `tools/Memory.Tests`.
- `docs/memory/glossary.md`, `rules.md`, and `entities.md` stay.
- History stays reachable from `8bf925b`. Checking out `tools/Memory` and `tools/Memory.Tests` from that commit restores the projects. Checking out the deleted script and doc paths from the same commit restores those files. Solution entries and the CI step are not restored by that checkout. Reintroducing a memory store needs a new ADR and must stay outside the indicator pipeline.
- ADR 0001's decision to keep the solution at the repository root remains. ADR 0002's ban on integrating agent memory into the WPF runtime remains.
- `tasks/todo.md`, `tasks/lessons.md`, and `docs/superpowers/plans/` keep their memory entries as history.
- `.gitignore` still ignores `docs/memory/generated/` and `bin/` / `obj/`. Those ignored trees, including `tools/Memory.Tests/TestResults/`, were removed from this working tree with the projects.
- The Infrastructure test no longer checks that `docs/architecture.md` contains the sentence forbidding an `Application` reference to `Infrastructure`. That sentence stays in `docs/architecture.md` and in ADR 0009.
