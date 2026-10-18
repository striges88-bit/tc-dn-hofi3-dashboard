# Choose symbol

The symbol field picks one USDS-M futures symbol. A fresh window shows `BTCUSDT` from configuration. The label above the field is `Symbol`.

## Sub-features

- `symbol-shown` displays the configured symbol before any refresh.
- `symbol-edit` changes the symbol while the session is stopped. The recording path at the bottom follows the symbol.

## How to get to it (user POV)

- Click the symbol field in the top bar and type a symbol.
- After Refresh, pick a symbol from the list.

## Driving it with verify.ps1

Preconditions:

- `verify.ps1 doctor` reports the idle copied instance.
- The symbol combo has an accessible name. It does not. `MainWindow.xaml` sets no `AutomationProperties.Name` on that combo.

- **Change symbol.** Type a different symbol. No command is valid. Report failure: the symbol combo has no accessible name, so `verify.ps1` will not type into the first combo by index. The pass would show the new symbol in the field and in `Recording path`.

## Gotchas

- The editable combo has no automation name. Do not target it by tab order.
- A session in progress disables the field. Stop before editing.
- The recording line is a path template display. It is not proof that a file was written.
