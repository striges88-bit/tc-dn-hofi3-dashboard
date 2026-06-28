## 1. Executive conclusion

**Лучший стартовый вариант: refined Indicator X — TC-DN-HOFI3**: trade-confirmed, depth-normalized, top-3 hybrid OFI, считаемый по rolling 250 ms window, но **оцениваемый event-driven / 100 ms cadence**, а не только на закрытии non-overlapping 250 ms bucket.

**Почему X-гибрид выбран:**

* OFI как класс имеет сильную методологическую базу: Cont–Kukanov–Stoikov показывают, что на коротких интервалах price change в значительной степени объясняется order-flow imbalance на best bid/ask, а slope price impact обратно пропорционален depth. ([arXiv][1])
* MLOFI добавляет информацию из уровней глубже BBO; Xu–Gould–Howison находят улучшение out-of-sample fit при добавлении уровней в MLOFI, но это было на liquid Nasdaq equities, поэтому перенос на in-play altcoin perps — только частичный. ([arXiv][2])
* Для crypto нельзя слепо доверять visible book: исследование BitMEX XBTUSD показывает, что trade-flow imbalance лучше объяснял contemporaneous price changes, чем aggregate OFI; там же отмечены lack of depth и низкие update arrival rates как отличия crypto microstructure. ([IDEAS/RePEc][3])
* Binance USDS-M depth stream поддерживает 100/250/500 ms updates, aggTrade stream — 100 ms updates и поле `m` для определения taker side; это делает top-3 rolling OFI + TFI практически вычислимым в пределах ≤200 ms, если инфраструктура рядом с matching/data region и без REST-зависимости в hot path. ([Центр разработчиков Binance][4])

**Главный риск:** сигнал может стать ловушкой adverse selection во время liquidation cascades, spoof/cancel bursts, news pumps/listings и liquidity vacuums. Это не решается формулой. Нужны жесткие kill filters, realistic queue/fill simulation и live latency replay. Ликвидационный stream Binance обновляется только раз в 1000 ms и публикует snapshot largest liquidation order, поэтому его нельзя использовать как субсекундный entry trigger; только как risk overlay. ([Центр разработчиков Binance][5])

---

## Evidence boundary

**Подтверждено источниками**

* OFI/MLOFI являются валидными microstructure features для short-horizon price impact, но исходные strongest papers — equity/LOB studies, не Binance in-play altcoin perps. ([arXiv][1])
* DSR нужен, потому что Sharpe после перебора вариантов, параметров и фильтров завышается selection bias, multiple testing и non-normal returns. ([SSRN][6])
* PBO/CSCV нужны, потому что обычный hold-out может быть ненадежным для investment backtests; Bailey et al. предлагают CSCV для оценки probability of backtest overfitting. ([SSRN][7])
* Purged K-fold / embargo применимы, когда финансовые labels перекрываются во времени; de Prado explicitly structures cross-validation in finance around purging and embargo. ([PhilPapers][8])
* Binance public depth/trade feeds технически совместимы с субсекундной feature calculation, но funding/mark stream и open interest не являются hot-path entry inputs: mark/funding push every 1s/3s, OI — REST endpoint. ([Центр разработчиков Binance][9])

**Разумная гипотеза**

Top-3 decayed OFI + same-direction TFI лучше, чем L1-only, потому что видит near-touch liquidity pressure, но не тащит шум и latency top-10/full-depth book.

**Требует проверки на данных**

DSR, PBO, realized slippage, maker queue position, signal half-life, robustness across coins/regimes, live p99 latency.

---

## 2. Comparison matrix

Весовая схема: **DSR 25% + PBO 25% + latency 20% + robustness 20% + implementation simplicity 10%**. Оценки 0–5 являются **предварительным research prior**, не результатом backtest.

| Вариант |                                                                                                                              Идея | DSR | PBO | Latency | Robustness | Impl. simplicity | Weighted score | Комментарий                                                                                                                                                                                                   |
| ------- | --------------------------------------------------------------------------------------------------------------------------------: | --: | --: | ------: | ---------: | ---------------: | -------------: | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **T**   |                                           L1 CKS OFI + depth/z normalization + mandatory trade-flow confirmation, 250 ms buckets  | 4.0 | 4.5 |     5.0 |        3.5 |              5.0 |       **4.33** | Самый чистый baseline. Низкий PBO, высокая скорость. Минус: L1 flicker и потеря near-touch absorption. Spread cap 0.6% слишком мягкий для 5–15 sec horizon.                                                   |
| **U**   |                                                           Top-heavy normalized MLOFI, L≈5, robust z, 250 ms/1s windows, TFI gate  | 4.2 | 4.0 |     4.2 |        4.3 |              3.7 |       **4.12** | Хороший engineering design. Риск: L=5 и dual-window persistence добавляют degrees of freedom.                                                                                                                 |
| **V**   |                                                                   L1 OFI + TFI на volume clock, z-score over volume buckets, EMA  | 3.4 | 3.5 |     4.5 |        3.2 |              4.4 |       **3.71** | Volume clock полезен для stationarity, но может нарушать 5–15 sec signal timing: в quiet pockets сигнал может не эмититься, в frenzy — слишком часто. Funding scalar как entry adjustment опасен для overfit. |
| **W**   |                                                        5–10 level OFI, normalization by visible depth + volatility + z, EMA, TFI  | 3.6 | 3.2 |     3.8 |        3.8 |              3.2 |       **3.64** | Идея валидная, но denominator с volatility и L up to 10 повышают fragility. Лучше использовать volatility как filter, не как core denominator.                                                                |
| **X**   | Top-3 HOFI, decayed weights, rolling depth normalization, z-score, TFI gate, spread/depth/vol/cancel/funding/liquidation filters  | 4.5 | 4.3 |     4.7 |        4.4 |              4.0 |       **4.42** | Лучший balance. Достаточно богатый, но не чрезмерный. Нужно заменить non-overlapping bucket emission на rolling/event-driven evaluation для strict ≤200 ms.                                                   |
| **Y**   |                                                                Explicit WOFI L=5, robust median/MAD z, zTFI, 100/250 ms emission  | 4.2 | 3.9 |     4.2 |        4.2 |              3.6 |       **4.07** | Сильная формализация. Минус: L=5 по умолчанию и 100 ms emission требуют более чистой L2 feed/replay дисциплины.                                                                                               |
| **Z**   |                                                          Top-10 ED-ML-GOFI, volume-time only, dynamic α, EMA depth, z-score, TFI  | 3.0 | 2.5 |     2.7 |        3.1 |              2.0 |       **2.74** | Слишком много moving parts: top-10, dynamic α, volume threshold, GOFI, 24h vol calibration. Может быть research branch, но не лучший production starting point.                                               |

---

## 3. Ranking

1. **X refined / TC-DN-HOFI3** — лучший компромисс между statistical defensibility, latency и robustness. Top-3 достаточно информативен, но не перегружен.
2. **T** — лучший low-complexity benchmark. Должен быть обязательным baseline в validation. Риск: слишком L1-sensitive.
3. **U** — хороший MLOFI design, но L=5 и dual-window logic повышают PBO относительно X.
4. **Y** — сильная explicit формула, но чуть тяжелее и шире по параметрам.
5. **V** — полезен как alternative sampling test, но volume clock хуже подходит под жесткий 5–15 sec horizon.

**W и Z ниже top-5**: W из-за over-engineered normalization; Z из-за top-10 + dynamic α + volume-time complexity.

---

## 4. Improvement plan

| Вариант | Что улучшить                                                                                       | Что опасно overfit-ить                                         | Нормализация                                                     | Что ломает сигнал                                                               | Как снизить PBO                                                                        |
| ------- | -------------------------------------------------------------------------------------------------- | -------------------------------------------------------------- | ---------------------------------------------------------------- | ------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| **T**   | Сделать L1 primary + optional top-3 confirm; заменить non-overlap 250 ms на rolling 250 ms window. | z-threshold, spread cap, trade-volume threshold per coin.      | Depth in USD + robust z over fixed 120–300 s window.             | Quote flicker, spoof/cancel burst, crossed/stale book, spread blowout.          | Оставить T как frozen benchmark; не подбирать per-symbol thresholds кроме percentiles. |
| **U**   | Снизить default L с 5 до 3 для live; L=5 только после ablation.                                    | L, decay λ, persistence 2/3, 250 ms vs 1s blending.            | Rolling median depth; MAD z-score.                               | Deeper book spoofing, stale L2, fragmented update quality.                      | Grid ≤12 configs; pooled WFO across symbols.                                           |
| **V**   | Volume clock оставить только как secondary research view; entry делать time/event rolling.         | Volume bucket size, β for TFI, funding scalar.                 | Robust z over volume buckets, but compare to time buckets.       | Quiet periods, sudden volume bursts, liquidation cascades.                      | Predefine 2–3 volume bucket sizes; не использовать funding scalar как optimized term.  |
| **W**   | Убрать volatility из denominator; использовать volatility только как halt/filter.                  | K=5/10, EMA span 3–10, vol denominator.                        | `OFI / rolling median depth`, затем robust z.                    | Volatility denominator может усиливать/ослаблять сигнал в wrong regime.         | One denominator only; ablation: depth-only vs depth+vol.                               |
| **X**   | Финализировать как top-3 rolling HOFI + TFI gate; funding/liquidation только filters.              | λ, z-threshold, TFI threshold, cancel-burst threshold.         | Rolling reference depth + median/MAD z.                          | Adverse selection, liquidations, hidden/RPI liquidity, quote stuffing.          | Freeze architecture; small global parameter grid; nested WFO + CSCV.                   |
| **Y**   | Сделать L=3 default, L=5 ablation; 100 ms emission только при proven p99 latency.                  | λ range, zOFI/zTFI thresholds, emit interval.                  | Robust median/MAD — оставить.                                    | L2 update gaps, false positives from rank-level shifts.                         | No per-coin λ; use liquidity-tier thresholds.                                          |
| **Z**   | Упростить до top-3/top-5; dynamic α заменить fixed λ; volume-time сделать auxiliary.               | α by 24h vol, top-10, volume bucket, z window, exit reversion. | EMA depth less robust than rolling median; switch to median/MAD. | Bandwidth, delayed buckets, stale deep levels, dynamic calibration instability. | Разделить на 2 experiments: GOFI correction vs volume-time; не всё сразу.              |

---

## 5. Final indicator specification

### Название

**TC-DN-HOFI3** — Trade-Confirmed Depth-Normalized Top-3 Hybrid Order Flow Imbalance.

### Гипотеза

На Binance Perpetuals in-play altcoins локальный directional impulse на 5–15 sec возникает не от одного visible imbalance, а от сочетания:

1. **passive near-touch pressure / absorption** через top-3 OFI;
2. **same-direction aggressive flow** через taker buy/sell imbalance;
3. **здоровой книги**: spread, depth, volatility, sequence integrity, no liquidation/cancel shock.

Это research hypothesis, не торговая рекомендация.

### Входные данные

Hot path:

* USDⓈ-M Futures diff depth stream: local order book, top at least 10 maintained, compute top-3.
* USDⓈ-M aggTrade stream: taker-side classification via `m`: `m=true` means buyer is maker, hence aggressive sell; `m=false` means aggressive buy.
* Exchange timestamp, receive timestamp, sequence IDs, resync count, stale/crossed/locked flags.
* Latency logs: exchange event → receive → feature calc → decision → order send/ack.

Risk/regime path:

* Mark/funding stream, 1s/3s cadence.
* Liquidation stream, 1000 ms cadence.
* Open interest REST / historical OI stats only as slow regime context, not entry trigger.

Binance explicitly documents diff depth update speeds of 100/250/500 ms, aggTrade at 100 ms, local book sequencing rules, mark/funding stream at 1s/3s, liquidation stream at 1000 ms, and OI as REST market data. ([Центр разработчиков Binance][4])

### Core formula

Для каждого order book update `n` и уровня `l ∈ {1,2,3}`:

[
e_{n,l} =
\mathbf{1}(P^B_{n,l} \ge P^B_{n-1,l})Q^B_{n,l}
----------------------------------------------

## \mathbf{1}(P^B_{n,l} \le P^B_{n-1,l})Q^B_{n-1,l}

\mathbf{1}(P^A_{n,l} \le P^A_{n-1,l})Q^A_{n,l}
+
\mathbf{1}(P^A_{n,l} \ge P^A_{n-1,l})Q^A_{n-1,l}
]

Top-heavy weights:

[
w_l=\frac{e^{-\lambda(l-1)}}{\sum_{j=1}^{3}e^{-\lambda(j-1)}}, \quad \lambda=0.8
]

Rolling 250 ms HOFI:

[
HOFI_t=\sum_{l=1}^{3}w_l\sum_{n \in [t-250ms,t]}e_{n,l}
]

Depth reference in USD, updated from past-only rolling history:

[
D^{ref}*t=\operatorname{median}*{s \in [t-W_D,t)}\left(\sum_{l=1}^{3}w_l(D^B_{s,l}+D^A_{s,l})\right)
]

[
NOFI_t=\frac{HOFI_t}{\epsilon+D^{ref}_t}
]

Robust z-score:

[
Z^{OFI}*t=
\frac{NOFI_t-\operatorname{median}*{W_Z}(NOFI)}
{1.4826 \cdot MAD_{W_Z}(NOFI)+\epsilon}
]

Aggressive trade-flow imbalance:

[
TFI_t=
\frac{V^{buyAgg}*{[t-250ms,t]}-V^{sellAgg}*{[t-250ms,t]}}
{V^{buyAgg}*{[t-250ms,t]}+V^{sellAgg}*{[t-250ms,t]}+\epsilon}
]

Stability window:

[
Z^{OFI}*{1s,t}=robustZ\left(\sum*{\tau \in [t-1s,t]}HOFI_\tau / D^{ref}_t\right)
]

### Signal logic

**Long candidate** only if all pass:

[
Z^{OFI}_{250ms,t} \ge \theta_Z
]

[
Z^{OFI}*{1s,t} \ge \theta*{stable}
\quad \text{or} \quad
Z^{OFI} \text{ same sign in 2 of last 3 evaluations}
]

[
TFI_t \ge \theta_{TFI}
]

[
V^{agg}*{250ms,t} \ge V*{floor}
]

and all filters pass.

**Short candidate** is symmetric.

Default parameters:

| Parameter             |        Default | Allowed research range | Comment                               |
| --------------------- | -------------: | ---------------------: | ------------------------------------- |
| `L`                   |              3 |           1, 3, 5 only | L=3 primary; L=5 only ablation.       |
| `λ`                   |            0.8 |                0.6–1.0 | Do not optimize per symbol.           |
| OFI window            | rolling 250 ms |             100–500 ms | Evaluate event-driven/100 ms cadence. |
| Stability window      |            1 s |                  fixed | Do not optimize aggressively.         |
| `W_D` depth reference |           60 s |               60–300 s | Rolling median.                       |
| `W_Z` z-score         |          180 s |              120–300 s | Median/MAD.                           |
| `θ_Z`                 |            2.0 |                1.6–2.5 | Global or liquidity-tier only.        |
| `θ_stable`            |            0.8 |                0.5–1.2 | Avoid per-coin fitting.               |
| `θ_TFI`               |           0.15 |              0.05–0.30 | Same-sign mandatory.                  |
| Max signal horizon    |           15 s |       5/10/15 s labels | Time stop mandatory.                  |

### Filters

**Universe filter**

* Symbol must satisfy user-defined in-play condition using past-observable data only: 24h volume > $300m and 24h price change > 20%.
* Disable newly listed / freshly migrated symbols until enough clean book/trade history exists for rolling stats.

**Spread filter**

* Disable if spread bps > rolling p90–p95 for that symbol/regime.
* Also disable if expected spread + fees + modeled slippage exceeds allowed edge budget.
* Hard spread cap should be in bps, not a universal 0.6% cap. For 5–15 sec horizon, 0.6% is usually too permissive unless the strategy is explicitly breakout/taker with very large expected move.

**Depth filter**

* Weighted top-3 depth in USD must be above rolling depth floor.
* Suggested first rule: `top3_weighted_depth >= max(p20_24h, 0.5 * median_24h)`, then validate.

**Volatility filter**

* Disable if 1s/5s/60s realized volatility exceeds rolling p99–p99.5.
* Disable during one-sided liquidation cascades or multi-tick gaps where queue-position assumptions are invalid.

**Book-health filter**

* Disable on sequence gap, failed `pu == previous u`, crossed/locked book, stale book, >3 resyncs/min, or missing trade stream.
* Local order book must follow Binance snapshot + buffered diff update process, including dropping stale updates and resyncing on continuity failure. ([Центр разработчиков Binance][10])

**Cancel-burst filter**

* Disable for 1–3 s if top-3 depth drops >70% within 1 s without confirming same-direction aggressive flow.
* Treat extreme OFI without trades as suspect passive withdrawal, not directional conviction.

**Funding / liquidation / OI filters**

* Funding and mark-price changes are slow-regime inputs, not sub-200 ms entry inputs. Binance mark/funding stream is 1s/3s. ([Центр разработчиков Binance][9])
* Liquidation stream is risk overlay only because update speed is 1000 ms and only the largest liquidation order snapshot is pushed for a symbol. ([Центр разработчиков Binance][5])
* OI REST should not be in the hot path. Use it for regime classification or daily/session filters. ([Центр разработчиков Binance][11])

### Pseudocode

```python
on_depth_update(msg, recv_ts):
    if not sequence_ok(msg):
        freeze_signal("sequence_gap")
        resync_book()
        return

    prev_top3 = book.top_levels(3)
    book.apply(msg)
    curr_top3 = book.top_levels(3)

    if book.invalid_or_stale():
        freeze_signal("bad_book")
        return

    event_ofi = []
    for level in range(3):
        e = cks_level_ofi(prev_top3[level], curr_top3[level])
        event_ofi.append(cap_event_contribution(e, level))

    rolling_250ms.add_ofi(event_ofi, recv_ts)
    rolling_1s.add_ofi(event_ofi, recv_ts)
    health.update_book(msg.exchange_ts, recv_ts)

    evaluate_signal(now=recv_ts)


on_trade(msg, recv_ts):
    # Binance aggTrade: m=True => buyer is maker => aggressive seller
    side = "sell_aggressor" if msg.m is True else "buy_aggressor"
    notional = msg.price * msg.qty

    rolling_250ms.add_trade(side, notional, recv_ts)
    rolling_1s.add_trade(side, notional, recv_ts)
    health.update_trade(msg.exchange_ts, recv_ts)

    evaluate_signal(now=recv_ts)


def evaluate_signal(now):
    if not health.hot_path_ok(now):
        emit_no_trade()
        return

    hofi_250 = weighted_sum(rolling_250ms.ofi_sum(), weights=[w1, w2, w3])
    hofi_1s = weighted_sum(rolling_1s.ofi_sum(), weights=[w1, w2, w3])

    depth_ref = rolling_depth_median.value()
    nofi_250 = hofi_250 / max(depth_ref, eps)
    nofi_1s = hofi_1s / max(depth_ref, eps)

    z_250 = rolling_nofi_stats.robust_z(nofi_250)
    z_1s = rolling_nofi_1s_stats.robust_z(nofi_1s)

    tfi = signed_trade_imbalance(rolling_250ms.buy_agg,
                                 rolling_250ms.sell_agg)

    if not filters_pass(z_250, z_1s, tfi, now):
        emit_no_trade()
        return

    if z_250 >= theta_z and (z_1s >= theta_stable or persistence_long()) and tfi >= theta_tfi:
        emit_signal("LONG_CANDIDATE")

    elif z_250 <= -theta_z and (z_1s <= -theta_stable or persistence_short()) and tfi <= -theta_tfi:
        emit_signal("SHORT_CANDIDATE")

    else:
        emit_no_trade()
```

### Latency feasibility

Strictly, **non-overlapping 250 ms buckets are not enough** for the user’s latency constraint: an event received just after bucket start could wait almost 250 ms before decision. Therefore final design uses:

```text
market data event
→ update local book / trade ring buffer
→ recompute trailing 250 ms + 1 s features
→ apply filters
→ emit candidate signal
```

Expected hot-path target:

| Stage                                   |                                            Target |
| --------------------------------------- | ------------------------------------------------: |
| WebSocket receive to parse              |                                        p99 < 5 ms |
| Book update + OFI event                 | p99 < 2 ms in Rust/C++/optimized Python extension |
| Ring-buffer aggregation                 |                                        p99 < 1 ms |
| Signal/filter decision                  |                                        p99 < 1 ms |
| Internal decision latency after receive |                                       p99 < 10 ms |
| Full receive → decision                 |                     p99 ≤ 200 ms including jitter |

This is feasible only if the market-data connection is stable, REST is excluded from hot path, streams are split by traffic type, and latency is measured live. Binance itself recommends separate public/market/private WebSocket endpoints after its WebSocket base URL split; high-frequency public data is under `/public`, regular market data under `/market`. ([Центр разработчиков Binance][12])

### Failure modes

* OFI spike caused by cancellations, not real demand.
* TFI confirms too late after move already occurred.
* Hidden/RPI liquidity not visible in public book; Binance notes RPI orders are not visible in diff depth response. ([Центр разработчиков Binance][4])
* Liquidation cascade creates mechanically one-sided flow where signal has negative convexity.
* Queue-position model wrong for maker entries.
* Backtest uses synchronized trades/book that live system cannot reproduce.
* Symbol enters in-play universe after pump already matured, creating selection bias.

---

## 6. Validation protocol

### 6.1 Dataset design

Use event-level historical/replayed data:

* diff depth updates with exchange timestamps, receive timestamps if available;
* aggTrade/trade prints with aggressor side;
* funding/mark stream;
* liquidation stream;
* open interest snapshots/history;
* latency logs;
* order acknowledgements and fill reports for live shadow / paper execution.

Minimum dataset:

* 60–120 trading days;
* all symbols that became in-play by the user’s rule;
* include failed pumps, listings, delist-risk, weekend sessions, liquidation cascades;
* keep non-in-play symbols as control group.

### 6.2 Event study labels

For each signal candidate timestamp `t`:

[
r_{h,t}=\frac{mid_{t+h}-mid_t}{mid_t}, \quad h \in {5s,10s,15s}
]

Also compute execution-aware outcomes:

* taker entry / taker exit;
* maker entry with queue-position model;
* maker entry + taker exit;
* partial fills;
* no-fill as explicit outcome, not deletion.

Primary label should be **net of spread, maker/taker fees, slippage, partial fills, cancel latency and adverse selection**. Do not evaluate raw mid-price alpha alone.

### 6.3 Walk-forward scheme

Use nested walk-forward:

```text
Train/calibration: 14–21 days
Validation/model choice: 3–7 days
Test/OOS: next 3–7 days
Roll forward
```

Constraints:

* Universe membership computed using only past data.
* Thresholds fitted only on train/validation.
* Final OOS untouched until selected.
* No per-coin cherry-picking; use global or liquidity-tier parameters.

### 6.4 Purging and embargo

Labels overlap because 5–15 sec horizons reuse future returns. Use purging for all train observations whose label interval overlaps the test interval, plus embargo after each test block.

Recommended first setting:

```text
purge = max_horizon = 15 sec
embargo = max(15 sec, 3 × feature_window) = max(15 sec, 3 sec) = 15 sec
```

If trades are clustered or holding period can extend, increase embargo to 30–60 sec. Purged K-fold/embargo is specifically intended to reduce leakage in financial cross-validation where labels overlap. ([PhilPapers][8])

### 6.5 CSCV / PBO

Candidate set must be pre-registered:

* variants: T, U, V, W, X, Y, Z, plus final TC-DN-HOFI3;
* `L ∈ {1,3,5}`;
* `θ_Z ∈ {1.6,2.0,2.4}`;
* `θ_TFI ∈ {0.05,0.15,0.30}`;
* OFI window `{100,250,500 ms}`;
* no additional hidden experiments outside the trial count.

CSCV procedure:

1. Split time into `S=16` chronological groups per symbol/regime.
2. For each combinatorial split, choose best candidate by in-sample net Sharpe or net expectancy.
3. Rank the selected candidate out-of-sample among all candidates.
4. Compute logit of OOS rank percentile.
5. **PBO = fraction of splits where logit < 0**.

Pass/fail:

| Metric |  Pass | Conditional |  Fail |
| ------ | ----: | ----------: | ----: |
| PBO    | ≤0.10 |   0.10–0.20 | >0.20 |

Bailey et al. propose CSCV specifically to estimate PBO in investment simulations and note that standard hold-out techniques can be unreliable in this setting. ([SSRN][7])

### 6.6 DSR

Calculate Sharpe on OOS net returns, then apply Deflated Sharpe Ratio using:

* actual number of trials, including discarded variants;
* skewness and kurtosis of OOS returns;
* effective sample size adjusted for autocorrelation / overlapping holding periods;
* non-normality and multiple testing correction.

Pass/fail:

| Metric          |                Pass |               Conditional |                           Fail |
| --------------- | ------------------: | ------------------------: | -----------------------------: |
| DSR probability |               ≥0.95 |                 0.90–0.95 |                          <0.90 |
| OOS net Sharpe  | positive and stable | positive but concentrated | negative or single-regime only |
| OOS expectancy  |      >0 after costs |                 near zero |                             <0 |

DSR is explicitly designed to correct Sharpe inflation from selection bias, backtest overfitting and non-normal returns. ([SSRN][6])

### 6.7 Robustness tests

Run separate reports by:

* coin;
* liquidity tier;
* spread percentile;
* volatility percentile;
* time of day;
* weekend vs weekday;
* funding-window vs non-funding-window;
* liquidation burst vs normal;
* pump continuation vs pump exhaustion;
* listing/news/manual-event windows;
* Binance vs another venue if data available.

Pass criteria:

* positive net result in ≥70% of coin-regime cells;
* no single symbol contributes >30% of total OOS PnL/edge;
* no single week contributes >40%;
* signal half-life consistent with 5–15 sec horizon;
* performance not destroyed by ±50–100 ms latency perturbation;
* stable under maker/taker fee sensitivity.

### 6.8 Latency test

Replay and live-shadow test:

```text
exchange_event_ts
recv_ts
book_apply_done_ts
feature_done_ts
decision_ts
order_send_ts
ack_ts
fill_ts
```

Pass/fail:

| Metric                               |                                  Pass |
| ------------------------------------ | ------------------------------------: |
| receive → decision p50               |                                <20 ms |
| receive → decision p99               |                               ≤200 ms |
| feature calculation p99              |                                <10 ms |
| sequence-gap freeze time             |                             immediate |
| stale/crossed book false-active rate |                                     0 |
| resyncs                              | <3/min/symbol under normal conditions |

Also test degraded conditions:

* forced WebSocket disconnect;
* delayed trade stream;
* missing depth update;
* REST snapshot delay;
* symbol burst traffic;
* 10–20 simultaneous in-play symbols.

### 6.9 Execution simulation

Include:

* maker queue model using displayed size ahead;
* partial fills and cancel/replace latency;
* taker slippage through top levels;
* spread crossing;
* fee tier sensitivity;
* post-only rejection;
* IOC/fill-or-kill behavior if used;
* adverse selection after maker fill;
* latency perturbation Monte Carlo;
* funding only if position can cross funding timestamp.

### 6.10 Final pass/fail gate

Do not move to live trading unless all are true:

1. **DSR ≥0.95** after full trial count.
2. **PBO ≤0.10**, or ≤0.20 with additional live-shadow confirmation.
3. **Net execution-aware OOS expectancy >0** after conservative costs.
4. **Latency p99 receive → decision ≤200 ms** in live environment.
5. **Robustness across coins/regimes**, not one-symbol artifact.
6. **Kill switches verified** under stale book, sequence gaps, liquidation bursts, spread blowouts.
7. **Shadow live period** shows feature distributions and signal timing match replay.

---

## 7. Data gaps

Cannot make final statistical conclusion without:

* event-level Binance USDS-M depth/trade history for the exact in-play universe;
* local receive timestamps, not only exchange timestamps;
* historical and live latency logs;
* fill/ack data for the intended order type;
* current maker/taker fee tier;
* queue-position reconstruction;
* liquidation and funding context;
* full list of all parameter trials already attempted;
* whether entries are maker, taker, or mixed;
* actual target holding/risk rules.

Preliminary conclusions that must be treated as hypotheses:

* X refined is most likely to survive DSR/PBO among the seven.
* Top-3 is likely better than L1-only but safer than top-10.
* TFI gate reduces false positives from passive book noise.
* Volume-clock designs are less suitable for strict 5–15 sec timing.
* Z is too complex for first production validation.

The selected indicator is therefore **not “profitable” by assertion**. It is the most defensible engineering starting point for a validation campaign under DSR, PBO, latency ≤200 ms and market-regime robustness constraints.

[1]: https://arxiv.org/abs/1011.6402 "[1011.6402] The Price Impact of Order Book Events"
[2]: https://arxiv.org/abs/1907.06230 "[1907.06230] Multi-Level Order-Flow Imbalance in a Limit Order Book"
[3]: https://ideas.repec.org/a/spr/digfin/v1y2019i1d10.1007_s42521-019-00007-w.html "Order flow analysis of cryptocurrency markets"
[4]: https://developers.binance.com/docs/derivatives/usds-margined-futures/websocket-market-streams/Diff-Book-Depth-Streams "Diff Book Depth Streams | Binance Open Platform"
[5]: https://developers.binance.com/docs/derivatives/usds-margined-futures/websocket-market-streams/Liquidation-Order-Streams "Liquidation Order Streams | Binance Open Platform"
[6]: https://papers.ssrn.com/sol3/papers.cfm?abstract_id=2460551 "The Deflated Sharpe Ratio: Correcting for Selection Bias, Backtest Overfitting and Non-Normality by David H. Bailey, Marcos Lopez de Prado :: SSRN"
[7]: https://papers.ssrn.com/sol3/papers.cfm?abstract_id=2326253 "The Probability of Backtest Overfitting by David H. Bailey, Jonathan Borwein, Marcos Lopez de Prado, Qiji Jim Zhu :: SSRN"
[8]: https://philpapers.org/rec/LPEAIF "Marcos López de Prado, Advances in Financial Machine Learning - PhilPapers"
[9]: https://developers.binance.com/docs/derivatives/usds-margined-futures/websocket-market-streams/Mark-Price-Stream "Mark Price Stream | Binance Open Platform"
[10]: https://developers.binance.com/docs/derivatives/usds-margined-futures/websocket-market-streams/How-to-manage-a-local-order-book-correctly "How To Manage A Local Order Book Correctly | Binance Open Platform"
[11]: https://developers.binance.com/docs/derivatives/usds-margined-futures/market-data/rest-api/Open-Interest "Open Interest | Binance Open Platform"
[12]: https://developers.binance.com/docs/derivatives/usds-margined-futures/websocket-market-streams/Important-WebSocket-Change-Notice "Important WebSocket Change Notice | Binance Open Platform"
