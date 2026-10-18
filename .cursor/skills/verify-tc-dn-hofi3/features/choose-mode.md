# Choose mode

Mode chooses Live or Replay before a session starts. The label is `Mode`. The choices are `Live` and `Replay`. Replay path text at the bottom is dim in Live and solid in Replay.

## Sub-features

- `mode-live` is the fresh state from `appsettings.json`.
- `mode-replay` shows the configured replay path at full opacity. It does not start playback until Start.

## How to get to it (user POV)

- Open the Mode dropdown in the top bar, between the symbol field and Refresh.
- Read `Replay path` at the bottom left.

## Driving it with verify.ps1

Preconditions:

- `verify.ps1 doctor` reports the idle copied instance.
- The Mode combo has an accessible name. It does not. `MainWindow.xaml` sets no `AutomationProperties.Name` on that combo.

- **Switch to Replay.** Choose Replay in the Mode dropdown. No command is valid. Report failure: the Mode combo has no accessible name, so `verify.ps1` will not click it by index. The pass would replace the bottom text `Live mode` with the replay path, or `Replay file not configured` when the path is empty, without starting a session.

## Gotchas

- Clicking the second combo by index is not a verification. The journey stays failed until the control has a stable name.
- Changing mode while a session is running is disabled. Start locks configuration.
- An empty replay path still switches the mode. Start then fails with `Replay path not configured`.
