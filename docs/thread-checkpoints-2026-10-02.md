# Thread Recovery Checkpoints - 2026-10-02

Исторический checkpoint **2026-10-02**. Все слова «current», «next» и результаты приёмки ниже относятся к этой дате. Актуальная карточка: [STATUS.md](../STATUS.md); исходные решения и доказательства сохранены.

## Coverage And Evidence

- Source conversation: `019e3900-1234-7441-8cb8-64b8e7236e32`, title `Создать Binance OFI/TF приложение`.
- Read all 7 available `read_thread` pages, 68 turn records, newest to oldest; final page returned `hasMore=false` and no cursor.
- Oldest turn: `019e3900-322d-7b33-b180-6fc6c1f360f1`; initial app proposal recovered.
- This API exposes turn representations, not a guaranteed complete raw transcript. Historical tool outputs were not exhaustively audited; message text and final checkpoints were recovered.
- Large first-page output was parsed from the retained full tool result, rather than relying on its truncated display. Later pages omitted command outputs. No returned message was marked truncated.
- Current source/config was inspected to distinguish implemented behavior from old plans. No product code was changed in this recovery.
- Earlier in this chat, fresh solution tests passed 71/71: Domain 19, Application 10, Infrastructure 17, Desktop 22, LiveDryRun 3. Reused because product inputs are unchanged.
- Historical live counts below are thread evidence, not fresh Binance connectivity or proof of full sample-value equality. No fresh GUI/live/publish this turn.

## Approved Product And Architecture

- Windows C#/WPF, .NET 8, single-process layered option A; not Electron, services, or .NET 10.
- Replace broad Core with Domain + Application. Desktop composes Infrastructure. Application references Domain only; this later explicit correction supersedes the original Application -> Infrastructure reference. Superseded on 2026-10-20 for adapter placement only: Infrastructure references Application and hosts the port adapters. History of this sentence stays here. Current text: [0009](decisions/0009-infrastructure-references-application.md).
- Domain has project-owned events, book sequencing, indicator math and health. Binance DTOs stay in Infrastructure; Application depends on source/recorder interfaces, not JsonlMarketEventStore.
- Binance.Net is JKorf third-party software, not an official Binance SDK. Current pinned package is 12.12.0; historical proxy investigation used CryptoExchange.Net 11.1.0.
- USDS-M Futures only; one selected symbol at a time, no multi-symbol scanning. Analytics/research hypothesis, not proven trading signal.
- No trading/orders, alerts, execution simulation, portfolio/risk execution, parameter optimization UI, generic formula plugin, database/Parquet or DSR/PBO validation in MVP.
- Formula parameters remain config-only. Do not change formula, thresholds, filters or sampling without explicit approval.
- Public market-data endpoints do not require API keys. Never request/store keys for this scope.
- Local SDK is in `.dotnet`; folder has no Git repository. Do not claim commits/worktrees exist.

## Pipeline And Recording

- Recording + replay required from first implementation; JSONL raw events, not only derived indicators.
- Version 1 envelope: source, stream, eventType, symbol, exchangeTime, receiveTime, payload with update/trade IDs.
- Supported events: DepthSnapshotEvent, DepthUpdateEvent, AggTradeEvent. Reader stops on malformed/empty rows, unknown type, unsupported version; no silent skipping.
- Live records before calculation. Replay feeds the same IndicatorPipeline and engine; 60-second sample buffer drops old values.
- Subscribe diff depth before REST snapshot, buffer updates, emit snapshot then applicable diffs, first overlap check, subsequent pu == previous u. Gaps invalidate/resync book; stale diffs skipped according to sequencing rules.
- Streams: depth@100ms and aggTrade. REST snapshot/resync only, not depth/trade calculation hot path. OI REST remains a separate slow context path.
- Recording schema currently excludes liquidation/OI and derived indicator-output logging. Calc/signal wall-clock telemetry requested in project rules is not present in current raw envelope/IndicatorSample; coverage should be audited separately.

## Formula: Current Approved Implementation

- CKS per-level OFI uses quantity contributions with bid/ask price comparison indicators. Top 3 weights are normalized exp(-0.8 * levelIndex), indices 0..2.
- HOFI sums weighted OFI over rolling 250ms. Weighted depth is sum(weight * (bidPrice*bidQty + askPrice*askQty)); reference is rolling 60s median. NOFI = HOFI / depthReference, zero if denominator zero.
- Robust z applies to NOFI history over 180s: (current - median(history)) / max(1.4826*MAD(history), 0.000001). Current value is appended after scoring, not included in its own reference.
- Warm-up: at least 30 historical samples, not a guaranteed 180s history; before that z is zero. Event-driven emission is gated to at least 100ms spacing, not a free-running timer.
- Aggressor: buyer maker true -> aggressive sell; false -> aggressive buy. Approved fix changed TFI from base qty to price*qty notional.
- TFI = (buyNotional-sellNotional)/(buyNotional+sellNotional+1e-9), rolling 250ms using trade timestamps.
- Long: fast z >= 2 AND TFI >= 0.15 AND (1s stable z >= 0.8 OR 2 of last 3 fast-z evaluations positive). Short symmetric. It is a logical gate, not a weighted final signal sum.
- Stable z has separate history and 1s HOFI window; same depth reference, 180s history and warm-up/floor.
- No implemented volume floor, spread/depth/volatility/cancel/funding kill filters. Their research-spec presence is not MVP implementation or approval to add them now.
- Follow-up audit candidate: CKS numerator remains base quantity while depth denominator is quote notional; cross-symbol dimensional interpretation needs explicit analysis, not an unapproved formula edit.

## Historical Validation Milestones

- Domain 9 tests -> JSONL/boundaries 17 -> Application 23 -> WPF 26 -> live source 30 -> proxy/dry-run 34 -> formula analysis 35 -> validity fixes 40 -> controls/symbols 45 -> compact UI/chart 51 -> Topmost 52 -> context 64 -> OI refresh/TFI amplification 68 -> raw rollback 69 -> zero line 71.
- First dry run: 92 events, 69 live / 69 replay samples, book synced, resync 0.
- Formula analysis recordings 2026-05-25: ESPORTSUSDT 390 events/125 samples; XANUSDT 236/108; PLAYUSDT 660/133. Original XAN z reached tens of billions due near-zero MAD.
- Same-file recalculation after validity fixes: ESPORTS z [-8.94286167,4.62699879], candidates L/S/N 6/14/105; XAN [-4.89913568,4.0521794], 7/7/94; PLAY [-4.56441306,3.67812624], 2/4/127. Numerical repair does not establish signal profitability.
- Later controls smoke: ESPORTS 106 events, 44/44 samples; XAN 501,60/60; PLAY 131,34/34; synced and resync 0. Tickers were active then, not verified active today.
- Context smoke: three symbols at 15m and XAN at 5m loaded OI/subscribed liquidations; no forceOrder in short 4s windows was not failure.
- Sandbox WebSocket CantConnectError.UnableToConnect was followed historically by successful same-command outside-sandbox runs; do not assume code/proxy failure from sandbox result alone.
- NU1900 persists: unavailable NuGet vulnerability metadata, not completed security validation.

## Desktop UX: Implemented And User-Confirmed

- Replaced auto-run with Symbol, Mode, Start/Stop/Restart; default Live, BTCUSDT, recording template recordings/{symbol}.jsonl.
- Refresh active TRADING + PERPETUAL symbols; editable searchable ComboBox accepts typing/paste, clears stale filtering after refresh, rejects unresolved text instead of starting previous symbol.
- Compact unified header/status and short metric row. User clarified excess metric height, not merely width.
- Fixed chart sizing: star zone and stable MinHeight; geometry tests. User subsequently confirmed live chart works.
- Signal text LongCandidate/ShortCandidate/Neutral retained with green/red intensity background. Visual strength is UI-only, not signal formula.
- Live replay path is dimmed/mode-aware, not an error. Replay has a clear missing-file message; file picker remains absent, path config-only.
- Header Pin binds Window.Topmost, default off. User confirmed window stays above others; no lower-layer changes.
- User confirmed selection, Refresh, Start/Stop/Restart and context frame switching; later confirmed OI survives refresh and observed liquidations update.
- Codex launch previously had no usable MainWindowHandle/UIA window. Automated tests cannot substitute for user-side WPF visual smoke.
- Latest published exe is under publish/desktop, dated 2026-05-26; not republished in today's recovery.

## Chart Decision History And Pending Visual Test

- Shared scaling made raw TFI subtle. Approved visual-only TFI/ThetaTfi, clipped +/-3, with raw metric unchanged; implemented then user rejected clutter hiding OFI.
- Rolled back to raw sample.Tfi and TFI legend. Regression test protects rollback. No core TFI/formula change in these experiments.
- Gray dashed neutral zero line is implemented below data, centered at canvas height/2.
- Latest user request permits four signed colors: distinct OFI/TFI positive colors and distinct negative colors. Earlier concern about static green/red series identities is not a ban on sign colors.
- White background, clear contrast, dominant opaque OFI, thinner/translucent secondary TFI so it cannot obscure OFI. Exact palette/opacity not chosen.
- Compare identical real replay segments: quiet, spikes, oscillation, overlap and zero crossings. Split lanes and TFI/Theta are comparison proposals, not selected production defaults.
- Current two strokes are #0F766E and #C2410C, both width 2; no TFI opacity yet, OFI rendered before TFI. Signed segmented geometry/zero-cross interpolation remains unimplemented.
- Audit candidate from current code: signal visual intensity clamps 0.65*abs(z)/ThetaZ + 0.35*abs(tfi)/ThetaTfi at 1. Candidates already satisfy both ratios >=1, so candidate backgrounds saturate; graded strength needs a separate approved Desktop-only fix/test.

## Independent Liquidation And OI Context

- Context for market/game dynamics, never HOFI/TFI entry/signal inputs. OI rise/fall is not long/short direction.
- 15m default and 5m selector, visible 150m = 10 or 30 tiles. Current frame change workflow uses Stop then Start/Restart.
- BUY force-order closes shorts -> positive/green; SELL closes longs -> negative/red. Net liquidation = buy filled notional - sell filled notional, not gross volume.
- Liquidation notional uses average execution price * filled quantity. Rate = net / latest OI quote value; unavailable denominator -> Waiting for OI. No events is not a subscription error.
- OI uses REST history; deltaValue = current sumOpenInterestValue - previous; relative delta = deltaValue/previous. This quote-value metric can also move with price, not exclusively contract count.
- Normalize per symbol/frame on target 24h, min 12 nonzero buckets, floor 1e-8. Sign controls hue; intensity = clamp(abs(delta)/max(floor,median(abs(history))+1.4826*MAD(abs(history)))/3,0,1).
- This final magnitude rule supersedes the earlier signed robust-z suggestion; code uses max floor, not additive floor wording in the plan overview.
- Historical OI bootstraps, config history limit 288. Liquidations have no historical stream backfill here and session-local history; warm-up resets on restart/symbol/frame change. Do not pretend immediate normalized strength is available for new symbols.
- Periodic OI refresh default 60s, 0 disables. Dedup by latest Binance timestamp; it can emit without liquidation arrivals, but polling alone does not create new OI buckets.
- Stream label/tooltip says observed snapshots, not complete total liquidations: documented largest order per symbol in 1000ms interval. User confirmed live updates, not completeness.
- Replay explicitly marks context live-only. Missing context persistence/replay is pending design, not completed parity.
- Follow-up normalization audit: old liquidation buckets divide by latest OI, and tiles use snapshot history rather than point-in-time backtest history. Evaluate semantics before adding persistence/replay.

## Stop Point And Proposed Work Order On 2026-10-02 (History)

- Next bounded slice: replay-backed visual chart comparison; choose palette/layout before production changes. User authorized visual tests, not default amplified overlay.
- Then propose context persistence/recording/replay and verify warm-up/frame boundaries. This order is a recommendation, not a blanket approval to change research logic.
- Then measure JSONL append cost, per-sample Dispatcher/geometry rebuild and unbounded queue growth before optimization; never silently drop depth or break record-before-process.
- Fresh one-symbol live/replay and user WPF smoke, publish accepted slice. Historical count equality is narrower than exact full-output equality.
- Workflow: binance-indicator-dev; TDD for implementation; prototype for visual decision. Bounded task does not require full wayfinder or unavailable legacy skills.
- User wants concise evidence, inline task-by-task TDD, warning when context appears >50%, natural checkpoints for /compact. Exact context percentage is not exposed; do not fabricate it.

## Engram Recovery

- Initial search/save failed unknown_project despite cwd basename detection. Added `.engram/config.json` with project_name prjct-indic; mem_current_project now confirms config identity.
- Registered confirmed conversation ID through mem_session_start with exact project directory. It is caller-provided real Codex ID, not an Engram-generated ID.
- Post-registration HOFI and MVP searches returned no observations. Empty-query FTS syntax error was handled with real keywords.
- Save only selected checkpoint conclusions, capture_prompt=false, full readback. Full conversation, tool logs and detailed plans are not copied to Engram.
- backup_status_checked is carried from earlier turn: 2026-10-02T03:31:43Z success, no reported error; within 4h when checked, restore untested. Not polled again.
- Saved/read back checkpoints #33-39, continuation task #40 (`task/indic-mvp-continuation/state`), summary #41 in prjct-indic and the registered real conversation session. Similarity relations were checked and resolved as compatible, not replacement decisions.
