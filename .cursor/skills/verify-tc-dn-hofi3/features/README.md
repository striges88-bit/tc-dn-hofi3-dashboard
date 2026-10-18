# TC-DN-HOFI3 dashboard verification map

This directory is the maintained source for verifying the user-facing dashboard. Read this index, then use the matching feature file.

## Baseline preconditions

- Run `verify.ps1 launch` so the dashboard is a copied exe under `%LOCALAPPDATA%\verify-tc-dn-hofi3\runs\<run-id>\app`.
- Run `verify.ps1 doctor` and require an idle window: title `TC-DN-HOFI3 Dashboard`, Start enabled, Stop disabled, Pin off, not topmost.
- Never drive a process that this launch did not record.
- Symbol `BTCUSDT`, mode `Live`, and an empty replay path come from the copied `appsettings.json`.
- Start and Refresh need the public Binance network. Pin does not.

## Driving conventions

- Start every recipe from the idle dashboard unless its preconditions say otherwise.
- Use accessible names. Do not use coordinates or control index.
- Treat every command as literal.
- Run window actions through `.cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1`.
- Leave proof files in `.cursor/skills/verify-tc-dn-hofi3/evidence/<run-id>/`. Cleanup must not delete them.

## Proof and skip reporting

- Capture the user action and the resulting state, not only the final picture.
- UI proof includes `window.png` plus the text file that records the control state.
- A network action also needs the side effect: a new JSONL under the copied app, or symbol-refresh text other than `Not refreshed`.
- Record the feature id with the artifact.
- Report an unreachable path with the command tried and the missing precondition.
- Do not report a skipped entry point as verified through a different path.

## Feature entry contract

Each feature file has an H1, one paragraph, then four H2 sections: `Sub-features`, `How to get to it (user POV)`, `Driving it with verify.ps1`, and `Gotchas`.

## Features

- [Pin the window](./pin-window.md) keeps the dashboard above other windows.
- [Session controls](./session-controls.md) starts, stops, and restarts Live or Replay.
- [Refresh symbols](./refresh-symbols.md) loads the active perpetual symbol list.
- [Choose mode](./choose-mode.md) switches Live and Replay. The combo has no accessible name yet.
- [Choose symbol](./choose-symbol.md) edits the symbol field. The combo has no accessible name yet.
