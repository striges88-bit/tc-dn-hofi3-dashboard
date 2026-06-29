# Formulas

Owner: TC-DN-HOFI3 formula docs (`TC-DN-HOFI3.md` and this summary).

The canonical source for the TC-DN-HOFI3 research formula is `TC-DN-HOFI3.md`.

The current implementation is in `CryptoIndicatorApp.Domain/Indicators` and must not be changed without explicit approval because threshold or sampling changes can invalidate replay comparisons.

## Implemented Core

- Top-N HOFI over the configured order book levels.
- Depth-normalized OFI.
- Robust z-score over normalized OFI with warm-up and MAD floor.
- Rolling aggressive notional TFI.
- Candidate signal gate using fast z-score, stability window, and TFI confirmation.

## Guardrails

- Treat outputs as analytics, not trading recommendations.
- Keep raw metric values separate from any UI-only visual scaling.
- Add deterministic tests before changing formula, thresholds, filters, or sampling cadence.
- Use replay files to compare before/after behavior for formula changes.
