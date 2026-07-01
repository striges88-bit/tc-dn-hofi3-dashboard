# Windows Binance Indicator App - Planning Todo

## Status

- [x] Inspect project files and documentation.
- [x] Identify current source material for the indicator formula.
- [x] Check git context.
- [x] Clarify MVP scope and runtime stack.
- [x] Propose 2-3 architecture options with tradeoffs.
- [x] Get user approval for the selected design.
- [x] Write implementation plan after design approval.
- [x] Implement only after the plan is approved.
- [x] Verify with tests/live-data dry run.

## Git Repository And Memory Structure Todo

- [x] Review the proposed Git/repository structure against the current WPF/.NET solution.
- [x] Confirm the safer strategy: keep current project layout instead of moving everything into `src/` and `tests/`.
- [x] Check local Git and GitHub tooling availability.
- [x] Update `.gitignore` so build outputs, local tool caches, published binaries, logs, data files, and JSONL recordings are not committed.
- [x] Add durable docs for architecture, formula source, data sources, configuration, repository decisions, and future memory.
- [x] Add `config/appsettings.example.json`, `data/README.md`, `recordings/README.md`, and `scripts/README.md`.
- [x] Verify ignore rules before any first commit.
- [x] Initialize local Git repository on `main` if Git becomes available.
- [x] Create the first commit: `chore: initialize repository structure`.
- [x] Create/connect private GitHub repository `tc-dn-hofi3-dashboard` if GitHub tooling and authentication are available.
- [x] Run solution tests/build after repository hygiene changes.
- [x] Record results and blockers.

## Git Repository And Memory Structure Results

- Installed Git for Windows `2.54.0.windows.1` through `winget` because Git was not available in PATH.
- Initialized a local Git repository on branch `main`.
- Added repository docs: `README.md`, `docs/architecture.md`, `docs/formulas.md`, `docs/data-sources.md`, `docs/configuration.md`, `docs/decisions/0001-repository-and-memory-structure.md`, and `docs/memory/*`.
- Added `config/appsettings.example.json`, `data/README.md`, `recordings/README.md`, and `scripts/README.md`.
- Updated `.gitignore` to exclude build outputs, local SDK/tool caches, `.superpowers/`, `publish/`, logs, `data/*`, JSONL recordings, and future generated memory exports.
- Added `.gitattributes` to keep text files normalized with LF and mark common binary outputs as binary.
- Verified `git add -n .` does not include `bin/`, `obj/`, `publish/`, `recordings/*.jsonl`, or `.superpowers/` files.
- Verified ignore rules with `git check-ignore -v` for `publish/`, `recordings/*.jsonl`, `obj/`, and `bin/` paths.
- Verification used the project-local SDK: `.dotnet/dotnet.exe`.
- `dotnet test CryptoIndicatorApp.sln --no-restore` via local SDK passed `71/71`.
- `dotnet build CryptoIndicatorApp.sln --no-restore` via local SDK passed with `0` warnings and `0` errors.
- Global Git author identity was not configured; the first commit uses local repository identity `MECHREVO <mechrevo@users.noreply.github.com>`.
- GitHub CLI `2.95.0` is installed and authenticated for `striges88-bit`.
- Created private GitHub repository `striges88-bit/tc-dn-hofi3-dashboard`.
- Connected `origin` to `https://github.com/striges88-bit/tc-dn-hofi3-dashboard.git`.
- Pushed `main` to `origin/main` and set the upstream tracking branch.

## Project Setup Todo

- [x] Confirm whether `AGENTS.md` exists in the workspace.
- [x] Create project-specific `AGENTS.md` rules for TC-DN-HOFI3 development.
- [x] Create `binance-indicator-dev` skill for recurring project workflow.
- [x] Validate skill frontmatter and basic structure.
- [x] Record setup results and any limitations.

## Current Findings

- Workspace currently contains `TC-DN-HOFI3.md` and no application code.
- The folder is not a git repository, so commits/history are unavailable unless git is initialized later.
- The source document defines TC-DN-HOFI3: top-3 rolling HOFI, depth normalization, robust z-score, TFI gate, and risk filters.
- The document explicitly warns that the indicator is a research/analytics hypothesis, not a proven trading signal.

## Key Risks

- A desktop UI is easy to build; correct Binance order book sequencing is the harder part.
- REST must not be in the hot calculation path if subsecond indicators matter.
- Formula configurability can become a source of accidental overfitting if every threshold is editable without guardrails.
- Live-only display is insufficient for confidence; we need replay/logging early, even in an analytics-only MVP.
- If this becomes Windows-only too early, testing and data-pipeline work may become harder than necessary.

## Open Decisions

- Runtime stack: C# + WPF + .NET 8 LTS (`net8.0-windows`).
- Market scope: USDS-M Futures only, one configured symbol per app run for MVP.
- MVP data mode: live streams plus raw event recording/replay from the first step.
- Formula configuration: config file only for MVP; broad UI parameter editing is deferred.
- UI scope: current values/status plus a simple 60-second chart.

## Stack Direction

- Candidate stack: C# + WPF + .NET.
- Configuration: `Microsoft.Extensions.Configuration` with JSON files is acceptable.
- Binance access: `Binance.Net` is a practical C# client, but treat it as a third-party dependency, not an official Binance SDK.
- Official Binance `binance-connector-dotnet` appears focused on Spot public API/Spot streams, so it does not cover the USDS-M Futures hot path we need for TC-DN-HOFI3.
- Architecture implication: isolate Binance client calls behind our own market-data interfaces so the indicator engine and replay logic do not depend directly on `Binance.Net` models.
- Data pipeline implication: live and replay modes must feed the same internal event types into the indicator engine.
- Recording format: JSONL for the first version, optimized for debuggability and deterministic replay over storage efficiency.
- JSONL implication: define versioned event envelopes early so future schema changes do not break old recordings.
- Market scope: USDS-M Futures only, one configured symbol per app run for MVP.
- Multi-symbol support is intentionally deferred until single-symbol sequencing, replay, and indicator correctness are verified.
- Binance stream baseline: diff depth from `/public` using `<symbol>@depth@100ms`; aggregate trades from `/market` using `<symbol>@aggTrade`.
- UI MVP: show current `Z_OFI`, `TFI`, signal state, book health, latency, recording status, and a simple 60-second chart for `Z_OFI` and `TFI`.
- Selected architecture: layered single-process WPF MVP.

## Review / Results

- Created project `AGENTS.md` with TC-DN-HOFI3 scope, pipeline, indicator, and verification rules.
- Created Codex skill `binance-indicator-dev` in `C:\Users\Steven Owl\.codex\skills\binance-indicator-dev`.
- Created `tasks/lessons.md` as the feedback/fix learning log.
- Official `quick_validate.py` could not run because the active Python lacks `yaml`; manual frontmatter/default-prompt/template scan passed.
- Design sections approved by user.
- Design spec written to `docs/superpowers/specs/2026-05-18-windows-binance-indicator-design.md`.
- Spec self-review fixed vague runtime and stream-speed wording.
- No application implementation started.
- Next required step: user review of updated spec before implementation plan.

## Structure Review Todo

- [x] Read proposed project tree from `C:\Users\Steven Owl\Downloads\file.txt`.
- [x] Compare proposed tree with approved design spec.
- [x] Identify structural risks before scaffolding.
- [x] Recommend a revised MVP project structure.

## Structure Review Results

- The proposed `Desktop` / `Core` / `Infrastructure` / `Tests` split is directionally correct.
- `Core/Interfaces/IBinanceApiClient.cs` is the wrong boundary for the domain; Binance-specific clients should stay in Infrastructure.
- Generic `Services` and `Models` folders are likely to become dumping grounds unless split by domain concepts.
- MVP needs explicit folders for market events, order book sequencing, JSONL recording/replay, indicator engine, and health/latency state.
- Broad runtime-loaded `Formulas` are too flexible for MVP; use a concrete TC-DN-HOFI3 implementation behind a small domain interface.
- User approved replacing broad `Core` with explicit `Domain` + `Application`.
- Updated the design spec with the accepted solution structure and project responsibilities.

## MVP Implementation Todo

- [x] Install or locate a usable .NET 8 SDK for scaffold/build/test.
- [x] Scaffold `CryptoIndicatorApp` solution with `Desktop`, `Application`, `Domain`, `Infrastructure`, and test projects.
- [x] Implement domain market events, health/sample models, trade classification, order book sequencing, and TC-DN-HOFI3 primitives.
- [x] Implement JSONL reader/writer and Binance adapter boundaries without leaking Binance DTOs into Domain.
- [x] Implement live/replay application sessions and a 60-second chart buffer.
  - [x] Keep `Application` dependent on `Domain` interfaces/models only; compose `Infrastructure` in Desktop or outer layer.
  - [x] Add Application tests for shared pipeline, deterministic replay, recording-before-calculation, and chart retention.
  - [x] Remove direct `Application -> Infrastructure` project reference.
- [x] Implement minimal WPF dashboard with config-only parameters.
  - [x] Add Desktop composition tests for JSONL source/recorder adapters and dashboard state updates.
  - [x] Add Desktop -> Infrastructure reference and compose JSONL replay/recording at the Desktop boundary.
  - [x] Add config-only app settings for one symbol, mode, replay path, and recording path.
  - [x] Build WPF dashboard for symbol/mode/status/path/book health/latency/Z_OFI/TFI/signal and native 60-second chart.
- [x] Run deterministic tests and build verification.

## Live Binance Source Todo

- [x] Verify Binance.Net 12.12.0 public REST/socket API surface against local package docs and official Binance stream/snapshot docs.
- [x] Add red Infrastructure tests for live source snapshot-first buffering, initial depth overlap, and resync on `pu` gap.
- [x] Implement Infrastructure live source boundaries without adding an `Infrastructure -> Application` reference.
- [x] Add Desktop composition adapter for live source and wire Live mode to `LiveIndicatorSession` with JSONL recording.
- [x] Verify narrow tests, full solution tests, and build.
- [x] Record whether a live-data dry run was performed or deferred.

## Post-Live MVP Enhancements Todo

- [ ] Add manual symbol/ticker refresh from Binance USDS-M exchange information, with delisted/inactive symbol handling.
- [x] Add optional proxy configuration for public REST snapshot and WebSocket market streams.
- [x] Verify proxy support against Binance.Net/CryptoExchange.Net APIs before implementation; ShadowSocks should be treated as an external local proxy endpoint, not as app-managed networking.

## Proxy And Live Dry Run Todo

- [x] Verify Binance.Net/CryptoExchange.Net proxy support from the installed package documentation.
- [x] Add red tests for config-only proxy binding and Binance client option propagation.
- [x] Implement minimal proxy options: enabled/type/host/port.
- [x] Wire proxy options into `BinanceNetUsdFuturesMarketDataClient` through Desktop composition.
- [x] Run narrow tests, full tests, and build.
- [x] Perform live dry run: record short JSONL and replay it through the existing pipeline.
  - [x] Add a small non-GUI dry-run harness using existing Application/Infrastructure pipeline.
  - [x] Record a short public USDS-M Futures JSONL session.
  - [x] Replay the recorded JSONL through `ReplayIndicatorSession`.
  - [x] Run solution tests/build after the dry run.

## Active Symbol Dry Run And Formula Review Todo

- [x] Verify requested symbols are active Binance USDS-M perpetual contracts.
- [x] Run live JSONL recording plus replay for `ESPORTSUSDT`, `XANUSDT`, and `PLAYUSDT`.
- [x] Extract replay-level sample statistics from the recorded JSONL files.
- [x] Review current formula implementation against `TC-DN-HOFI3.md`.
- [x] Run deterministic tests/build after analysis.
- [x] Record dry-run and formula review results.

## Active Symbol Dry Run And Formula Review Results

- Binance REST check on 2026-05-25 confirmed all requested symbols exist as `TRADING` `PERPETUAL` USDS-M contracts:
  `ESPORTSUSDT`, `XANUSDT`, `PLAYUSDT`.
- 24h REST snapshot at check time:
  `ESPORTSUSDT` last `0.0601000`, 24h change `-91.553%`, quote volume `228323482.1636300`;
  `XANUSDT` last `0.0124990`, 24h change `38.126%`, quote volume `215983845.7516040`;
  `PLAYUSDT` last `0.1041200`, 24h change `48.235%`, quote volume `240164431.8149900`.
- Added `tools/LiveDryRun.Tests` and `IndicatorSampleSummaryCollector` so the CLI reports replay-wide `Z_OFI`, TFI, signal, resync, and latency stats instead of only the last replay sample.
- Extended dry-run recordings:
  `recordings/live-dry-run-esportsusdt-summary-20260525-173847.jsonl`,
  `recordings/live-dry-run-xanusdt-summary-20260525-173907.jsonl`,
  `recordings/live-dry-run-playusdt-summary-20260525-173928.jsonl`.
- ESPORTSUSDT 20s dry run: `390` events, `125` live samples, `125` replay samples, count match `true`, resync max `0`, `Z_OFI` min/max `-8.94286167` / `19.38043897`, TFI min/max `-1` / `1`, candidates long/short/neutral `12/15/98`, latency p50/p95/p99 `211.364/244.245/288.603 ms`.
- XANUSDT 20s dry run: `236` events, `108` live samples, `108` replay samples, count match `true`, resync max `0`, `Z_OFI` min/max `-5233262585.5446074` / `66728151910.78520356`, TFI min/max `-1` / `1`, candidates long/short/neutral `10/9/89`, latency p50/p95/p99 `212.05/266.508/301.942 ms`.
- PLAYUSDT 20s dry run: `660` events, `133` live samples, `133` replay samples, count match `true`, resync max `0`, `Z_OFI` min/max `-4.56441306` / `3.67812624`, TFI min/max `-1` / `1`, candidates long/short/neutral `2/5/126`, latency p50/p95/p99 `208.127/222.117/239.359 ms`.
- Formula implementation review:
  HOFI uses CKS level OFI over top 3 book levels with exponential weights and `lambda=0.8`; depth normalization divides rolling 250 ms HOFI by median weighted top-3 USD depth over `DepthReferenceSeconds=60`.
- Robust z-score is applied to `NOFI`, not raw HOFI, using median/MAD over `ZScoreWindowSeconds=180`; current implementation has no warm-up/min sample requirement and no MAD floor beyond epsilon.
- XANUSDT produced enormous `Z_OFI` values because early/flat NOFI history made MAD effectively zero; this makes current candidate counts unsafe as signal evidence.
- Current TFI sums base asset quantities over the same rolling `OfiWindowMilliseconds=250`; the source formula/pseudocode expects aggressive notional (`price * qty`), so this is a formula mismatch for symbols with different price scales.
- Current final signal is a simple logical gate: long if `Z_OFI >= ThetaZ` and `TFI >= ThetaTfi`, short if symmetric; it does not yet implement the 1s stability window, volume floor, spread/depth/vol/cancel filters, or funding/liquidation/OI risk overlays from `TC-DN-HOFI3.md`.
- Verification after the dry-run analysis: full solution tests passed `35/35`; build passed with 0 errors and 5 existing `NU1900` warnings for NuGet vulnerability metadata.

## Formula Validity Fix Todo

- [x] Add red Domain tests for TFI notional flow, robust z warm-up/MAD floor, and 1s stability gating.
- [x] Switch rolling TFI from base quantity to aggressive notional (`price * quantity`) over the existing 250 ms window.
- [x] Add config-only warm-up/min-history and MAD denominator floor so early/flat `NOFI` history cannot emit actionable z-score spikes.
- [x] Implement the existing 1s stability gate using `StabilityWindowMilliseconds` / `ThetaStable` without adding wider filters yet.
- [x] Run full tests/build.
- [x] Replay the latest `ESPORTSUSDT`, `XANUSDT`, and `PLAYUSDT` JSONL files through the updated formula and record before/after impact.

## Formula Validity Fix Results

- Added red/green Domain tests for notional TFI, robust z denominator floor, warm-up/min-history, and stability-gated candidate emission.
- Added config parameters `MinimumZScoreSamples=30` and `NofiMadFloor=0.000001`; Desktop config now exposes both under `Dashboard.Indicator`.
- `RollingTradeFlow` now uses aggressive notional (`Price * Quantity`) instead of base quantity over the same rolling 250 ms window.
- Signal candidate logic now requires 250 ms `Z_OFI`, same-direction TFI, and either same-direction 1s stable `Z_OFI` or same-direction fast `Z_OFI` in 2 of the last 3 evaluations. Wider filters from `TC-DN-HOFI3.md` remain deferred.
- Added `tools/LiveDryRun` replay-only mode using `--input ... --replay-only` so existing JSONL recordings can be recalculated without a new live run.
- Full verification after implementation: `dotnet test CryptoIndicatorApp.sln --no-restore` passed `40/40`; `dotnet build CryptoIndicatorApp.sln --no-restore` passed with 0 errors and 5 existing `NU1900` warnings.
- Replay recalculation on the same files:
  `ESPORTSUSDT` stayed at `125` samples, `Z_OFI` min/max `-8.94286167` / `4.62699879`, candidates long/short/neutral `6/14/105`.
  `XANUSDT` stayed at `108` samples, `Z_OFI` min/max `-4.89913568` / `4.0521794`, candidates long/short/neutral `7/7/94`.
  `PLAYUSDT` stayed at `133` samples, `Z_OFI` min/max `-4.56441306` / `3.67812624`, candidates long/short/neutral `2/4/127`.
- Main impact: the XANUSDT near-zero-MAD explosion was removed; candidate counts dropped because 1s stability now filters isolated 250 ms spikes.

## User-Ready MVP UI Slice Todo

- [x] Add Binance USDS-M exchange-info symbol metadata boundary and tests for active perpetual filtering.
- [x] Add Desktop symbol refresh composition without moving Infrastructure into Application.
- [x] Convert WPF startup from auto-run to controlled `Symbol`, `Mode`, `Start`, `Stop`, `Restart` UI.
- [x] Keep one selected symbol active at a time; cancel/dispose the previous session on stop/restart.
- [x] Change default mode to `Live` so the app opens in a usable state.
- [x] Run narrow tests, full solution tests, and publish.
- [x] Run live smoke recordings/replays for `ESPORTSUSDT`, `XANUSDT`, and `PLAYUSDT`.

## User-Ready MVP UI Slice Results

- Added `BinanceUsdFuturesSymbolMetadata`, `BinanceUsdFuturesSymbolFilter`, and `IBinanceUsdFuturesSymbolProvider`.
- `BinanceNetUsdFuturesMarketDataClient` now loads USDS-M exchange info and returns sorted active `TRADING` + `PERPETUAL` symbols for UI refresh.
- WPF dashboard no longer auto-runs on launch; it exposes `Symbol`, `Mode`, `Refresh symbols`, `Start`, `Stop`, and `Restart`.
- UI state remains single-symbol: changing symbol only changes the next session; running sessions are stopped before restart.
- Default Desktop config is now `Live` with recording path template `recordings/{symbol}.jsonl`.
- Published Desktop app to `publish\desktop`.
- Full solution verification after implementation: `dotnet test CryptoIndicatorApp.sln --no-restore` passed `45/45`; publish passed with the existing `NU1900` warning.
- Sandbox live WebSocket failed with `CantConnectError.UnableToConnect`; the same dry-run command succeeded outside the sandbox.
- Live smoke `ESPORTSUSDT`: `106` events, `44` live samples, `44` replay samples, count match `true`, synced `true`, max resync `0`, candidates long/short/neutral `0/0/44`, latency p50/p95/p99 `219.295/271.553/315.523 ms`.
- Live smoke `XANUSDT`: `501` events, `60` live samples, `60` replay samples, count match `true`, synced `true`, max resync `0`, candidates long/short/neutral `3/1/56`, latency p50/p95/p99 `215.658/221.257/276.902 ms`.
- Live smoke `PLAYUSDT`: `131` events, `34` live samples, `34` replay samples, count match `true`, synced `true`, max resync `0`, candidates long/short/neutral `0/0/34`, latency p50/p95/p99 `211.383/254.19/260.146 ms`.

## Next UX And Context Modules Plan

- [x] Fix the non-rendering 60-second chart first; likely cause is WPF layout sizing where the metrics row owns the remaining `*` height and the chart `Canvas` sits in an `Auto` row without a stable height.
- [x] Add a focused Desktop test or renderable chart-point test for non-empty `ChartSamples` producing non-empty `Polyline` points when the chart has dimensions.
- [x] Add ticker search/type-ahead behavior to the symbol selector so pasted or typed symbols can be selected without scrolling through 500+ contracts.
- [x] Compact the top command/status area by merging title, symbol/mode controls, refresh, start/stop/restart, connection status, and symbol refresh status into one dense header band.
- [x] Replace oversized metric tiles with compact value-fit columns for `Z_OFI`, TFI, signal, book health, latency, and last update.
- [x] Add visual color state for signal candidates, with green/red intensity based on signal strength, but keep text labels so color is not the only signal.
- [x] Make Replay path behavior explicit: in Live mode hide or de-emphasize it; in Replay mode require/select a file instead of showing ambiguous `n/a`.
- [x] Add a window pin / always-on-top toggle in the compact header so the dashboard can stay visible while switching to other windows.
- [ ] Design liquidation context as a separate slow-risk module using Binance USDS-M `<symbol>@forceOrder`; aggregate by 15-minute buckets and display signed notional intensity over roughly 2.5 hours.
- [ ] Design open-interest context as a separate low-frequency module from Binance REST open interest endpoints; do not treat it as a WebSocket stream or hot-path input.
- [ ] Run full tests, publish, and manual WPF smoke after each UI slice before adding the next data module.

## Next UX And Context Modules Notes

- Binance liquidation stream is available for a specific symbol as `<symbol>@forceOrder`, update speed 1000 ms, and only pushes the largest liquidation snapshot in each interval. It is useful as context/risk overlay, not as a subsecond trigger.
- Binance open interest for USDS-M is documented through REST endpoints: current `/fapi/v1/openInterest` and historical/statistical `/futures/data/openInterestHist` with periods such as `5m` and `15m`. Treat it as slow context.
- The first implementation should not mix liquidation/OI into TC-DN-HOFI3 signal logic. Show them as separate visual modules until there is replay data and a tested rule.

## MVP UX Hardening Slice Todo

- [x] Add red Desktop tests for chart geometry, symbol search/filtering, mode-aware replay path text, and signal visual intensity.
- [x] Fix the 60-second chart so live samples render into non-empty polyline points and the chart owns stable vertical space.
- [x] Add ticker type-ahead/search after symbol refresh with paste-to-select exact symbol behavior.
- [x] Merge title, symbol/mode controls, refresh/start/stop/restart, connection status, and symbol refresh status into one compact header band.
- [x] Replace tall metric tiles with a compact value-fit metric strip so the chart gets more space.
- [x] Add signal color state with long/short intensity from existing `Z_OFI` plus TFI confirmation, keeping the text label.
- [x] Make Replay path UX mode-aware: de-emphasized live text, explicit missing-file/configured-file state in Replay.
- [x] Run narrow Desktop tests, full solution tests, publish, and record results.

## MVP UX Hardening Slice Review

- Added `ChartGeometryBuilder` and Desktop tests for non-empty chart points with stable dimensions.
- Moved the chart into the main `*` row, made metrics `Auto`, and gave the chart/canvas stable minimum height.
- Switched symbol selector to editable type-ahead over `FilteredSymbols`; exact paste and unique prefix search select the symbol without scrolling.
- Invalid search text now disables `Start` so a stale previous symbol is not launched accidentally.
- Merged title, controls, refresh status, and connection status into one compact header band.
- Replaced tall metric tiles with a compact horizontal metric strip; signal tile uses a visual intensity brush while keeping `LongCandidate` / `ShortCandidate` / `Neutral` text.
- Replay path now shows `Live mode` with reduced opacity in Live and explicit missing/configured state in Replay.
- Verification: Desktop tests passed `14/14`; full solution tests passed `51/51`; publish to `publish\desktop` succeeded.
- Existing warning remains: `NU1900` because NuGet vulnerability metadata could not be loaded from `https://api.nuget.org/v3/index.json`.
- Published app was launched for smoke check as process `CryptoIndicatorApp.Desktop` PID `33292`; visual inspection remains user-side.

## Upcoming MVP Polish Todo

- [x] User visual smoke: confirm the published app renders the live 60-second chart after the latest layout fix.
- [x] Add a header-level pin toggle that maps to WPF `Window.Topmost`, with clear on/off visual state.
- [x] Keep the pin state local to the running window first; persist it to config only if repeated manual use shows it is worth remembering between launches.
- [x] Run Desktop/full solution tests before publish.
- [x] User visual smoke: confirm the Pin toggle keeps the app above other windows during normal window switching.

## Upcoming MVP Polish Priority

- The pin / always-on-top toggle should be the next small UI polish after confirming the chart fix, and before liquidation/open-interest modules.
- It is useful for the actual workflow, but it is not data-pipeline work; it should not delay fixing a still-broken chart if visual smoke finds one.
- Implementation should stay in Desktop only: no Domain/Application/Infrastructure changes and no indicator behavior changes.

## Upcoming MVP Polish Results

- User confirmed the live 60-second chart renders after the previous layout fix.
- Added `DashboardViewModel.IsAlwaysOnTop` with a Desktop test for default-off state and `PropertyChanged` notification.
- Bound WPF `Window.Topmost` to `IsAlwaysOnTop` and added a compact `Pin` toggle in the header with a visible checked state.
- Kept pin state runtime-local only; no config persistence was added.
- Verification: Desktop tests passed `15/15`; full solution tests passed `52/52`; publish to `publish\desktop` succeeded.
- User confirmed the Pin toggle keeps the window above other windows during normal window switching.
- Existing warning remains: `NU1900` because NuGet vulnerability metadata could not be loaded from `https://api.nuget.org/v3/index.json`.

## Liquidation And Open Interest Formula Design Todo

- [x] Confirm the pin / always-on-top UX with user-side smoke.
- [x] Verify current Binance USDS-M liquidation and open-interest data sources from official docs.
- [x] Analyze bucket delta formulas and normalization choices before implementation.
- [x] Get user approval for the recommended liquidation/OI context formula.
- [x] Write the implementation plan for separate context modules after formula approval.

## Liquidation And Open Interest Formula Design Notes

- Liquidations should be treated as an observed forced-order context stream, not total market liquidations, because Binance pushes only the largest liquidation order per symbol within each 1000 ms interval.
- Recommended liquidation bucket value: signed observed notional delta, where `BUY` force-order side is interpreted as short-liquidation buy pressure and `SELL` side as long-liquidation sell pressure.
- Recommended liquidation normalization: divide signed liquidation notional by current/open-interest notional when available, use the signed value only for direction, and use robust-normalized absolute magnitude for color intensity. This keeps strength comparable across symbols and avoids color inversion when a positive delta is merely smaller than usual.
- Open interest should be treated as low-frequency REST/statistics context, not a WebSocket stream or hot-path input.
- Recommended OI bucket value: `sumOpenInterestValue[t] - sumOpenInterestValue[t-1]`, with relative delta `delta / previousOpenInterestValue`; green/red direction comes from the delta sign, while brightness comes from robust-normalized absolute magnitude.
- Default frame should be 15 minutes with a 5-minute option; display duration should stay around 150 minutes, so tile count becomes 10 at 15m and 30 at 5m.
- Normalization history should be longer than the visible 150 minutes, preferably about 24 hours per timeframe, bootstrapped from Binance open-interest history. Liquidation normalization needs warm-up or persisted local history because Binance does not provide equivalent historical liquidation buckets through this stream.
- These context modules must remain separate visual modules first and must not change TC-DN-HOFI3 signal logic without a separate tested rule.
- Implementation plan written to `docs/superpowers/plans/2026-05-26-liquidation-open-interest-context-modules.md`.

## Liquidation And Open Interest Context Modules Implementation Todo

- [x] Add Domain context models, frame helpers, and robust magnitude normalizer.
- [x] Add Domain bucket calculators.
- [x] Add Application context source/session boundary without referencing Infrastructure.
- [x] Add Infrastructure Binance mapping for liquidation stream and open-interest history.
- [x] Add Desktop config, ViewModel projection, and WPF liquidation/OI strips.
- [x] Run full tests, publish, and live smoke on active symbols.

## Liquidation And Open Interest Context Modules Results

- HOFI/TFI formula unchanged; no changes were made to `TcDnHofi3Engine`, `IndicatorParameters`, `IndicatorSample`, or `SignalState`.
- Added separate Domain context models, robust magnitude normalization, liquidation bucket calculation, and OI delta bucket calculation.
- Added Application `IContextDataSource`, `ContextModuleSession`, and `ContextModuleSample`; `CryptoIndicatorApp.Application` still references only `CryptoIndicatorApp.Domain`.
- Added Infrastructure Binance context mapping, USDS-M OI history REST loading, and `<symbol>@forceOrder` liquidation stream subscription.
- Added Desktop context config defaults, 15m/5m frame selection, ViewModel tile projection, and compact WPF strips for liquidations and open interest.
- Added `tools/LiveDryRun --context-only` smoke mode for non-GUI verification of OI history and liquidation subscription.
- Verification: `dotnet test CryptoIndicatorApp.sln --no-restore` passed `64/64`; existing `NU1900` warning remains because NuGet vulnerability metadata could not be loaded.
- Publish: `dotnet publish CryptoIndicatorApp.Desktop\CryptoIndicatorApp.Desktop.csproj -c Release -o publish\desktop --no-restore` completed with 0 errors and existing `NU1900` warning.
- Sandbox context smoke failed on Binance SSL/network, then succeeded outside sandbox.
- Live context smoke at 15m:
  `ESPORTSUSDT`, `XANUSDT`, and `PLAYUSDT` each loaded 10 OI tiles, had 10 non-zero OI tiles, opened liquidation subscription successfully, and had 0 liquidation tiles because no force-order events arrived during the 4-second windows.
- Additional 5m smoke on `XANUSDT` loaded 30 OI tiles, had 30 non-zero OI tiles, opened liquidation subscription successfully, and had 0 liquidation events during the 4-second window.
- Known limitation: Binance liquidation stream reports only the largest liquidation snapshot per 1000 ms interval, not complete market liquidation history.

## Context Refresh And TFI Chart Visibility Todo

- [x] Record user-side WPF smoke: published app starts, symbol selection works, Start Live works, context strips render, and 15m/5m switching works.
- [x] Add config-only periodic OI refresh for the context module, defaulting to a conservative one-symbol interval rather than any hot-path REST dependency.
- [x] Keep OI refresh de-duplicated by Binance OI timestamp so repeated REST polls do not create fake delta tiles.
- [x] Add Application tests proving OI refresh can emit updated context samples even when no liquidation events arrive.
- [x] Add clearer Desktop label/tooltip for liquidations: the stream is observed liquidation snapshots, not complete liquidation volume.
- [x] Add tooltip/source wording that Binance sends only the largest liquidation order per symbol within a 1000 ms interval and sends no event when none occurs.
- [x] Improve TFI chart visibility as a Desktop-only rendering change; do not change `RollingTradeFlow`, `TcDnHofi3Engine`, thresholds, or signal logic.
- [x] Prefer threshold-normalized visual scaling for TFI: plot/display a chart value derived from `TFI / ThetaTfi`, clipped for readability, while keeping the raw TFI metric text unchanged.
- [x] Add chart rendering tests so a threshold-level TFI move produces a visible vertical deviation and does not flatten against the center line when `Z_OFI` has larger magnitude.
- [x] Run Desktop tests, Application tests, full solution tests, and publish.
- [x] User-side manual WPF smoke: verify OI refresh status over time, liquidation tooltip wording, and `TFI/Theta` chart visibility.

## Context Refresh And TFI Chart Visibility Notes

- Binance OI statistics are REST data with period choices including `5m` and `15m`, most-recent-data behavior when no time range is sent, and an IP limit documented by Binance. Polling belongs only in the slow context module, not in the HOFI/TFI hot path.
- For one selected symbol, a simple configurable OI refresh interval is enough for MVP. Polling faster than the selected OI frame is mostly redundant, so refresh should be conservative and timestamp-deduplicated.
- The liquidation strip should be named/tooled as observed forced-order snapshots. It must not imply full liquidation history or total liquidation volume.
- Current chart geometry scales `Z_OFI` and raw `TFI` against a shared max absolute value. Since TFI is bounded near `[-1, 1]` and the useful confirmation threshold is around `0.15`, it becomes visually flat when `Z_OFI` spikes to several z-score units.
- Do not "sharpen" TFI by changing the formula or lowering `ThetaTfi`. The lowest-risk visual fix is to plot TFI in threshold units, e.g. `TFI / ThetaTfi`, optionally clipped around `[-3, 3]`. Then `+1` means long-side TFI confirmation threshold and `-1` means short-side confirmation threshold.
- If the combined chart still feels visually crowded after threshold-normalized TFI, the next UI option is a split two-lane chart with shared time axis: top `Z_OFI`, bottom `TFI confirmation strength`.

## Context Refresh And TFI Chart Visibility Results

- Added Application `IContextRefreshClock` and periodic OI refresh support in `ContextModuleSession`.
- Desktop config now has `Dashboard.Context.OpenInterestRefreshSeconds`, default `60`; values below zero normalize to disabled `0`.
- OI refresh is timestamp-deduplicated: repeated Binance OI snapshots with the same latest timestamp do not emit fake context samples.
- Added Application tests for OI refresh without liquidation events and for same-timestamp deduplication.
- Added Desktop labels/tooltips describing liquidation data as observed force-order snapshots, not total liquidation volume.
- Changed chart rendering only: TFI line is plotted as `TFI / ThetaTfi`, clipped to `+/-3`; raw TFI metric text and signal logic are unchanged.
- Added chart geometry tests proving a threshold-level TFI move is visibly displaced even next to larger `Z_OFI`.
- Verification: Application tests passed `10/10`; Desktop tests passed `19/19`; full solution tests passed `68/68`.
- Publish: `dotnet publish CryptoIndicatorApp.Desktop\CryptoIndicatorApp.Desktop.csproj -c Release -o publish\desktop --no-restore` succeeded; published config contains `OpenInterestRefreshSeconds: 60`.
- Existing warning remains: `NU1900` because NuGet vulnerability metadata could not be loaded from `https://api.nuget.org/v3/index.json`.
- Codex-side WPF launch for manual smoke was attempted after publish, but the launched process did not expose a top-level WPF window to UI Automation / `MainWindowHandle`; no crash event was recorded. Treat the visual WPF smoke as still user-side.
- Re-verified after the launch attempt: `dotnet test CryptoIndicatorApp.sln --no-restore` passed `68/68`; published config still contains `OpenInterestRefreshSeconds: 60` and `ThetaTfi: 0.15`.

## Raw TFI Chart Rollback Todo

- [x] Record user-side smoke: OI strip stays healthy after refresh, and observed liquidations update/work.
- [x] Add a Desktop regression test proving the chart uses raw TFI scaling rather than threshold-normalized `TFI/Theta`.
- [x] Roll back the Desktop chart legend and rendering to raw `TFI` without changing `RollingTradeFlow`, thresholds, or signal logic.
- [x] Run Desktop tests, full solution tests, and publish.

## Raw TFI Chart Rollback Notes

- User-side smoke found `TFI/Theta` more visible but too chaotic: it obscures `Z_OFI` and hurts chart readability.
- The correct MVP rollback is Desktop-only raw TFI rendering. A future alternative, if TFI context is still needed, is a split two-lane chart instead of overlaying threshold-normalized TFI on the same axis.

## Raw TFI Chart Rollback Results

- Reverted the 60-second chart legend from `TFI/Theta` to raw `TFI`.
- Reverted `MainWindow.RenderChart()` to plot raw `sample.Tfi` on the same shared scale as `Z_OFI`.
- Removed the unused `ToTfiConfirmationStrength` chart helper.
- Added a Desktop regression test that rejects `TFI/Theta` in the chart contract and verifies raw TFI stays visually subtle next to larger `Z_OFI`.
- HOFI/TFI calculation, thresholds, signal logic, and metric-strip raw TFI values were not changed.
- Verification: Desktop tests passed `20/20`; full solution tests passed `69/69`.
- Publish: `dotnet publish CryptoIndicatorApp.Desktop\CryptoIndicatorApp.Desktop.csproj -c Release -o publish\desktop --no-restore` succeeded.
- Existing warning remains: `NU1900` because NuGet vulnerability metadata could not be loaded from `https://api.nuget.org/v3/index.json`.

## Chart Zero Line And Visual Color Tests Todo

- [x] Record design requirement: red/green should not be used as static OFI/TFI identity colors because both series can support long and short context.
- [x] Record design requirement: add a neutral gray zero line for orientation on the 60-second chart.
- [x] Add a red Desktop test for zero-line geometry/contract.
- [x] Implement the neutral zero line as Desktop-only chart rendering.
- [x] Run Desktop tests, full solution tests, and publish.
- [ ] Future visual-test slice: compare 4-color signed line variants where positive and negative OFI/TFI segments use distinct colors, with OFI opaque/dominant and TFI lower-opacity/secondary.

## Chart Zero Line And Visual Color Tests Notes

- Four-color signed rendering should not be added by just recoloring the two existing polylines. WPF `Polyline` has one stroke, so signed coloring needs segmented geometry or multiple per-sign polylines.
- The visual design test should evaluate contrast on a white chart background, zero-line visibility, OFI dominance, TFI transparency, and color semantics that do not imply static long/short identity for the whole series.
- Candidate palette families to test later: OFI positive in blue/cyan or emerald, OFI negative in ruby/rose; TFI positive in mint/teal with opacity, TFI negative in amber/orange/rose with opacity. Static red/green line identity should remain avoided.

## Chart Zero Line And Visual Color Tests Results

- Added a neutral gray dashed zero line to the 60-second chart as a Desktop-only orientation aid.
- The zero line is rendered as a separate WPF `Line` behind the OFI/TFI polylines, not as market data and not as an indicator formula change.
- Added Desktop tests for zero-line geometry and XAML/code-behind chart contract.
- Recorded future visual-test requirements for signed 4-color rendering: separate positive/negative colors per series, OFI opaque/dominant, TFI lower-opacity/secondary, and no static red/green identity colors for whole OFI/TFI lines.
- Verification: Desktop tests passed `22/22`; full solution tests passed `71/71`.
- Publish: `dotnet publish CryptoIndicatorApp.Desktop\CryptoIndicatorApp.Desktop.csproj -c Release -o publish\desktop --no-restore` succeeded.
- Existing warning remains: `NU1900` because NuGet vulnerability metadata could not be loaded from `https://api.nuget.org/v3/index.json`.

## MVP Implementation Results

- Implementation started after user approved the plan.
- Current folder is not a git repository, so git worktree isolation and commits are unavailable unless git is initialized later.
- Infrastructure red/green completed for JSONL event store and minimal Binance adapter boundaries.
- JSONL replay fails fast on malformed rows and unsupported schema versions.
- Minimal Binance boundary currently covers USDS-M stream names and raw field mapping into Domain events; full live socket source remains pending.
- `dotnet test CryptoIndicatorApp.sln --no-restore` passed: Domain.Tests 9, Infrastructure.Tests 8.
- `dotnet build CryptoIndicatorApp.sln --no-restore` passed after fixing the Desktop `Application` namespace collision.
- Application boundary decision updated: `Application` must not reference `Infrastructure` or concrete `JsonlMarketEventStore`; JSONL/Binance composition belongs in Desktop or another outer composition layer.
- Application slice implemented with `IMarketEventSource`, `IMarketEventRecorder`, shared `IndicatorPipeline`, `LiveIndicatorSession`, `ReplayIndicatorSession`, and `ChartSampleBuffer`.
- Added `CryptoIndicatorApp.Application.Tests`: 6 tests cover no Infrastructure reference, shared live/replay output, deterministic replay sequence, live raw-event recording, record-before-process order, and 60-second chart retention.
- `dotnet test CryptoIndicatorApp.sln --no-restore` passed: Domain.Tests 9, Application.Tests 6, Infrastructure.Tests 8.
- `dotnet build CryptoIndicatorApp.sln --no-restore` passed with 1 warning: `NU1900` because NuGet vulnerability data could not be loaded from `https://api.nuget.org/v3/index.json`; escalated restore did not clear it.
- Next WPF/composition slice intentionally excludes the full Binance live WebSocket source; that remains a separate infrastructure slice because it needs current Binance.Net API verification and live-data dry run.
- WPF/composition slice added `CryptoIndicatorApp.Desktop.Tests` and registered it in the solution.
- Desktop now composes Infrastructure at the outer boundary via JSONL source/recorder adapters; Application still references only Domain.
- Added config-only `appsettings.json` for symbol, mode, replay path, recording path, chart window, and indicator parameters.
- Added a minimal WPF dashboard with symbol/mode/status, recording/replay paths, book health, latency, `Z_OFI`, TFI, signal, last update ID, and native Polyline chart for the last 60 seconds.
- Red/green verification: Desktop tests first failed on missing `Composition`, `Configuration`, and `ViewModels`, then passed after implementation.
- `dotnet test CryptoIndicatorApp.sln --no-restore` passed: Domain.Tests 9, Application.Tests 6, Infrastructure.Tests 8, Desktop.Tests 3.
- `dotnet build CryptoIndicatorApp.sln --no-restore` passed with 0 errors and 3 `NU1900` warnings from unavailable NuGet vulnerability data.
- Verified `CryptoIndicatorApp.Application` project references only `CryptoIndicatorApp.Domain`; the only source-text hit for `CryptoIndicatorApp.Infrastructure` under Application is the existing boundary test assertion.
- Confirmed `appsettings.json` is copied to `CryptoIndicatorApp.Desktop\bin\Debug\net8.0-windows`.
- Live Binance source slice added `IBinanceUsdFuturesMarketDataClient`, `BinanceUsdFuturesLiveMarketEventSource`, and `BinanceNetUsdFuturesMarketDataClient`.
- Live source subscribes to public depth and aggTrade streams first, loads the REST depth snapshot only for initial sync/resync, emits the snapshot before buffered depth updates, drops stale buffered updates, and emits a new snapshot after a detected `pu` gap.
- Desktop now adapts the Infrastructure live source through `BinanceLiveMarketEventSource` and uses `LiveIndicatorSession` with JSONL recording in `Live` mode.
- Application still references only Domain; `dotnet list CryptoIndicatorApp.Application/CryptoIndicatorApp.Application.csproj reference` shows only `CryptoIndicatorApp.Domain`.
- Full solution verification passed after the live-source slice: Domain.Tests 9, Application.Tests 6, Infrastructure.Tests 11, Desktop.Tests 4.
- `dotnet build CryptoIndicatorApp.sln --no-restore` passed with 0 errors and the existing 3 `NU1900` warnings about unavailable NuGet vulnerability data.
- Live GUI/network dry run was deferred; no Binance API keys are needed for the current public market-data slice.
- User-requested manual ticker refresh and optional ShadowSocks/local-proxy support were recorded as post-live MVP enhancements instead of being mixed into the sequence/snapshot slice.
- Proxy support source-check found `CryptoExchange.Net` `ApiProxy`, `ExchangeOptions.Proxy`, and WebSocket proxy hooks in the installed 11.1.0 XML docs; GitHub source shows REST handler builds a `WebProxy` from `proxy.Host` and `proxy.Port`.
- Added config-only proxy options under `Dashboard.Proxy`: `Enabled`, `Type`, `Host`, `Port`.
- Implemented `BinanceConnectionOptions` / `BinanceProxyOptions` in Infrastructure and wired them through Desktop live composition into `BinanceNetUsdFuturesMarketDataClient`.
- Supported proxy type is currently `Http`; unsupported values such as `Socks5` fail fast with a clear error instead of pretending SOCKS is supported by the current library surface.
- For HTTP proxy safety, Infrastructure normalizes a bare host such as `127.0.0.1` to `http://127.0.0.1` before creating the `ApiProxy`, matching the library's URI construction path.
- Proxy slice verification passed: `dotnet test CryptoIndicatorApp.sln --no-restore` passed 34 tests; `dotnet build CryptoIndicatorApp.sln --no-restore` passed with 0 errors and the existing 3 `NU1900` warnings.
- Added `tools/LiveDryRun`, a non-GUI CLI harness that records public Binance USDS-M market events to JSONL and immediately replays them through `ReplayIndicatorSession`.
- Sandbox live run failed at WebSocket subscription with `CantConnectError.UnableToConnect`; the same command succeeded outside the sandbox.
- Live dry run command recorded `C:\Users\Steven Owl\Desktop\PRJCT-INDIC\recordings\live-dry-run-20260525-141619.jsonl`: 92 JSONL events, 69 live samples, 69 replay samples, sample counts matched, final book synced, resync count 0.
- Full post-dry-run verification passed: `dotnet test CryptoIndicatorApp.sln --no-restore` passed 34 tests; `dotnet build CryptoIndicatorApp.sln --no-restore` passed with 0 errors and 4 existing `NU1900` warnings.

## Skill Import Todo

- [x] Inspect downloaded `agent-skills-main` folder.
- [x] Identify candidate Codex skills under `skills/*/SKILL.md`.
- [x] Check for install-name conflicts and obvious trigger risks.
- [x] Copy approved skill folders into `C:\Users\Steven Owl\.codex\skills`.
- [x] Verify installed skill folders and record restart requirement.

## Skill Import Results

- Source validator passed: 23 skills checked, 0 errors, 0 warnings.
- Installed 23 imported skills into `C:\Users\Steven Owl\.codex\skills`.
- Verified each non-system installed skill folder has `SKILL.md`.
- Codex restart is required before the newly installed skills appear in future sessions.
- Caution: several imported skills overlap with existing Superpowers workflows, so future sessions may trigger more process-heavy behavior.

## Skill Dedup Audit Todo

- [x] Define audit scope: imported global skills, existing Superpowers skills, and project `AGENTS.md`.
- [x] Inventory imported skill triggers and likely overlap.
- [x] Rank skills by usefulness for the TC-DN-HOFI3 Windows app.
- [x] Identify keep / optional / remove candidates.
- [x] Update `AGENTS.md` only with durable project guidance, not generic skill noise.
- [x] Verify resulting files and summarize deletion recommendation separately.

## Skill Dedup Audit Results

- Added `tasks/skill-audit.md` with keep / later / remove-or-avoid recommendations for all 23 imported skills.
- Updated `AGENTS.md` with a project-specific skill selection policy.
- Did not delete global skill folders; pruning should be confirmed separately because it changes Codex behavior outside this project.

## AGENTS Merge Todo

- [x] Read current project `AGENTS.md`.
- [x] Read downloaded `C:\Users\MECHREVO\Downloads\Telegram Desktop\agents.md`.
- [x] Separate useful project rules from web/WSL/Docker-specific rules.
- [x] Update project `AGENTS.md` with adapted combined guidance.
- [x] Verify the resulting `AGENTS.md` for conflicts and obsolete paths.

## AGENTS Merge Review

- The downloaded file is useful as a strict engineering-policy source, but it is web-project biased.
- Do not import Ubuntu WSL, Docker-only, CodeRabbit, JavaScript/JSDoc, frontend routing, database, or "no MVPs" rules into this WPF/.NET Binance analytics project.
- Adapt useful ideas instead: Russian responses, explicit dependencies, source-file size guardrails, fail-fast required config/data, human-readable errors, bounded retries, risk-based tests, external API mocks, and clean handling of legacy compatibility.
- Updated `AGENTS.md` with project-specific versions of those rules.
- Replaced the stale `C:\Users\Steven Owl\.codex\skills\binance-indicator-dev\SKILL.md` fallback with `%USERPROFILE%\.codex\skills\binance-indicator-dev\SKILL.md`.
- Verified the resulting `AGENTS.md` is 134 lines and does not contain imported Ubuntu WSL, CodeRabbit, JSDoc, Prisma, React, npm migration, or production-branch Docker deployment rules.
- Historical note, superseded by "Laptop Continuity And Skill Recovery Results": at this point `$binance-indicator-dev` was missing from `C:\Users\MECHREVO\.codex\skills\binance-indicator-dev\SKILL.md`; do not treat this as current skill status.

## Agent Memory Architecture Todo

- [x] Record implementation scope: memory is project tooling only, not application runtime.
- [x] Add red tests for memory contract files, retrieval facts, staleness rules, and refresh script behavior.
- [x] Add a human-authored memory contract under `docs/memory/`.
- [x] Add a generated-memory schema under `docs/memory/`.
- [x] Add a manual memory refresh script that writes only ignored generated output.
- [x] Add retrieval/staleness test commands for known project facts.
- [x] Run narrow memory tests, full solution tests, and build.
- [x] Record implementation results and limitations.

## Agent Memory Architecture Results

- Implemented memory as project tooling only; no WPF, Binance pipeline, formula, threshold, filter, cadence, or runtime config behavior was changed.
- Added `docs/memory/contract.md` with source priority, human/generated boundaries, node/edge schema, staged retrieval protocol, staleness rules, and tool strategy.
- Added `docs/memory/generated-memory.schema.json` for generated memory indexes.
- Added ADR `docs/decisions/0002-agent-memory-contract.md`.
- Added manual refresh script `scripts/memory-refresh.ps1`; it writes `docs/memory/generated/project-memory-index.json`, which remains ignored by Git.
- Refresh output currently records 6 curated nodes, 5 edges, a source-file hash index, and tool availability for `gbrain`, `graphify`, `mem0`, and `graphiti`.
- Added `MemoryContractTests` under Infrastructure tests for retrieval facts, schema enums/required metadata, staleness rules, ignored generated output, and build-artifact exclusion from the generated index.
- TDD red/green was used: initial tests failed on missing contract/schema/script; later regression failed on invalid `valid_until` and noisy `bin/obj` source indexing before the script fix.
- Verification passed: `dotnet test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter MemoryContractTests` passed `5/5`.
- Verification passed: `dotnet test CryptoIndicatorApp.sln --no-restore` passed `76/76`.
- Build verification passed: `.\.dotnet\dotnet.exe build CryptoIndicatorApp.sln --no-restore` completed with 0 warnings and 0 errors.
- Historical note, superseded by "Laptop Continuity And Skill Recovery Results": at this point `$binance-indicator-dev` was unavailable at `%USERPROFILE%\.codex\skills\binance-indicator-dev\SKILL.md`; this is not the current skill status.

## GBrain CLI/API Spike Todo

- [x] Confirm upstream GBrain repository, install/runtime requirements, CLI entrypoint, and documented local setup command from current source.
- [x] Check whether `gbrain` and its required runtime are available in the current Windows environment.
- [x] Record the confirmed command/API surface and local availability in `docs/memory/`.
- [x] Update memory open questions so GBrain is not mixed with still-unverified Graphify details.
- [x] Run the narrow memory contract tests after documentation changes.
- [x] Record spike results and remaining risks.

## GBrain CLI/API Spike Results

- Confirmed upstream `garrytan/gbrain` exists and documents `gbrain init --pglite` for local PGlite setup.
- Confirmed package metadata: CLI entrypoint is `gbrain`, runtime requires Bun, and upstream documents standalone install via `bun install -g github:garrytan/gbrain`.
- Confirmed upstream Codex MCP path: `codex mcp add gbrain -- gbrain serve`.
- Local environment check found neither `gbrain` nor `bun` in PATH, so GBrain is verified upstream but not currently usable locally.
- Added `docs/memory/gbrain-spike.md` and updated `docs/memory/contract.md`, `docs/memory/README.md`, and `docs/memory/open-questions.md`.
- Added a MemoryContractTests guard so retrieval does not collapse "confirmed upstream" into "installed/current local tool".
- Ran `scripts/memory-refresh.ps1`: generated ignored index with 6 nodes, 5 edges, and 141 indexed files.
- Verification passed: `.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter MemoryContractTests` passed `6/6`.
- Remaining risks: local Windows install, `gbrain doctor`, runtime MCP tool inspection, import policy, and export/backup format are still unverified.

## Hindsight Memory Candidate Todo

- [x] Confirm upstream Hindsight repository and documented Codex/CLI/MCP surfaces from current sources.
- [x] Add `docs/memory/hindsight-spike.md` with local availability, curated import policy, and auto-retain restriction.
- [x] Mark GBrain as historical/secondary instead of the preferred external memory candidate.
- [x] Update `docs/memory/contract.md`, `docs/memory/README.md`, and `docs/memory/open-questions.md`.
- [x] Add/adjust `MemoryContractTests` so retrieval preserves Hindsight preferred status and GBrain historical status.
- [x] Run `scripts/memory-refresh.ps1` and the narrow memory contract tests.
- [x] Record spike results and remaining install-mode decision.

## Hindsight Memory Candidate Results

- Confirmed upstream `vectorize-io/hindsight` exists and is a Python project with `hindsight-api`, `hindsight-embed`, `hindsight-cli`, Codex integration, and MCP docs.
- Confirmed package metadata: `hindsight-api` and `hindsight-embed` require Python `>=3.11`; `hindsight-cli` builds a Rust binary named `hindsight`.
- Added `docs/memory/hindsight-spike.md` with upstream sources, local availability, MVP decision, curated import allowlist, auto-retain restriction, and remaining gaps.
- Updated `docs/memory/gbrain-spike.md` so GBrain is historical/secondary, not the roadmap-preferred external memory candidate.
- Updated `docs/memory/contract.md`, `docs/memory/README.md`, `docs/memory/open-questions.md`, `.gitignore`, and `tasks/lessons.md`.
- Local environment check found no `hindsight`, `hindsight-api`, `uvx`, or Docker; `python --version` resolves only to the Windows Store alias, so local Hindsight installation remains a separate install spike.
- Ran `scripts/memory-refresh.ps1`: generated ignored index with 6 nodes, 5 edges, and 142 indexed files.
- Verification passed: `.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter MemoryContractTests` passed `7/7`.
- Remaining decision: choose Hindsight install mode first: Cloud, Docker, Python/uvx embedded daemon, or external PostgreSQL.

## Hindsight Curated Import Todo

- [x] Commit the current Hindsight/GBrain memory-roadmap layer before adding import tooling.
- [x] Add a failing memory contract test for a curated Hindsight import manifest/script allowlist.
- [x] Verify the failing test rejects missing curated import tooling for the expected reason.
- [x] Add minimal curated import tooling that lists only approved source files.
- [x] Ensure denylisted sources stay excluded: `recordings/*.jsonl`, `docs/memory/generated/`, secrets, build artifacts, and local proxy details.
- [x] Update memory docs/open questions with the pre-install import rule and keep Codex auto-retain disabled.
- [x] Run `scripts/memory-refresh.ps1`, narrow memory tests, and review uncommitted changes.
- [x] Record results and remaining install-spike decision.

## Hindsight Curated Import Results

- Committed the prior Hindsight/GBrain roadmap layer as `3b30b2a docs: prefer hindsight memory candidate`.
- Added `CryptoIndicatorApp.Infrastructure.Tests/HindsightCuratedImportTests.cs` with a red/green test for the curated import manifest script.
- Red verification failed for the expected reason: missing `scripts/hindsight-curated-import.ps1`.
- Added `scripts/hindsight-curated-import.ps1`; it writes only `docs/memory/generated/hindsight-curated-import-manifest.json` and does not install Hindsight, call Hindsight APIs, start daemons, or enable Codex hooks.
- The import allowlist is `docs/memory/*.md`, `docs/decisions/*.md`, `docs/formulas.md`, `AGENTS.md`, and `tasks/lessons.md`.
- The denylist excludes raw JSONL recordings, generated memory exports, secrets, local proxy details, build artifacts, and unreviewed experiment dumps.
- Updated `docs/memory/contract.md`, `docs/memory/README.md`, `docs/memory/hindsight-spike.md`, and `docs/memory/open-questions.md` so the current status is manifest-only pre-install tooling.
- `scripts/hindsight-curated-import.ps1` generated `13` curated file entries with Codex auto-retain disabled.
- `scripts/memory-refresh.ps1` generated the ignored index with `6` nodes, `5` edges, and `144` indexed files.
- Verification passed: `.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore` passed `26/26`.
- Full solution build/test was not rerun for this docs/script/test-only memory slice.
- Remaining install-spike decision: use Python/uvx embedded daemon first unless a later constraint requires Cloud, Docker, or external PostgreSQL.

## Laptop Continuity And Skill Recovery Todo

- [x] Confirm current skill status: `%USERPROFILE%\.codex\skills\binance-indicator-dev\SKILL.md` is missing on this laptop.
- [x] Confirm current repo/tooling status: `.git` exists in the project folder, but `git` is not available in PATH on this laptop.
- [x] Identify existing durable context sources: `AGENTS.md`, `TC-DN-HOFI3.md`, `docs/*`, `docs/memory/*`, `tasks/todo.md`, `tasks/lessons.md`, current code/tests/config.
- [x] Install or locate Git for Windows and GitHub tooling on the laptop; verify `git status`, `git log`, `git remote -v`, and remote push health before relying on repository history.
- [x] Reconstruct `binance-indicator-dev` as a versioned project artifact, not as local-only state. Preferred source path: `skills/binance-indicator-dev/SKILL.md`; installed copy path: `%USERPROFILE%\.codex\skills\binance-indicator-dev\SKILL.md`.
- [x] Build the reconstructed skill from repo facts only: project scope/rules from `AGENTS.md`, formula and filters from `TC-DN-HOFI3.md` and `docs/formulas.md`, architecture from `docs/architecture.md` and ADRs, memory/retrieval rules from `docs/memory/contract.md`, and implementation boundaries from current code/tests.
- [x] Keep `SKILL.md` concise and procedural; move longer reference material into `skills/binance-indicator-dev/references/` only if it prevents re-reading large docs every session.
- [x] Add a small install script such as `scripts/install-project-skills.ps1` that copies the versioned skill into `%USERPROFILE%\.codex\skills` and fails fast if the source skill is missing.
- [x] Add a lightweight repository test or script check that verifies the project skill source exists, has valid frontmatter, and includes the critical guardrails: no REST hot path, live/replay shared event types, no formula/threshold/cadence change without approval, Application must not reference Infrastructure, JSONL schema/versioning discipline.
- [x] Validate the installed skill after copy and record that Codex restart may be required before the skill appears in the active skills list.
- [x] Create a durable session-handoff habit: after meaningful work, update `tasks/todo.md` results, `tasks/lessons.md` after feedback/fixes, and `docs/memory/*` or a dated handoff note only for decisions/facts that should survive chat loss.
- [x] Run `scripts/memory-refresh.ps1` after durable docs/code changes and verify generated memory remains under ignored `docs/memory/generated/`.
- [x] Push the repository after each meaningful work session; if remote push is unavailable, create an encrypted/off-machine backup or `git bundle` until GitHub access is restored.
- [x] Do not treat ChatGPT/Codex chat history as canonical project storage. If an old thread contains important context, summarize the decision/evidence into repo docs instead of depending on account sync.
- [x] Keep secrets, local proxy settings, raw JSONL recordings, and generated memory caches out of Git; commit reviewed summaries and reproducible scripts instead.

## Laptop Continuity And Skill Recovery Review

- Current plan deliberately avoids recreating the lost desktop state as another laptop-local-only dependency.
- The source of truth should become the project repository; `%USERPROFILE%\.codex\skills` is only an installation target.
- Old chat history and the original desktop skill remain unavailable for roughly 3 months, so reconstruction must be treated as best-effort from committed/project files, not as exact recovery.

## Laptop Continuity And Skill Recovery Results

- Git for Windows and GitHub CLI were already installed at `C:\Program Files\Git\cmd\git.exe` and `C:\Program Files\GitHub CLI\gh.exe`; the current Codex process PATH is stale, but Machine PATH already contains both tools. Use absolute paths in this session or restart Codex/terminal to refresh PATH.
- Verified repository state with Git: branch `main` tracks `origin/main`; recent commits include `807d057 docs: record repository publication`; remote is `https://github.com/striges88-bit/tc-dn-hofi3-dashboard.git`.
- Verified GitHub CLI auth for `striges88-bit` and remote `origin/main` via `git ls-remote --heads origin main`.
- Created versioned project skill at `skills/binance-indicator-dev/SKILL.md` with UI metadata at `skills/binance-indicator-dev/agents/openai.yaml`.
- Added `scripts/install-project-skills.ps1`; it installs the versioned skill to `%USERPROFILE%\.codex\skills\binance-indicator-dev` and refuses unsafe destination paths.
- Installed the skill to `C:\Users\MECHREVO\.codex\skills\binance-indicator-dev`; Codex restart may be required before it appears in the active skill list.
- Added `scripts/verify-project-skills.ps1`; it checks frontmatter, default prompt metadata, installed-copy equality, and critical project guardrails.
- Official skill validator initially failed because bundled Python lacked `PyYAML`; installed `PyYAML` into ignored `.tools\python-packages` and reran `quick_validate.py`, which passed.
- Fixed `scripts/memory-refresh.ps1` root detection because default parameter evaluation could see an empty `$PSScriptRoot` under `powershell.exe -File`; applied the same safer pattern to the new skill scripts.
- Verification passed: `scripts/verify-project-skills.ps1 -CheckInstalled`, `quick_validate.py`, `scripts/memory-refresh.ps1`, `MemoryContractTests` `5/5`, full solution tests `76/76`, and solution build with `0` warnings and `0` errors.
- Committed and pushed continuity tooling to `origin/main` in commit `111760d chore: add project continuity tooling`.

## Hindsight Python/Uvx Install Spike Todo

- [x] Confirm current upstream Python/uvx embedded daemon commands and note any doc inconsistencies before installing or retaining memory.
- [x] Add a failing test for a safe install-spike report/script contract.
- [x] Add a minimal install-spike script that probes prerequisites and writes only ignored generated output.
- [x] Keep Codex auto-retain disabled and do not import curated files during the install spike.
- [x] Run local prerequisite checks; only run network/install commands as an explicit spike step.
- [x] Update memory docs/open questions with actual install status, blocked items, and next command.
- [x] Run narrow memory/tooling tests and `scripts/memory-refresh.ps1`.
- [x] Record install-spike results and remaining risk.

## Hindsight Python/Uvx Install Spike Results

- Added `CryptoIndicatorApp.Infrastructure.Tests/HindsightInstallSpikeTests.cs` with a red/green guard for a safe install-spike script and docs.
- Red verification failed for the expected reason: missing `scripts/hindsight-install-spike.ps1` and `docs/memory/hindsight-install-spike.md`.
- Added `scripts/hindsight-install-spike.ps1`; default mode only probes local tools and writes ignored `docs/memory/generated/hindsight-install-spike-report.json`.
- Added `docs/memory/hindsight-install-spike.md` and updated `docs/memory/hindsight-spike.md`, `docs/memory/contract.md`, `docs/memory/open-questions.md`, and `docs/memory/README.md`.
- Installed `uv` user-scoped through WinGet package `astral-sh.uv`; installed version is `uv 0.11.25`.
- Current Codex PATH stayed stale after WinGet install, so the spike script now discovers `uv.exe` and `uvx.exe` under `%LOCALAPPDATA%\Microsoft\WinGet\Packages\astral-sh.uv_*`.
- `uvx hindsight-embed --help` succeeded and downloaded managed `cpython-3.14.6-windows-x86_64-none` plus package dependencies.
- Embedded CLI help confirms profile, daemon, UI/control, `memory retain`, `memory recall`, `memory reflect`, and `bank list` surfaces.
- `hindsight-embed profile show -o json` reports default config under `%USERPROFILE%\.hindsight\embed` and port `8888`.
- `hindsight-embed daemon status` reports daemon not running and exits with code `1`.
- `hindsight-embed memory retain --help` and `hindsight-embed bank list --help` fail before help output with `LLM API key is required`; no `OPENAI_API_KEY`, `HINDSIGHT_API_TOKEN`, or `HINDSIGHT_API_LLM_API_KEY` was present in this Codex process.
- Codex auto-retain remains disabled; curated import was not executed; daemon was not started; no `retain` or `retain-files` command was run.
- Verification passed: `.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore` passed `28/28`.
- `scripts/memory-refresh.ps1` generated the ignored index with `6` nodes, `5` edges, and `147` indexed files.
- `scripts/hindsight-install-spike.ps1 -ProbeUvxHelp` regenerated the ignored install-spike report after tests.
- Remaining next step: approve secret-backed Hindsight env handling, then create an explicit project profile and test daemon/MCP endpoint behavior before any curated import.

## Hindsight Profile And Daemon Smoke Todo

- [x] Create a new OpenAI project API key through the secure encrypted flow without printing the plaintext key.
- [x] Store the key only in ignored repo-local env storage under `.hindsight/`.
- [x] Decide the project secret-backed env policy and document it without recording secret values.
- [x] Create an explicit Hindsight project profile for `tc-dn-hofi3` using the ignored env file.
- [x] Start/check the embedded daemon and confirm the actual local endpoint before any import.
- [x] Probe the MCP endpoint/bank surface without retaining or importing project files.
- [x] Update Hindsight docs/open questions with real profile, daemon, and MCP results.
- [x] Run narrow memory tooling tests and refresh generated memory.
- [x] Record results, blockers, and the next safe command.

## Hindsight Profile And Daemon Smoke Results

- Created OpenAI project API key `TC-DN-HOFI3 Hindsight` through encrypted setup and wrote it only to ignored `.hindsight/tc-dn-hofi3.env`; plaintext was not printed.
- Secret policy: keep Hindsight/OpenAI secrets in ignored `.hindsight/` env files, load them only into process environment, and do not pass them through Hindsight `--env`, `profile set-env`, shell history, or committed config.
- Created explicit Hindsight profile `tc-dn-hofi3` on port `9077`; Hindsight stores profile config at `%USERPROFILE%\.hindsight\profiles\tc-dn-hofi3.env`.
- First daemon start opened a visible/hanging launcher and then hit Hindsight's 180s timeout while downloading/initializing heavy Python dependencies, local embeddings/reranker, embedded PostgreSQL, and migrations.
- Stopped the visible launcher process tree; later hidden startup completed enough for the API process to become healthy.
- Confirmed daemon status: `hindsight-embed -p tc-dn-hofi3 daemon status` reports `Daemon Running`.
- Confirmed endpoints: `http://127.0.0.1:9077/health`, `/mcp/`, and `/metrics` return HTTP `200`; `/` returns HTTP `404`.
- Hindsight log shows OpenAI verification fails with `billing_not_active`, so LLM-dependent retain/recall/reflect behavior is blocked until OpenAI account billing is active.
- `hindsight-embed -p tc-dn-hofi3 bank list` with process env attempts to use/install the separate Rust `hindsight` CLI and failed locally with `[WinError 2]`; bank/import behavior remains unverified.
- Curated import, `retain`, `retain-files`, and Codex auto-retain were not executed.
- Updated `docs/memory/hindsight-install-spike.md`, `docs/memory/hindsight-spike.md`, `docs/memory/contract.md`, `docs/memory/open-questions.md`, and `MemoryContractTests` so old "daemon not running" facts are no longer current.
- Verification passed: `.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter "MemoryContractTests|HindsightInstallSpikeTests"` passed `9/9`.
- `scripts/memory-refresh.ps1` regenerated the ignored memory index with `6` nodes, `5` edges, and `147` indexed files.
- `git diff --check` passed.
- Intended Hindsight daemon processes remain running for the approved project profile on port `9077`; no extra visible launcher process remained after cleanup.
- Next safe command after fixing OpenAI billing is a non-import smoke such as `hindsight-embed -p tc-dn-hofi3 bank list` with process env loaded. Do not run curated import or retain until Rust CLI/import behavior and retention/export/delete policy are confirmed.

## Git Commit Cadence Todo

- [x] Record a durable rule that Codex should commit coherent verified work slices proactively.
- [x] Prefer a project instruction over an automatic Git hook because hooks cannot safely decide semantic commit boundaries or exclude unrelated user changes.
- [x] Keep the guardrail explicit: no auto-commit for secrets, raw recordings, generated memory exports, local machine state, unrelated user changes, unclear scope, or failed/incomplete verification.
- [x] Re-run the narrow memory/Hindsight tests before committing the current slice.
- [x] Review Git status/diff hygiene before staging.
- [x] Commit the verified memory/Hindsight profile smoke and commit-cadence rule.

## Git Commit Cadence Results

- Added `AGENTS.md` Git commit cadence rules for proactive commits after coherent verified slices.
- Added the same feedback-driven rule to `tasks/lessons.md`.
- Did not add a Git auto-commit hook: that would be unsafe for secrets, generated outputs, and mixed worktrees.

## SQLite FTS5 Canonical Memory Todo

- [x] Create an isolated implementation branch for the SQLite memory slice.
- [x] Add red tests for the SQLite canonical memory contract, schema, retrieval, explain logging, stale checks, and safety exclusions.
- [x] Add a tooling-only memory CLI under `tools/` with `refresh`, `search`, `explain`, and `stale-check` commands.
- [x] Build the SQLite schema with FTS5 tables and a local `query_log`; do not use PostgreSQL-only diagnostics.
- [x] Update memory docs and ADRs so Hindsight is historical/failed, SQLite is canonical local memory, and LanceDB was deferred for that SQLite MVP slice.
- [x] Ensure raw JSONL, generated exports, secrets, local proxy details, and build artifacts are not indexed.
- [x] Run memory refresh, narrow memory/tooling tests, solution build, and stale-check verification.
- [x] Run diff hygiene checks.
- [x] Commit the verified slice.

## SQLite FTS5 Canonical Memory Results

- Started on branch `codex/sqlite-memory-store` from clean `main` with local commits ahead of `origin/main`.
- Added red/green CLI tests in `tools/Memory.Tests/MemoryCliTests.cs`; the initial red run failed because `tools/Memory/CryptoIndicatorApp.Memory.csproj` did not exist.
- Added `tools/Memory` console CLI with `refresh`, `search`, `explain`, and `stale-check`.
- Added SQLite FTS5 schema with `files`, `symbols`, `chunks`, `rules`, `adr`, `formula_versions`, `metrics`, `experiments`, `events`, `relations`, `sources`, `todos`, `search_documents`, `search_documents_fts`, and `query_log`.
- `tools/Memory.Tests` passed after implementation: `.\.dotnet\dotnet.exe test tools\Memory.Tests\CryptoIndicatorApp.Memory.Tests.csproj --no-restore --filter MemoryCliTests` passed `4/4`.
- Updated docs: `docs/decisions/0003-sqlite-fts5-canonical-memory.md`, `docs/memory/lancedb-spike.md`, `contract.md`, `README.md`, `open-questions.md`, `hindsight-spike.md`, and `gbrain-spike.md`.
- Added `Owner:` metadata to `docs/formulas.md` so the current `formula_version` is not stale.
- Added `tasks/lessons.md` rule: SQLite memory diagnostics use `EXPLAIN QUERY PLAN` plus local `query_log`, not PostgreSQL-only diagnostics.
- Added ranking coverage so typed records such as `formula_version` and ADRs rank above noisy generic chunks for current factual retrieval.
- Split SQLite schema statements into `MemorySchema.cs`; `MemoryStore.cs` is back under the project source-file size guardrail.
- Added `tools/Memory` and `tools/Memory.Tests` to `CryptoIndicatorApp.sln`; `.\.dotnet\dotnet.exe restore CryptoIndicatorApp.sln` passed.
- Verification passed: `MemoryContractTests` `8/8`, `MemoryCliTests` `4/4`, full `.\.dotnet\dotnet.exe test CryptoIndicatorApp.sln --no-restore`, and `.\.dotnet\dotnet.exe build CryptoIndicatorApp.sln --no-restore` with 0 warnings and 0 errors.
- Memory verification passed: `scripts/memory-refresh.ps1` indexed 159 files, SQLite `refresh`, `search`, `explain`, and `stale-check`; real `actual OFI formula` search now returns current `formula_version.tc-dn-hofi3.current` first.
- Diff hygiene passed: `git diff --check` returned exit code 0; generated memory, `.hindsight/`, and raw recordings remained ignored.

## LanceDB Semantic Sidecar Spike Todo

- [x] Create a separate branch for the LanceDB semantic sidecar spike.
- [x] Add red tests for local-only LanceDB sidecar guardrails, SQLite-only ingestion, generated-path safety, and no commit-hook auto-refresh.
- [x] Add tooling-only wrapper/script for `probe`, `rebuild`, `search`, `explain`, and `cleanup` against SQLite-exported current/proposed records.
- [x] Keep LanceDB below SQLite: no canonical status ownership, no direct project crawl, no raw JSONL/generated/secrets/local proxy/build artifact import.
- [x] Verify clean rebuild/delete/reindex behavior using local embedded LanceDB through `uv`, without Cloud and without auto-update after commit.
- [x] Update memory docs and lessons with actual LanceDB API/smoke results and remaining semantic-quality limits.
- [x] Run narrow tests, memory refresh/stale-check, real sidecar smoke, diff hygiene, and commit the verified slice.

## LanceDB Semantic Sidecar Spike Results

- Created branch `codex/lancedb-semantic-sidecar` from the clean SQLite memory branch.
- Added `LanceDbSidecarSpikeTests`; RED verification failed for the expected reasons: missing `scripts/lancedb-sidecar.ps1` and deferred LanceDB docs.
- Added `scripts/lancedb-sidecar.ps1` and `tools/MemorySemantic/lancedb_sidecar.py`; the sidecar reads only SQLite `search_documents` current/proposed records with valid `source_path/source_hash`.
- Added local deterministic token-hash vectors plus a typed/exact-token reranker after the first smoke showed raw vector distance ranked generic chunks above the current formula record.
- Real local LanceDB smoke through `uv` succeeded: `cleanup` removed generated store, `rebuild` recreated `docs/memory/generated/lancedb` with `271` records, and `search`/`explain` returned `formula_version.tc-dn-hofi3.current` first for `actual OFI formula`.
- LanceDB `explain` returned `explain_plan`/`analyze_plan` with `KNNVectorDistance`, `LanceRead`, and `TopK`.
- SQLite refresh now reports `semantic_sidecar: lancedb-active-local-spike`; `stale-check` reports no issues after final refresh.
- Remaining limitation: current embeddings are a no-Cloud mechanics smoke, not final semantic recall quality.
- Compact handoff: full `.\.dotnet\dotnet.exe test CryptoIndicatorApp.sln --no-restore` passed after the LanceDB changes (`90/90` across projects). A parallel `build` run failed from transient file locks in `obj/` while tests were still using outputs, so rerun build alone next.
- Post-compact verification passed: `.\.dotnet\dotnet.exe build CryptoIndicatorApp.sln --no-restore` completed with `0` warnings and `0` errors.
- Fresh narrow tests passed: Infrastructure memory/Hindsight/LanceDB filters `13/13`, Memory CLI tests `4/4`, and `tools/MemorySemantic/lancedb_sidecar_tests.py` returned `ok`.
- Fresh memory refresh/stale-check passed: legacy generated index has `162` indexed files, SQLite refresh reports `semantic_sidecar: lancedb-active-local-spike`, and SQLite `stale-check` reports no issues.
- Fresh LanceDB smoke passed after cleanup/rebuild: local generated table has `271` records, `search` returns current `formula_version.tc-dn-hofi3.current` first for `actual OFI formula`, and `explain` includes `KNNVectorDistance`, `LanceRead`, and `TopK`.

## LanceDB Semantic Quality Candidate Todo

- [x] Add RED tests for a local FastEmbed/ONNX provider contract, provider metadata, and deterministic fallback isolation.
- [x] Add RED tests for a LanceDB `eval` command that gates the required retrieval questions.
- [x] Replace production sidecar embeddings with local FastEmbed/ONNX while keeping token-hash only as an explicit test/fallback mode.
- [x] Store embedding provider/model/dimensions metadata in generated reports and indexed rows.
- [x] Add `eval` to `tools/MemorySemantic/lancedb_sidecar.py` and `scripts/lancedb-sidecar.ps1`.
- [x] Keep LanceDB below SQLite: import only current/proposed SQLite `search_documents` with valid `source_path/source_hash`.
- [x] Update `docs/memory/*`, ADR, and lessons so the sidecar is a production-candidate semantic quality layer, not a final source of truth.
- [x] Verify clean cleanup/rebuild/search/explain/eval behavior with local Python embedded dependencies, no Cloud, and no hooks.
- [x] Run narrow Python tests, Infrastructure memory tests, Memory CLI tests, memory refresh/stale-check, build, diff hygiene, and commit.

## LanceDB Semantic Quality Candidate Results

- Added `docs/decisions/0004-funding-source-context.md` so the funding-source eval case has a real ADR source instead of relying on noisy chunks.
- `scripts/lancedb-sidecar.ps1` now supports `eval`, `-EmbeddingProvider`, and `-EmbeddingModel`; default provider is local FastEmbed/ONNX through pinned `fastembed==0.8.0`.
- `tools/MemorySemantic/lancedb_sidecar.py` now stores provider/model/dimensions/package metadata in generated rows and reports.
- Token-hash remains available only as explicit `--embedding-provider token-hash` fallback/test mode.
- `tools/Memory/ProjectMemoryIndexer.cs` now emits relation records for real `CryptoIndicatorApp.Infrastructure/Binance/*` adapter files so exchange-adapter impact retrieval is source-backed.
- Real FastEmbed rebuild indexed the current/proposed SQLite record set with `384`-dimensional vectors; exact per-run counts stay in ignored generated reports because docs/task edits can legitimately shift chunk counts.
- Real LanceDB `eval` passed `4/4`: current OFI formula, funding-source ADR, exchange adapter impact, and superseded-rule exclusion.
- Added a rerank regression test so a self-referential `docs/memory/lancedb-spike.md` quality-gate chunk cannot outrank the canonical `formula_version` for the OFI formula query.
- FastEmbed `0.8.0` warns that `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` now uses mean pooling instead of older CLS behavior; the wrapper pins this behavior, and changing it later requires a fresh eval baseline.
- Compact handoff: stop here before final verification/commit. Freshly verified after the latest rerank change: Python sidecar tests returned `ok`; LanceDB `search "actual OFI formula"` returned `formula_version.tc-dn-hofi3.current` first; LanceDB `eval` passed `4/4`; SQLite `stale-check` returned no issues before the final rerank-only change. Next commands: run `scripts/lancedb-sidecar.ps1 -Command explain -Query "actual OFI formula"`, rerun SQLite `stale-check`, run Infrastructure memory tests, Memory CLI tests, solution build, `git diff --check`, review `git status`, then commit if all pass.
- Post-compact `stale-check` first failed only on `tasks/todo.md` `source_hash_mismatch`, because this handoff text changed after the previous memory refresh; refresh/rebuild must run after final plan edits.
- Post-compact verification passed on the final slice: Python sidecar tests returned `ok`; Infrastructure memory/LanceDB tests passed `11/11`; Memory CLI tests passed `4/4`; solution build completed with `0` warnings and `0` errors; LanceDB `eval` passed `4/4`; SQLite `stale-check` returned no issues after the canonical CLI `refresh`.
- The final `explain` run for `actual OFI formula` returned `formula_version.tc-dn-hofi3.current` first and included `KNNVectorDistance`, `LanceRead`, and `TopK` in the LanceDB plan.

## Memory Refresh-All Wrapper Todo

- [x] Add RED tests for a tooling-only `scripts/memory-refresh-all.ps1` wrapper contract.
- [x] Isolate and fix the current `Invoke-RefreshStep` child-process runner failure with a one-step legacy refresh repro before running the full wrapper.
- [x] Ensure the wrapper runs the existing sequence in order: legacy JSON refresh, SQLite refresh, SQLite stale-check, LanceDB cleanup, LanceDB rebuild, LanceDB eval.
- [x] Keep the wrapper outside the WPF runtime and avoid hooks, Codex auto-retain, direct project crawls, Cloud, raw JSONL, generated exports, secrets, local proxy details, and build artifacts.
- [x] Write a generated JSON report with step timings, exit codes, and commands without leaking secrets.
- [x] Update memory docs and lessons so operators use `memory-refresh-all` for full rebuilds instead of mixing partial refresh commands.
- [x] Run narrow tests, real wrapper smoke, SQLite stale-check, LanceDB eval, build, and diff hygiene.
- [x] Prepare the verified memory-refresh-all wrapper slice for commit.

## Memory Refresh-All Wrapper Handoff

- Added `CryptoIndicatorApp.Infrastructure.Tests/MemoryRefreshAllTests.cs`; initial RED failed because `scripts/memory-refresh-all.ps1` and docs did not exist.
- Added `scripts/memory-refresh-all.ps1` with `-PlanOnly`, expected step order, no Cloud/hooks/auto-retain/direct crawl flags, and generated report path `docs/memory/generated/memory-refresh-all-report.json`.
- Updated `docs/memory/README.md`, `docs/memory/contract.md`, `docs/memory/lancedb-spike.md`, `scripts/README.md`, and `tasks/lessons.md` to prefer `memory-refresh-all` for full manual rebuilds.
- Narrow plan contract passes: `.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter MemoryRefreshAllTests` passed `2/2`.
- Real full wrapper smoke is not passing yet. First attempt deadlocked on child stdout because the wrapper waited before reading redirected output; temp-file workaround avoided the deadlock but lost `ExitCode` with Windows PowerShell `Start-Process -RedirectStandardOutput`.
- Current implementation uses `System.Diagnostics.Process` plus async output handlers, but `powershell.exe -File scripts\memory-refresh-all.ps1` exits `1` before writing a full report. A minimal inline reproduction with the same async handler pattern exited `2` with no output, so continue systematic debugging before running the heavy full refresh again.
- No active `memory-refresh-all` child process remains at handoff.
- Next exact step after `/compact`: isolate `Invoke-RefreshStep` in a small temporary script file or add temporary diagnostics inside `scripts/memory-refresh-all.ps1` around `Process.Start`, `BeginOutputReadLine`, and `WaitForExit`; verify a single `legacy-json-refresh` step returns exit code `0`, then rerun the full wrapper.

## Memory Refresh-All Wrapper Debug Plan

- [x] Verify `scripts/memory-refresh.ps1` still succeeds when run directly.
- [x] Reproduce the failure with a one-step script-level runner repro.
- [x] Identify whether the failure occurs at `Process.Start`, output-read registration, wait/exit-code capture, or strict-mode variable scoping.
- [x] Patch `scripts/memory-refresh-all.ps1` only after the failing boundary is known.
- [x] Re-run one-step legacy refresh through the wrapper runner, then the full wrapper.

## Memory Refresh-All Wrapper Debug Results

- Direct `scripts/memory-refresh.ps1` succeeded, so the legacy refresh script was not the failing component.
- A temporary one-step script-level repro showed `Process.Start`, `BeginOutputReadLine`, `BeginErrorReadLine`, and timed `WaitForExit` completed, then the parent PowerShell process exited before `ExitCode` was read.
- The same repro using `StandardOutput.ReadToEndAsync()` and `StandardError.ReadToEndAsync()` returned exit code `0`, so the root cause was the PowerShell 5.1 `DataReceivedEventHandler` output path, not the child command.
- `scripts/memory-refresh-all.ps1` now uses task-based async reads and the full local sequence completed successfully.

## Memory Refresh-All Wrapper Verification Results

- `scripts/memory-refresh-all.ps1` completed the full local sequence: legacy JSON refresh, SQLite refresh, SQLite stale-check, LanceDB cleanup, LanceDB rebuild, and LanceDB eval.
- `.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter MemoryRefreshAllTests` passed `2/2`.
- `.\.dotnet\dotnet.exe test CryptoIndicatorApp.Infrastructure.Tests\CryptoIndicatorApp.Infrastructure.Tests.csproj --no-restore --filter "MemoryRefreshAllTests|LanceDbSidecarSpikeTests|MemoryContractTests|HindsightInstallSpikeTests|HindsightCuratedImportTests"` passed `17/17`.
- `.\.dotnet\dotnet.exe test tools\Memory.Tests\CryptoIndicatorApp.Memory.Tests.csproj --no-restore --filter MemoryCliTests` passed `4/4`.
- `uv run --python 3.12 --with lancedb --with pyarrow --with fastembed==0.8.0 python tools\MemorySemantic\lancedb_sidecar_tests.py` returned `ok`.
- `.\.dotnet\dotnet.exe build CryptoIndicatorApp.sln --no-restore` completed with `0` warnings and `0` errors.
- `git diff --check` passed.

## LanceDB Semantic Quality Gate Expansion Todo

- [x] Add typed, human-authored memory rules for project guardrails that should not rely only on generic chunks.
- [x] Expand LanceDB `eval` cases from smoke coverage to formula owner, Binance DTO boundary, REST hot-path ban, live/replay shared pipeline, funding slow context, exchange adapter impact, and stale/superseded exclusion.
- [x] Add narrow Python tests for the expanded eval contract and fallback embedding behavior.
- [x] Update memory docs and lessons with the stronger gate and its limits.
- [x] Run Python sidecar tests, memory refresh/stale-check, LanceDB cleanup/rebuild/eval, relevant .NET tests/build, diff hygiene, and commit if the slice is clean.

## LanceDB Semantic Quality Gate Expansion Results

- Added `docs/memory/rules.md` with typed current rules for REST hot-path ban, Binance DTO boundary, live/replay shared pipeline, funding slow context, and a superseded legacy-rule fixture.
- Expanded `tools/MemorySemantic/lancedb_sidecar.py` eval from `4` to `9` cases: OFI formula, formula owner, funding-source ADR, Binance DTO boundary, REST hot-path ban, live/replay shared pipeline, funding slow context, exchange adapter impact, and superseded/failed exclusion.
- Fixed the token-hash fallback embedding dimension constant and added a Python test that actually embeds text through the fallback provider.
- Updated docs and tests so the stronger gate is source-backed by `formula_version`, ADR, typed rules, and relation records instead of only generic chunks.
- Verification passed: Python sidecar tests returned `ok`; `LanceDbSidecarSpikeTests` passed `3/3`; `MemoryCliTests` passed `4/4`; `memory-refresh-all` completed with SQLite stale-check clean and LanceDB eval `9/9`; solution build completed with `0` warnings and `0` errors.

## LanceDB Eval Report Todo

- [x] Add RED Python tests for a compact eval quality report with query, expected ids/types, matched rank, source path, confidence, and gap notes.
- [x] Add generated Markdown output for the same eval report under `docs/memory/generated/` without making it a source of truth.
- [x] Update `scripts/lancedb-sidecar.ps1` and memory docs so operators can find the JSON/Markdown reports before any hook/automation decision.
- [x] Run Python sidecar tests, relevant Infrastructure tests, memory refresh-all, solution build, diff hygiene, and commit the verified slice if clean.

## LanceDB Eval Report Results

- Added compact eval case reporting with expected ids/types, forbidden statuses, matched rank, matched source path, matched confidence, ranked top results, and gap notes.
- Added generated Markdown output at `docs/memory/generated/lancedb-eval-report.md`; the JSON eval report remains `docs/memory/generated/lancedb-sidecar-report.json`.
- Updated `scripts/lancedb-sidecar.ps1` probe/output metadata and docs so the eval reports are explicit review evidence before any hook/automation decision.
- Extracted eval case/report logic into `tools/MemorySemantic/lancedb_eval_report.py` so `lancedb_sidecar.py` remains focused on LanceDB orchestration and embedding I/O.
- Verification passed: Python sidecar tests returned `ok`; `LanceDbSidecarSpikeTests|MemoryRefreshAllTests` passed `5/5`; `MemoryCliTests` passed `4/4`; `memory-refresh-all` completed with SQLite stale-check clean and LanceDB eval `9/9`; solution build completed with `0` warnings and `0` errors.

## Manual Memory Gate Todo

- [x] Add an ADR for the manual memory gate strategy: manual `memory-refresh-all` first, optional manual pre-push helper next, no post-commit automation.
- [x] Add RED tests for `scripts/memory-pre-push-check.ps1 -PlanOnly`.
- [x] Verify the helper plan does not install hooks, call Cloud, enable Codex auto-retain, touch raw JSONL, `.hindsight/`, secrets, generated exports as sources, build artifacts, or post-commit automation.
- [x] Implement the minimal helper as a manual command that validates refresh/eval evidence without becoming a hook.
- [x] Update memory docs and `tasks/lessons.md`.
- [x] Run real `memory-refresh-all`, then the helper.
- [x] Run narrow tests and build.
- [x] Run diff hygiene, review Git status, and prepare the verified commit boundary.

## Manual Memory Gate Results

- Added ADR `docs/decisions/0005-manual-memory-gate.md`: manual `memory-refresh-all`, manual `memory-pre-push-check`, no post-commit refresh automation, no automatic hook installation.
- Added `scripts/memory-pre-push-check.ps1` with `-PlanOnly`; the full mode validates existing refresh/eval evidence instead of rebuilding memory or installing hooks.
- Added `ManualMemoryGateTests` covering plan-only guardrails, docs, ADR, no Cloud, no Codex auto-retain, no hooks, no raw JSONL, no `.hindsight/`, no sensitive storage, no generated exports as source, and no build artifacts.
- Updated `docs/memory/contract.md`, `docs/memory/README.md`, `scripts/README.md`, and `tasks/lessons.md`.
- Real `scripts/memory-refresh-all.ps1` completed: legacy JSON refresh, SQLite refresh, SQLite stale-check with `issues: []`, LanceDB cleanup/rebuild, and LanceDB eval `9/9`.
- Real `scripts/memory-pre-push-check.ps1` passed with `passed_count=9`, `failed_count=0`, `runs_refresh_all=false`, `installs_hooks=false`, and `post_commit_auto_refresh_enabled=false`.
- Verification passed so far: `ManualMemoryGateTests|MemoryRefreshAllTests|LanceDbSidecarSpikeTests` `7/7`, `MemoryCliTests` `4/4`, `tools/MemorySemantic/lancedb_sidecar_tests.py` `ok`, and `dotnet build CryptoIndicatorApp.sln --no-restore` completed with `0` warnings and `0` errors.
- Diff hygiene passed with `git diff --check`; Git status contains only this manual memory gate slice.

## Optional Memory Pre-Push Hook Todo

- [x] Add RED tests for `scripts/install-memory-pre-push-hook.ps1` plan/install/disable behavior using a temp hook path, not `.git/hooks`.
- [x] Require explicit `-Confirm` for any write; default and `-PlanOnly` must not install hooks or run rebuilds.
- [x] Implement a managed pre-push hook that calls `scripts/memory-pre-push-check.ps1` only and refuses to overwrite unmanaged hooks.
- [x] Add a disable path for the managed hook and document how to remove it.
- [x] Add ADR/docs/lessons updates: optional local pre-push helper, no post-commit automation, no rebuild inside hook by default.
- [x] Run real `memory-refresh-all`, then `memory-pre-push-check`; do not install the actual repository hook during verification.
- [x] Run narrow tests, build, diff hygiene, review status, and commit the verified slice.

## Optional Memory Pre-Push Hook Results

- Added `scripts/install-memory-pre-push-hook.ps1` with explicit `-Confirm` for hook writes and `-Disable -Confirm` for removing only the managed hook.
- The generated hook calls `scripts/memory-pre-push-check.ps1` only; it does not run `memory-refresh-all`, rebuild memory, add post-commit automation, call Cloud, or enable Codex auto-retain.
- The generated shell hook is LF-only, so Git Bash does not see a CRLF shebang.
- The installer refuses to overwrite an unmanaged existing `pre-push` hook.
- Added ADR `docs/decisions/0006-optional-memory-pre-push-hook.md` and updated ADR 0005, memory contract, memory README, scripts README, and lessons.
- RED verification failed first on missing installer/ADR as expected; GREEN `ManualMemoryGateTests` passed `6/6`.
- Real `memory-refresh-all` completed with SQLite stale-check `issues: []` and LanceDB eval `9/9`.
- Real `memory-pre-push-check` passed with `passed_count=9`, `failed_count=0`, `runs_refresh_all=false`, `installs_hooks=false`, and `post_commit_auto_refresh_enabled=false`.
- Real installer `-PlanOnly` wrote an ignored report with `installs_hooks=false`; `.git/hooks/pre-push` remained absent.
- Verification passed: Infrastructure memory tests `19/19`, Memory CLI tests `4/4`, Python sidecar tests `ok`, solution build completed with `0` warnings and `0` errors, and `git diff --check` passed.
- Git status before final refresh contained only the optional-hook slice: `ManualMemoryGateTests`, ADR/docs/lessons/todo, and the new installer script.

## Memory Reminder Trigger Rules Todo

- [x] Add short Codex reminder triggers to `AGENTS.md` for push/PR, ADR/formula decisions, `/compact`, and failed experiments/regressions.
- [x] Add a matching "Когда Codex должен напоминать" section to the external harness memory-management note.
- [x] Verify the rule text forbids Codex auto-retain hooks and post-commit hooks for now.
- [x] Run memory refresh/check evidence, diff hygiene, and commit the repo instruction slice if clean.

## Memory Reminder Trigger Rules Results

- Added `AGENTS.md` reminder triggers for explicit `commit`, `push`, PR, `/compact`, ADR, formula, experiment, and regression moments.
- The triggers only remind which command to run; they do not add Codex auto-retain hooks, post-commit hooks, after-save hooks, or background memory refresh.
- Updated `C:\Users\MECHREVO\Desktop\harness management\memory management.md` with a matching "Когда Codex должен напоминать" section.
- `scripts/memory-refresh-all.ps1` completed with SQLite stale-check `issues: []` and LanceDB eval passing.
- `scripts/memory-pre-push-check.ps1` passed with LanceDB eval `9/9`, `failed_count=0`, Cloud/Codex auto-retain/post-commit refresh disabled.
- `git diff --check` passed.

## Commit-Addressed Memory Refresh Todo

- [x] Add ADR for commit-addressed memory refresh: Git tree/commit is the refresh source, not the live working directory.
- [x] Add RED CLI tests for `memory refresh-from-commit --commit HEAD`, commit metadata, and `memory status`.
- [x] Add SQLite metadata fields: `commit_sha`, `tree_sha`, `source_blob_sha`, and `indexed_at`.
- [x] Implement `refresh-from-commit` so it indexes files from the requested Git commit tree and ignores uncommitted working-tree changes.
- [x] Implement `memory status` showing `head`, `indexed_commit`, and `needs_refresh`.
- [x] Add marker-only post-commit hook installer with `-Confirm`, `-Disable`, timeout/lock/report guardrails.
- [x] Update docs/contracts/scripts/lessons with curated retain as a deferred stage behind redaction/delete/export policy.
- [x] Run narrow tests, memory refresh/check evidence, build, diff hygiene, and commit the verified slice.

## Commit-Addressed Memory Refresh Compact Handoff

- Implemented and verified the core `tools/Memory` path: `refresh-from-commit --commit HEAD`, `status`, `commit_sha`, `tree_sha`, `source_blob_sha`, `indexed_at`, marker clearing, and working-tree-dirty reporting.
- Added `GitCommitMemoryIndexer.cs` and `MemoryRefreshMarker.cs`; commit refresh reads Git blob content from the requested commit tree and does not use the live working directory as the source.
- Added `scripts/memory-mark-needs-refresh.ps1` and `scripts/install-memory-post-commit-marker-hook.ps1`, plus hook guardrail tests in `ManualMemoryGateTests`.
- Added ADR `docs/decisions/0007-commit-addressed-memory-refresh.md` and updated `AGENTS.md`, memory contract/README, scripts README, lessons, and LanceDB sidecar metadata handling.
- `scripts/memory-refresh-all.ps1` now runs SQLite `refresh-from-commit --commit HEAD`; LanceDB source validation now checks Git blob metadata for commit-indexed rows instead of the dirty working tree.
- Verification passed: `ManualMemoryGateTests|MemoryRefreshAllTests` passed `10/10`; expanded Infrastructure memory tests passed `25/25`; `MemoryCliTests` passed `6/6`; Python LanceDB sidecar tests returned `ok`; solution build completed with `0` warnings and `0` errors; `git diff --check` passed.
- Post-commit memory evidence passed: `memory-refresh-all` completed with SQLite stale-check `issues: []`, LanceDB rebuild indexed commit metadata, LanceDB eval passed `9/9`, `memory status` reported `needs_refresh=false`, and `memory-pre-push-check` passed.

## Minimal GitHub Actions CI Todo

- [x] Add a Windows GitHub Actions workflow for minimal .NET CI.
- [x] Run `dotnet restore` once, then `dotnet build CryptoIndicatorApp.sln --no-restore`.
- [x] Run relevant deterministic .NET tests, including `tools/Memory.Tests`, without LanceDB/FastEmbed.
- [x] Verify locally with the project SDK and diff hygiene.
- [x] Commit, refresh memory from committed `HEAD`, run the manual pre-push gate, and push.
- [x] Check GitHub PR checks; if green, mark the PR ready for review and then merge only after review/merge conditions are clear.
- [x] Fix first GitHub Actions failure caused by PowerShell script hash portability.
- [x] Update CI actions to Node.js 24-compatible major versions.

## Minimal GitHub Actions CI Results

- Added `.github/workflows/ci.yml` with a Windows runner because the solution includes WPF/`net8.0-windows`.
- The workflow runs `dotnet restore CryptoIndicatorApp.sln` once, then `dotnet build CryptoIndicatorApp.sln --configuration Release --no-restore`.
- The workflow runs solution tests and a separate explicit `tools/Memory.Tests` step; it does not run Python, LanceDB, FastEmbed, Cloud, hooks, or memory rebuilds.
- Local verification passed: restore completed, Release build completed with `0` warnings and `0` errors, solution tests passed `102/102`, Memory CLI tests passed `6/6`, and `git diff --check` passed.
- First GitHub Actions run failed in Infrastructure tests because `scripts/memory-refresh.ps1` and `scripts/hindsight-curated-import.ps1` depended on `Get-FileHash`, which was not available in the runner's `powershell.exe` process.
- The same run also emitted a GitHub Actions warning that `actions/checkout@v4` and `actions/setup-dotnet@v4` target deprecated Node.js 20.
- Replaced script hashing with portable .NET `SHA256`, removed duplicate `codex/**` push CI trigger, and updated CI actions to `actions/checkout@v7` and `actions/setup-dotnet@v5`.
- Local re-verification after the CI fix passed: failed Infrastructure subset `10/10`, restore, Release build with `0` warnings/errors, solution tests `102/102`, and Memory CLI tests `6/6`.

## Curated Retain Policy Todo

- [x] Create branch `codex/curated-retain-policy` from updated `main`.
- [x] Add RED tests for curated retain allowlist, denylist, lifecycle policy, and post-commit marker-only guardrails.
- [x] Add ADR `docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md`.
- [x] Add `docs/memory/retain-policy.md` with redaction, delete, export, allowlist, denylist, and enablement gates.
- [x] Update `docs/memory/contract.md`, `docs/memory/README.md`, and `docs/memory/open-questions.md`.
- [x] Update curated import manifest tooling only as needed; do not enable external retain or Codex auto-retain.
- [x] Run narrow tests, memory refresh/check evidence, build/diff hygiene, commit, push, and open a PR.

## Curated Retain Policy Results

- Added RED tests in `CuratedRetainPolicyTests` and expanded `HindsightCuratedImportTests`; initial failure was expected because ADR `0008`, `retain-policy.md`, and `TC-DN-HOFI3.md` allowlist support were missing.
- Added ADR `docs/decisions/0008-curated-retain-and-memory-lifecycle-policy.md` and `docs/memory/retain-policy.md`.
- Updated memory contract, README, open questions, and curated manifest generation to include `TC-DN-HOFI3.md` and the stricter denylist.
- Real retain/import, external memory writes, Codex auto-retain, Cloud, and post-commit rebuild remain disabled.
- Narrow GREEN verification passed: `CuratedRetainPolicyTests|HindsightCuratedImportTests` `4/4`.
- Expanded memory guardrail verification passed: `CuratedRetainPolicyTests|HindsightCuratedImportTests|MemoryContractTests|ManualMemoryGateTests` `20/20`.
- Full solution tests passed: `dotnet test CryptoIndicatorApp.sln --no-restore` returned `104/104`.
- Build passed: `dotnet build CryptoIndicatorApp.sln --no-restore` completed with `0` warnings and `0` errors.
- `git diff --check` passed before commit.
- Post-commit `memory-refresh-all` completed with SQLite stale-check `issues: []`, LanceDB indexed count `344`, and eval `9/9`.
- `memory status` reported `NeedsRefresh=False`, `WorkingTreeDirty=False`, and `MarkerExists=False`.
- `memory-pre-push-check` passed with `passed_count=9`, `failed_count=0`, no Cloud, no Codex auto-retain, no post-commit refresh, and no hook installation.
- Branch `codex/curated-retain-policy` was pushed and draft PR #2 was opened.

## Curated Retain Dry-Run Todo

- [x] Create branch `codex/curated-retain-dry-run` from clean `main`.
- [x] Close stale Minimal GitHub Actions CI todo items after PR #2 merge.
- [x] Add RED tests for a provider-neutral curated retain dry-run report.
- [x] Implement `scripts/curated-retain-dry-run.ps1` with allowlist-only source enumeration and redaction-risk scanning.
- [x] Write the ignored report under `docs/memory/generated/`.
- [x] Verify the dry-run does not call Cloud, Hindsight, Codex retain, hooks, rebuild, or import denylisted sources.
- [x] Run narrow tests, build, and diff hygiene before commit.
- [x] After commit, run memory refresh/status/pre-push, then push/PR if clean.

## Curated Retain Dry-Run Results

- Added `scripts/curated-retain-dry-run.ps1` as a provider-neutral retain preflight. It writes only `docs/memory/generated/curated-retain-dry-run-report.json`, does not call Hindsight, Cloud, Codex retain, hooks, `memory-refresh-all`, or rebuild commands, and keeps external retain disabled.
- Added `CuratedRetainDryRunTests` with a RED/GREEN path for allowlisted source enumeration, denylist exclusion, redaction-risk findings, and external automation guardrails.
- Real dry-run on the repository produced an ignored report with `24` allowlisted files and `134` review findings across `16` files. Findings are review evidence only and do not import or retain data.
- Updated memory and scripts docs with the dry-run command and scope.
- Verification before commit: `CuratedRetainDryRunTests` passed `2/2`; expanded retain/memory guardrail tests passed `22/22`; full solution tests passed `106/106`; solution build passed with `0` warnings and `0` errors; `git diff --check` passed.
- Post-commit memory gate completed before push: `memory-refresh-all` completed with SQLite stale-check `issues: []`, LanceDB indexed the committed metadata, LanceDB eval passed `9/9`, `memory status` reported `needs_refresh=false`, and `memory-pre-push-check` passed.

## Memory Polish Roadmap Todo

- [x] Start branch `codex/memory-polish-roadmap` from clean `main`.
- [x] Add a five-slice implementation plan for a polished memory system.
- [x] Slice 1: improve curated retain report quality with severity, type counts, de-duplication, policy-reference classification, and Markdown output.
- [x] Slice 2: add provider-neutral retain export/delete dry-run policy tooling before any external/Codex retain can be enabled.
  - [x] Add RED tests for export/delete lifecycle gate, missing/stale reports, and denylist exclusion.
  - [x] Implement report-only export/delete dry-run scripts under `docs/memory/generated/`.
  - [x] Update retain lifecycle docs and script operator docs.
  - [x] Run narrow tests, real dry-runs, full build, and diff hygiene.
- [x] Slice 3: validate optional post-commit marker local install behavior without enabling rebuild or retain hooks.
- [x] Slice 4: make the FastEmbed/LanceDB warning an explicit semantic baseline decision backed by eval.
- [ ] Slice 5: add a simple operator UX helper for routine memory checks.
- [ ] For each slice: use RED/GREEN tests, run narrow verification, build when shared scripts/docs are touched, commit the verified slice, refresh memory from committed `HEAD`, and run the manual pre-push gate before PR/push.

## Memory Polish Roadmap Results

- Added detailed plan `docs/superpowers/plans/2026-07-01-memory-polish-roadmap.md` covering five slices: report quality, export/delete policy tooling, post-commit marker validation, FastEmbed/LanceDB baseline, and operator UX.
- Slice 1 improved `scripts/curated-retain-dry-run.ps1`: JSON report now includes severity counts, type counts, de-duplicated findings, and `policy_reference`; the script also writes ignored Markdown report `docs/memory/generated/curated-retain-dry-run-report.md`.
- Added RED/GREEN coverage in `CuratedRetainDryRunTests` for Markdown output, severity summary, policy-only references, duplicate finding keys, blank-line compatibility, and `token-hash` not being treated as a secret token.
- Real dry-run on the repository now reports `24` allowlisted files, `133` findings, `0` critical, `78` review, `55` info, and `16` files requiring redaction review.
- Verification for Slice 1 passed: `CuratedRetainDryRunTests` `3/3`, real `curated-retain-dry-run.ps1`, solution build with `0` warnings/errors, and `git diff --check`.
- Slice 2 added provider-neutral lifecycle dry-runs: `scripts/curated-retain-export-dry-run.ps1` validates curated report source metadata/hash freshness without exporting source text, and `scripts/curated-retain-delete-dry-run.ps1` writes a deletion plan without deleting files, provider data, hooks, generated reports, local stores, or retained items.
- Added RED/GREEN coverage in `CuratedRetainPolicyTests` for missing/stale lifecycle reports, denylist rejection even if an input report is compromised, metadata-only export, and delete-plan-only behavior.
- Real Slice 2 dry-runs on the repository produced blocked reports as expected: 24 allowlisted sources, 0 invalid/denied sources, 0 source hash mismatches, external retain disabled, Codex auto-retain disabled, delete actions executed `false`, and 16 sources still requiring redaction review.
- Verification for Slice 2 passed: `CuratedRetainPolicyTests|CuratedRetainDryRunTests` `9/9`, real curated retain dry-run/export dry-run/delete dry-run, solution build with `0` warnings/errors, and `git diff --check`.

## Memory Polish Slice 3 Todo

- [x] Create branch `codex/memory-post-commit-marker-validation` from clean `main`.
- [x] Add RED tests for post-commit marker installer validation metadata, confirm-required behavior, unmanaged-hook refusal, and marker-only helper reports.
- [x] Keep validation in temporary/custom hook paths; do not install or disable the actual repository `.git/hooks/post-commit`.
- [x] Update script reports/docs so operators can see whether a run targets the real repo hook or a safe validation hook path.
- [x] Run narrow memory hook tests, related memory guardrail tests, Memory CLI tests, build, diff hygiene, and verify the real repo `post-commit` hook was not created.

## Memory Polish Slice 3 Results

- Added report metadata to `scripts/install-memory-post-commit-marker-hook.ps1`: `default_hook_path`, `targets_default_repo_hook`, `custom_hook_path`, `writes_default_repo_hook`, `removes_default_repo_hook`, and `actual_repo_hook_touched`.
- Added RED/GREEN coverage for custom-path validation metadata, missing `-Confirm`, unmanaged hook refusal, managed install/disable, and marker-only helper output.
- Updated memory and scripts docs so validation uses temporary/custom hook paths and real hook installation remains explicit opt-in.
- Verification before commit passed: `ManualMemoryGateTests` `11/11`; `ManualMemoryGateTests|CuratedRetainPolicyTests|MemoryRefreshAllTests` `19/19`; `tools/Memory.Tests` `6/6`; solution build completed with `0` warnings and `0` errors; `git diff --check` passed.
- `Test-Path .git/hooks/post-commit` returned `False`; the real repository post-commit hook was not installed.

## Memory Polish Slice 4 Todo

- [x] Create branch `codex/memory-semantic-baseline` from clean `main`.
- [x] Add RED tests for FastEmbed package/model/pooling baseline metadata in LanceDB reports.
- [x] Add explicit `embedding_pooling_baseline` and eval-gate metadata to Python and PowerShell reports.
- [x] Update LanceDB memory docs/contract so the mean-pooling warning is an accepted baseline only while eval remains `9/9`.
- [x] Run Python sidecar tests, LanceDB rebuild/eval, relevant .NET tests, solution build, and diff hygiene.

## Memory Polish Slice 4 Results

- Accepted the current FastEmbed `0.8.0` mean-pooling behavior as the documented LanceDB semantic baseline only while eval remains `9/9`; no model/package downgrade was added.
- Added `embedding_package_pin`, `embedding_pooling_baseline`, `embedding_baseline_status`, `embedding_baseline_eval_gate`, and baseline change-policy metadata to Python and PowerShell reports.
- The generated LanceDB Markdown eval report now includes an "Embedding baseline" section before retrieval cases.
- Updated `docs/memory/lancedb-spike.md`, `docs/memory/contract.md`, and `docs/memory/README.md` so the warning is not treated as hidden noise.
- Verification passed: Python sidecar tests returned `ok`; LanceDB cleanup/rebuild/eval completed with FastEmbed warning visible and eval `9/9`; `LanceDbSidecarSpikeTests|MemoryRefreshAllTests|ManualMemoryGateTests|CuratedRetainPolicyTests` passed `22/22`; `tools/Memory.Tests` passed `6/6`; solution build completed with `0` warnings and `0` errors; `git diff --check` passed.

## Memory Polish Slice 5 Todo

- [x] Create branch `codex/memory-operator-ux-helper` from clean `main`.
- [x] Add RED tests for `scripts/memory-daily-check.ps1 -PlanOnly`.
- [x] Implement a read/report-only memory operator helper.
- [x] Document the helper in memory and scripts docs.
- [x] Run helper tests, real `-PlanOnly`, memory status, build, diff hygiene, and review diff before commit.
- [x] After committing this source slice, run `memory-refresh-all`, `memory status`, and `memory-pre-push-check`.

## Memory Polish Slice 5 Results

- RED verification passed before implementation:
  `MemoryDailyCheckPlanWritesReadOnlyOperatorReport` failed on missing `scripts/memory-daily-check.ps1`;
  `MemoryDailyCheckDocsDescribeReadOnlyRoutine` failed on missing documentation.
- Added `scripts/memory-daily-check.ps1` as a read-only operator snapshot. It reports branch, `HEAD`, existing SQLite memory status, marker status, LanceDB eval status, and generated report presence. It does not run `memory-refresh-all`, rebuild memory, install hooks, import retain data, call Cloud, call Hindsight, or call Codex retain.
- Updated `docs/memory/README.md` and `scripts/README.md` with the routine command and explicit read-only limits.
- Verification passed so far:
  `MemoryDailyCheckPlanWritesReadOnlyOperatorReport` `1/1`;
  `MemoryDailyCheckDocsDescribeReadOnlyRoutine` `1/1`;
  `ManualMemoryGateTests` `13/13`;
  `ManualMemoryGateTests|MemoryRefreshAllTests|CuratedRetainPolicyTests|LanceDbSidecarSpikeTests` `24/24`;
  `tools/Memory.Tests` `6/6`;
  real `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\memory-daily-check.ps1 -ProjectRoot . -PlanOnly` completed and wrote the ignored daily report;
  `.\.dotnet\dotnet.exe build CryptoIndicatorApp.sln --no-restore` completed with `0` warnings and `0` errors;
  `git diff --check` passed and the diff was reviewed.
- `memory status` before compact handoff: `needs_refresh=false`, `marker_exists=false`, `indexed_commit=d330bfa98dbf2ac879228e7fc2cce4e4bc6dbb2d`, `working_tree_dirty=true`.
- Compact handoff was resumed; build, diff hygiene, source commit, and post-commit memory gate were completed.
- `memory-refresh-all` was intentionally run only after committing the slice because refresh indexes committed `HEAD`, not the dirty working tree.
- Post-commit memory gate passed after the final source commit: `scripts\memory-refresh-all.ps1` completed, `memory status` reported `needs_refresh=false`, and `scripts\memory-pre-push-check.ps1` reported `status=passed` with LanceDB eval `9/9`.

## Memory Futureproof Phase 2 Todo

- [x] Start branch `codex/memory-futureproof-phase2-roadmap` from clean `main`.
- [x] Create roadmap plan `docs/superpowers/plans/2026-07-01-memory-futureproof-phase2.md`.
- [x] Slice 1: controlled curated retain import into local SQLite, commit-grounded and blocked by redaction/denylist/stale metadata.
  - [x] Add RED Memory CLI tests for `retain-import`.
  - [x] Implement local-only `retain-import` and generated report wrapper.
  - [x] Update retain policy, memory contract, and script docs.
  - [x] Verify with Memory CLI tests, wrapper run, solution build, and diff hygiene.
- [x] Slice 2: end-to-end local retain lifecycle.
  - [x] Add RED tests for `retain-search`, `retain-export`, `retain-delete`, and absent-after-delete verification.
  - [x] Implement local-only lifecycle commands and wrappers.
  - [x] Verify lifecycle tests, wrapper reports, solution build, and diff hygiene.
- [x] Slice 3: expanded retrieval quality gate.
  - [x] Add eval cases for formula owner, funding-source rationale, Binance DTO boundary, REST hot path ban, live/replay shared pipeline, funding slow context, exchange adapter impact, and superseded/failed exclusion.
  - [x] Update JSON/Markdown eval reports with rank, source path, confidence, freshness, and gap notes.
  - [x] Verify Memory CLI tests, Python sidecar tests, LanceDB rebuild/eval, build, and diff hygiene.
- [x] Slice 4: recovery/rebuild proof from Git `HEAD`.
  - [x] Add tests for deleting local SQLite/LanceDB generated stores and rebuilding from committed sources.
  - [x] Add documented recovery wrapper that touches only approved generated memory artifacts.
  - [x] Verify recovery tests, real plan/report mode, build, and diff hygiene.
- [x] Slice 5: optional marker-only automation hardening.
  - [x] Keep post-commit automation explicit, disableable, marker-only, timeout/lock/report-backed, and no rebuild/retain/Cloud.
  - [x] Verify manual gate tests, helper `-PlanOnly`, build, memory refresh, status, and pre-push gate.

## Memory Futureproof Phase 2 Results

- Roadmap starts from the already merged Memory Polish Phase 1 baseline: SQLite FTS5 canonical store, LanceDB sidecar, manual memory gate, curated retain dry-runs, operator daily check, and marker-only hook policy are already present.
- Critical implementation constraint: controlled retain must read from a committed Git tree plus reviewed allowlist metadata, not from a dirty working directory.
- Slice 1 RED/GREEN completed: `retain-import` initially failed as an unknown command; after implementation, Memory CLI retain-import tests passed `2/2`.
- `retain-import` now imports only redaction-clean allowlisted files from the selected Git commit tree into local SQLite retained-memory tables; dirty working-tree text is not imported.
- `retain-search` can find locally imported retained items and excludes dirty working-tree-only content in tests.
- Denylisted paths and redaction-review sources block the whole import batch with exit code `2` at the CLI layer.
- Added `scripts/curated-retain-import.ps1` as a local-only wrapper that writes ignored report `docs/memory/generated/curated-retain-import-report.json`; real repo run is currently `blocked` with `imported_count=0` because the existing dry-run report has redaction/stale-source blockers.
- Verification for Slice 1 passed: `tools/Memory.Tests` `8/8`, `CuratedRetainPolicyTests|MemoryContractTests|ManualMemoryGateTests` `28/28`, solution build `0` warnings and `0` errors, and `git diff --check` clean.
- Slice 1 source committed as `079d4fb` and post-commit memory gate passed: `memory-refresh-all` completed, `memory status` reported `needs_refresh=false`, and `memory-pre-push-check` passed.
- Slice 2 adds local-only `retain-export`, `retain-delete`, `scripts/curated-retain-export.ps1`, and `scripts/curated-retain-delete.ps1`.
- Slice 2 lifecycle proof covers: import retained item, find through `retain-search`, export retained text and metadata, delete retained rows by `source_path`, and verify the deleted phrase is absent from `retain-search`.
- Slice 2 verification passed before commit:
  `RetainExportDeleteLifecycleProvesImportedItemCanBeRemoved` `1/1` (user-run full Memory CLI suite reported `9/9`);
  `ControlledRetainLifecycleScriptsAndDocsStayLocalOnly` `1/1`;
  related Infrastructure guardrails `29/29`;
  solution build `0` warnings and `0` errors;
  `git diff --check` clean.
- Commit Slice 2 source before running `memory-refresh-all`, because refresh indexes committed `HEAD`.
- Slice 2 source committed as `ef77b37`; post-commit memory gate passed: `memory-refresh-all` completed, `memory status` reported `needs_refresh=false`, and `memory-pre-push-check` passed.
- Slice 3 required retrieval quality gate was already implemented before this roadmap checkpoint: LanceDB eval has 9 cases covering current OFI formula, formula owner, funding-source rationale, Binance DTO boundary, REST hot-path ban, live/replay shared pipeline, funding slow context, exchange adapter impact, and superseded/failed exclusion.
- Slice 3 verification passed: Memory CLI tests `9/9`, Python sidecar tests through `uv` returned `ok`, and post-Slice-2 `memory-refresh-all` LanceDB eval reported `9/9`.
- Slice 3 source committed as `2dbacaa`; post-commit memory gate passed: `memory-refresh-all` completed, `memory status` reported `needs_refresh=false`, and `memory-pre-push-check` passed.
- Slice 4 adds `scripts/memory-rebuild-from-head.ps1` as a local recovery wrapper with `-PlanOnly`, allowlisted deletes under `docs/memory/generated/`, `memory-refresh-all` execution, and final `memory status needs_refresh=false` verification.
- Slice 4 real recovery run passed: deleted local generated SQLite/LanceDB/report artifacts only, rebuilt from committed `HEAD`, and reported `memory_status_needs_refresh=false`.
- Slice 4 source committed as `68ea272`; post-commit memory gate passed: `memory-refresh-all` completed, `memory status` reported `needs_refresh=false`, and `memory-pre-push-check` passed.
- Slice 5 hardened optional marker-only automation without enabling it by default: post-commit marker installer and marker helper now reject non-positive `-TimeoutSeconds`; marker helper reports include `lock_path`.
- Slice 5 verification before commit passed: RED timeout tests failed for the expected reason, GREEN timeout tests passed `2/2`, `ManualMemoryGateTests` passed `15/15`, custom hook-path `install-memory-post-commit-marker-hook.ps1 -PlanOnly` reported `actual_repo_hook_touched=false`, and solution build completed with `0` warnings and `0` errors.
- Slice 5 source committed as `94996ea`; post-commit memory gate passed before the full solution test run: `memory-refresh-all` completed, `memory status` reported `needs_refresh=false`, and `memory-pre-push-check` passed.

## Compact Handoff - Pre-Push Gate Robustness

- [x] Fix `scripts/memory-pre-push-check.ps1` so a non-eval/probe-style LanceDB sidecar JSON report is reported as a failed `lancedb-eval-passed` check instead of throwing a PowerShell missing-property exception.
- [x] Run the new regression test `PrePushCheckRejectsNonEvalLanceDbReportWithoutPowerShellPropertyCrash` and then `ManualMemoryGateTests`.
- [ ] Rerun `scripts\memory-refresh-all.ps1`, `memory status`, and `scripts\memory-pre-push-check.ps1` after committing the fix.
- [ ] Retry `git push -u origin codex/memory-futureproof-phase2-roadmap`.

Current evidence:

- Push failed because the managed pre-push hook invoked `scripts/memory-pre-push-check.ps1` after full `dotnet test` had overwritten `docs/memory/generated/lancedb-sidecar-report.json` with a non-eval/probe-style report (`status=ready-to-run`, no `passed_count`).
- A RED regression test has been added in `CryptoIndicatorApp.Infrastructure.Tests/ManualMemoryGateTests.cs`: `PrePushCheckRejectsNonEvalLanceDbReportWithoutPowerShellPropertyCrash`.
- RED was confirmed: the test fails because stderr contains the missing-property crash for `passed_count`.
- GREEN was confirmed after the fix: the regression test passed `1/1`, `ManualMemoryGateTests` passed `16/16`, solution build passed with `0` warnings/errors, and `git diff --check` passed.
- Do not run `memory-refresh-all` before committing the eventual fix; memory refresh indexes committed `HEAD`.
