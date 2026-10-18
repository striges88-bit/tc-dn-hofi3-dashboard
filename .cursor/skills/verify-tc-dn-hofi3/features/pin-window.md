# Pin the window

Pin keeps the dashboard above other windows. The button caption is `Pin`. Its accessible name is `Keep window on top`.

## Sub-features

- `pin-on` turns always-on-top on from a fresh idle window.
- `pin-off` turns it off again. A fresh launch is the off state.

## How to get to it (user POV)

- Open the dashboard. The Pin toggle is in the top bar, after Restart.

## Driving it with verify.ps1

Preconditions:

- `verify.ps1 doctor` reports the idle dashboard and `topmost=False`.
- The element named `Keep window on top` is Off.

- **Pin.** Choose Pin. Run `powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 pin`. `pin-after.txt` says `toggle=On` and `topmost=True`. `window.png` shows the same window.
- **Unpin.** Choose Pin again. Run `powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 invoke -Name "Keep window on top"`. A following `read -Name "Keep window on top"` reports `toggle=Off`.

## Gotchas

- The visible word is `Pin`. The automation name is `Keep window on top`. Searching for `Pin` fails.
- Toggle state alone is not proof. The window extended style must include `WS_EX_TOPMOST`.
- `pin` refuses to run unless the fresh window starts Off and not topmost.
