---
name: binance-indicator-dev
description: Project workflow for the TC-DN-HOFI3 Binance USDS-M Futures WPF analytics app. Use when working on Binance stream ingestion, local order-book sequencing, JSONL recording or replay, TC-DN-HOFI3 indicator logic, latency/book-health logging, WPF live or replay display, validation tests, dry runs, project memory, or continuity/skill recovery for this repository.
---

# Binance Indicator Dev

## Overview

Use this skill to work safely inside the TC-DN-HOFI3 project. Keep the repository as the source of truth; local Codex chat history, generated memory, and `%USERPROFILE%\.codex\skills` are convenience caches only.

## Start Here

1. Read `AGENTS.md` first and follow it when it conflicts with generic guidance.
2. Check `tasks/todo.md` for the active plan and update checkboxes for non-trivial work.
3. Check `tasks/lessons.md` for feedback-driven rules before repeating a risky pattern.
4. Use source priority from `docs/memory/contract.md`: current code/tests/config first, then `AGENTS.md`, ADRs, formula docs, human memory docs, generated memory, and external stores last.
5. If a fact may have changed, inspect the source instead of relying on chat memory.

## Project Boundaries

- Treat TC-DN-HOFI3 as a research/analytics hypothesis, not a trading recommendation.
- Keep MVP scope single-symbol USDS-M Futures live/replay WPF analytics unless the user explicitly reopens architecture.
- Do not add automated trading, order placement, portfolio/risk execution, multi-symbol scanning, broad parameter optimization UI, browser-first architecture, database-backed architecture, or Docker-only architecture without a separate approved design.
- Do not change formula, thresholds, filters, or sampling cadence without explicit approval and deterministic replay/unit evidence.

## Architecture Rules

- Preserve layers: `Domain`, `Application`, `Infrastructure`, `Desktop`, and tests.
- Keep `Application` dependent on `Domain` only. Compose `Infrastructure` from `Desktop` or another outer boundary.
- Keep Binance DTOs and third-party client models out of `Domain` and the indicator engine.
- Avoid global state, service locators, static mutable coupling, generic dumping-ground folders, and broad abstractions that do not protect a concrete boundary.
- Keep non-generated source files near or below 500 lines when touching them; extract focused code instead of growing already-large files.

## Data Pipeline Rules

- Live and replay must feed the same internal event types into the same `IndicatorPipeline`.
- Do not use REST in the hot path for depth/trade feature calculation. REST depth snapshot is allowed only for initial local book construction and explicit resync.
- Version JSONL event envelopes and treat replay files, Binance payloads, config files, and user-selected paths as untrusted input.
- Record exchange time, receive time, sequence/update IDs, resync count, book health flags, and any calculation/signal timestamps implemented by the current code.
- Liquidation and open-interest context are slow regime/risk overlays, not subsecond entry triggers.

## Indicator Work

- Canonical formula source: `TC-DN-HOFI3.md`; concise repo summary: `docs/formulas.md`; implementation: `CryptoIndicatorApp.Domain/Indicators`.
- Keep raw indicator values separate from UI-only visual transforms.
- For any indicator behavior change, use a test-first loop with the narrowest meaningful Domain/Application test and replay comparison when recordings are relevant.
- Watch known risks: early/flat robust z-score history, MAD denominator floor, same-direction TFI confirmation, stability gate, book gaps, stale/crossed book, and visual chart scaling that can hide bounded TFI.

## Binance And Infrastructure Work

- Prefer local package docs/tests first; use current official Binance documentation when stream semantics, endpoint shape, limits, or Binance.Net behavior may have changed.
- Maintain local order-book sequencing: snapshot plus buffered diff updates, stale update drops, `pu` continuity checks, and resync on gaps.
- Keep public market-data features keyless. Do not request or store Binance API keys unless a future approved feature truly needs authenticated endpoints.
- Proxy support is outer infrastructure/config; treat local proxy tools such as Shadowsocks as external endpoints, not app-managed networking.

## WPF And UX Work

- User-facing WPF errors should be Russian, plain-language, and free of stack traces or raw protocol noise.
- Do not count WPF visual smoke as complete from Codex unless a real top-level window/rendered geometry was observed. If not available, state that visual smoke remains user-side.
- For chart work, verify geometry/contracts in tests and avoid changing indicator math to fix visual readability.

## Verification

- Before claiming completion, run the narrowest meaningful tests and report anything not verified.
- For shared/high-risk changes, run the relevant project tests and build the solution with the project-local SDK when available: `.\.dotnet\dotnet.exe`.
- Useful commands:
  - `.\.dotnet\dotnet.exe test CryptoIndicatorApp.sln --no-restore`
  - `.\.dotnet\dotnet.exe build CryptoIndicatorApp.sln --no-restore`
  - `.\.dotnet\dotnet.exe publish CryptoIndicatorApp.Desktop\CryptoIndicatorApp.Desktop.csproj -c Release -o publish\desktop --no-restore`
  - `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-refresh.ps1`

## Continuity And Memory

- Keep this skill versioned under `skills/binance-indicator-dev`; install it into `%USERPROFILE%\.codex\skills` using the repository script instead of editing the installed copy by hand.
- Do not treat ChatGPT/Codex chat history as canonical project storage. Summarize durable decisions, evidence, and handoffs into repo docs.
- After meaningful work, update `tasks/todo.md`; after feedback/fixes, update `tasks/lessons.md`.
- Keep raw JSONL recordings, generated memory exports, secrets, and local proxy details out of Git. Commit reviewed summaries and reproducible scripts instead.

## If Context Is Missing

If old desktop-local files or chat history are unavailable, state that explicitly and continue from repository sources. Reconstruct project behavior from code, tests, `AGENTS.md`, `TC-DN-HOFI3.md`, ADRs, docs, and checked-in plans rather than guessing.
