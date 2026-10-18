# Refresh symbols

Refresh loads the active Binance USDS-M perpetual symbols into the symbol field. Before that, the dashboard only lists the configured symbol and the refresh line says `Not refreshed`.

## Sub-features

- `symbols-load` replaces the one configured symbol with the exchange list.
- `symbols-fail` shows the exchange error in the refresh line. It does not clear the current symbol.

## How to get to it (user POV)

- Choose `Refresh` in the top bar, next to Start. The line labeled `Symbol refresh` is to the right of Pin.

## Driving it with verify.ps1

Preconditions:

- `verify.ps1 doctor` reports the idle copied instance.
- The symbol-refresh text is `Not refreshed`.
- Public Binance market data is reachable. If it is not, the journey fails with the error text from the client. Do not mark it passed because the button clicked.

- **Refresh.** Choose Refresh. Run `powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 invoke -Name "Refresh"`. Wait until the refresh line is no longer `Not refreshed`. Success text ends with `active perpetual symbols`. Failure text is the exception message, or `Refresh failed` when the message is empty.
- **Proof.** Run `snapshot`. `named-controls.txt` includes the new refresh text. The symbol field still shows a selected symbol.

## Gotchas

- Refresh is a network call. An unchanged `Not refreshed` line means the click did not finish.
- The symbol combo has no accessible name, so the proof of the list is the refresh status text, not a combo index.
- Do not invoke Refresh on a window this launch did not start.
