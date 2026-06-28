# Skill Audit

Date: 2026-05-18

Scope: imported skills from `C:\Users\Steven Owl\.codex\skills`, existing Superpowers skills, and this project's `binance-indicator-dev` skill.

## Decision Rule

Keep skills that materially help build the TC-DN-HOFI3 Windows analytics app: Binance data correctness, internal contracts, deterministic replay, latency, tests, WPF UI quality, and documented architecture decisions.

Remove or avoid skills that duplicate existing Superpowers workflows, assume browser/web tooling that this WPF app does not use, or add broad process overhead without project-specific value.

## Keep

| Skill | Why |
| --- | --- |
| `api-and-interface-design` | Useful for internal market-data contracts, replay envelopes, indicator outputs, and keeping Binance DTOs out of the engine. |
| `source-driven-development` | Useful for official Binance, .NET, WPF, and library docs when API behavior may have changed. |
| `documentation-and-adrs` | Useful for durable stack, data-pipeline, replay, and sequencing decisions. |
| `incremental-implementation` | Useful because the app should land as small replayable slices, not one large dashboard-first change. |
| `code-simplification` | Useful for targeted refactors while preserving behavior. |
| `security-and-hardening` | Useful for validating external WebSocket payloads, JSONL replay files, config, and local file paths. |
| `performance-optimization` | Useful for measured latency, throughput, allocation, and UI responsiveness issues. Ignore web-only Core Web Vitals parts for this WPF app. |
| `code-review-and-quality` | Useful for major changes and pre-merge reviews, especially around sequencing, replay determinism, and tests. |

## Keep For Later

| Skill | Use Only When |
| --- | --- |
| `ci-cd-and-automation` | A git repository and .NET test/build pipeline exist. |
| `frontend-ui-engineering` | Working on WPF UI layout, accessibility, or state; ignore React/browser-specific examples. |
| `deprecation-and-migration` | Removing a legacy implementation after a replacement is already working. |

## Remove Or Avoid

| Skill | Reason |
| --- | --- |
| `using-agent-skills` | Duplicates `superpowers:using-superpowers`; meta-skill duplication creates noisy startup behavior. |
| `context-engineering` | Duplicates project `AGENTS.md` and Superpowers context rules; too broad for daily project work. |
| `spec-driven-development` | Duplicates `superpowers:brainstorming` and `superpowers:writing-plans`. |
| `planning-and-task-breakdown` | Duplicates `superpowers:writing-plans` and project `tasks/todo.md` rules. |
| `test-driven-development` | Duplicates `superpowers:test-driven-development`; keep the Superpowers version as canonical. |
| `debugging-and-error-recovery` | Duplicates `superpowers:systematic-debugging`; keep the Superpowers version as canonical. |
| `git-workflow-and-versioning` | Too broad because it triggers on any code change; current project is not yet a git repository. |
| `browser-testing-with-devtools` | Assumes Chrome DevTools MCP and browser apps; this project is WPF desktop, and the Browser plugin already covers local browser checks. |
| `idea-refine` | Duplicates `superpowers:brainstorming` for idea refinement. |
| `interview-me` | Duplicates the clarification/interview behavior already required by project rules and brainstorming. |
| `doubt-driven-development` | Overlaps with project "criticize" rule, subagent review, and code-review skills; useful idea, but too process-heavy as a global trigger. |
| `shipping-and-launch` | Not needed for the local research MVP; revisit only when packaging/release is in scope. |

## Practical Recommendation

Do not delete anything blindly while a session is active. If pruning is approved, remove only the "Remove Or Avoid" folders from `C:\Users\Steven Owl\.codex\skills`, then restart Codex and confirm the active skill list.
