# Session controls

Start runs the selected mode. Stop ends it. Restart stops and starts again. On a fresh window the status text is `Idle`, Start and Restart are enabled, and Stop is disabled.

## Sub-features

- `session-start-live` connects to Binance and writes a JSONL recording.
- `session-stop` returns the dashboard to a stopped state.
- `session-replay` plays a local JSONL file through the same pipeline. It needs a replay path.

## How to get to it (user POV)

- Choose `Start`, `Stop`, or `Restart` in the top bar.
- Live is the default mode. Replay is the other mode, and its path is shown at the bottom as `Replay path`.

## Driving it with verify.ps1

Preconditions:

- `verify.ps1 doctor` reports the idle copied instance.
- For Live start, the machine can reach public Binance market data. The copied app must be the one recording, so the new JSONL appears under that run's `app` directory, not under the repository `recordings` folder.
- For Replay start, `Dashboard.ReplayPath` in the copied `appsettings.json` must point at a real JSONL file before launch. The repo does not ship one. If it is missing, report failure `Replay file not found` or `Replay path not configured`. Do not invent market events to force a pass.

- **Read idle controls.** Run `powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 read -Name "Start"` and the same command with `-Name "Stop"`. Start is enabled. Stop is disabled.
- **Start Live.** Choose Start. Run `powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 invoke -Name "Start"`. Then `read -Name "Stop"` is enabled. A `BTCUSDT.jsonl` file exists under the copied app `recordings` directory. Status leaves `Idle`.
- **Stop.** Choose Stop. Run `powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 invoke -Name "Stop"`. Status becomes `Stopped` or `Completed`, and Start is enabled again.
- **Proof.** Run `snapshot`. `window.png` and `named-controls.txt` show the status after Stop, and the JSONL file is still on disk.

## Gotchas

- Start on Live opens the network and writes JSONL. Pin is the offline proof.
- The default copied config has an empty replay path. Replay start fails closed with `Replay path not configured`.
- Restart is enabled even while idle because it only requires a valid symbol. Do not treat an enabled Restart as proof that a session is running.
- Status text such as `Running` is not enough. The recording file must exist for a Live start.
