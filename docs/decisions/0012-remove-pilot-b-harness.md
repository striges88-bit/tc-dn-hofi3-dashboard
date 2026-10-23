# 0012: Remove the Pilot B Harness

Date: 2026-10-23

## Status

Accepted. No earlier ADR recorded Pilot B; this one records its removal.

## Decision

Remove the Pilot B observable-reasoning harness entirely. Deleted: `tools/PilotB/`, `tools/PilotB.Tests/`, `tools/PilotB.FakeCli/`, `docs/agents/observable-reasoning-pilot-b-v3-harness.md`, the three projects in `CryptoIndicatorApp.sln`, and the `Retain PilotB test diagnostics` step in `.github/workflows/ci.yml`. The annotated tag `archive/pilot-b` points at `fc30bbc`, the `main` commit this removal branches from.

## Rationale

Pilot B was a control-versus-treatment harness for agent reasoning with a Gate 1 verdict. It never touched the indicator: no Domain, Application, Infrastructure, Desktop, or LiveDryRun project referenced it. From its first commit on 2026-08-22 until removal, no real CLI run, Gate 1 experiment, or Gate 2 batch was performed; its own commits built and hardened the harness. It held about 4,200 lines of harness code and 4,400 lines of tests, roughly the size of the indicator with its tests, and 8 of the 35 non-merge commits since 2026-10-01 changed it directly.

Steven decided on 2026-10-23 not to run the experiment and to archive the harness under a tag instead of moving it to a separate repository.

## Consequences

- The solution builds and tests without Pilot B; CI no longer uploads Pilot B test diagnostics.
- History stays reachable: `git checkout archive/pilot-b -- tools/PilotB tools/PilotB.Tests tools/PilotB.FakeCli docs/agents` restores the sources and the harness doc; `fc30bbc` works where the tag is absent. The solution entries and the CI upload step are not restored by that command. Reintroducing the harness needs a new ADR and belongs in its own repository, not in this one.
- `tasks/todo.md` and `tasks/lessons.md` keep their Pilot B entries as history.
- Local `bin/` and `obj/` leftovers under `tools/PilotB*` are ignored by Git and safe to delete manually.
