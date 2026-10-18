---
name: verify-tc-dn-hofi3
description: "Drive the TC-DN-HOFI3 WPF dashboard on Windows through UI Automation. Use when proving dashboard behavior: pin, session buttons, symbol refresh, or when a change must be shown on the real window."
---

# Verify TC-DN-HOFI3

Primary surface: the Windows WPF dashboard `TC-DN-HOFI3 Dashboard`. The user touches symbol, mode, Refresh, Start, Stop, Restart, and Pin. Chart, Z_OFI, TFI, signal, book, and context tiles update only after Start.

Secondary surface: `tools/LiveDryRun` runs the same indicator pipeline without the window. It is not proof of the dashboard. There is no committed JSONL fixture. Do not treat a dry-run log as a window proof.

The desktop exe reads `appsettings.json` from its own directory (`AppContext.BaseDirectory`), not from the shell working directory. Two copies can run only as separate directory copies. Never start `CryptoIndicatorApp.Desktop\bin\...\CryptoIndicatorApp.Desktop.exe` for verification, and never drive a window whose process id is not the one `launch` recorded.

## Launch

From the repository root:

```powershell
powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 launch
```

`launch` builds `CryptoIndicatorApp.Desktop` in Debug, copies that output into `%LOCALAPPDATA%\verify-tc-dn-hofi3\runs\<run-id>\app`, and starts only that copy. The copy keeps the repo `appsettings.json`: symbol `BTCUSDT`, mode `Live`, empty replay path, recording template `recordings/{symbol}.jsonl` resolved under the copy. No database and no migration exist. Ready means the window title is `TC-DN-HOFI3 Dashboard`, `Start` is enabled, `Stop` is disabled, and Pin (`Keep window on top`) is Off. The process does not connect to Binance until Start or Refresh.

If a recorded pid is still alive, `launch` exits 1 instead of starting a second copy.

## Doctor

```powershell
powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 doctor
```

Doctor is read-only. It requires the recorded pid to be alive, the process path to be the copied exe, and the window title to match. It writes `doctor.txt` with button state and whether the window is topmost. It does not click anything. Run it before the first drive and again after anything surprising. `pin` itself still requires the fresh Off state. If doctor cannot see the recorded window, run `cleanup` and `launch` again. Do not attach to another dashboard the user already opened.

## Drive

Commands, from the repository root:

```powershell
powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 pin
powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 read -Name "Start"
powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 invoke -Name "Refresh"
powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 snapshot
```

Stable names from `CryptoIndicatorApp.Desktop/MainWindow.xaml`:

- `Refresh`, `Start`, `Stop`, `Restart` — button content, which UI Automation exposes as the name.
- `Keep window on top` — Pin. The visible caption is `Pin`.

`pin` toggles that control once from Off to On and requires both `ToggleState=On` and `WS_EX_TOPMOST`. `invoke` uses `InvokePattern`, or `TogglePattern` when the element is a toggle. `read` prints name, enabled, and toggle state.

The symbol combo and the mode combo have no `AutomationProperties.Name`. Do not click them by index or by screen position. Those journeys fail until the controls have names. See the feature map.

`invoke -Name Start` and `invoke -Name Refresh` open the public Binance market-data network. Start also creates a JSONL file under the copied app directory. Do not invoke them against a shared or repo-bin instance.

## Evidence

Proof for a run lives in `.cursor/skills/verify-tc-dn-hofi3/evidence/<run-id>/`. `launch` prints that path. Keep the action and the resulting state:

- `pin-before.txt` and `pin-after.txt` for the toggle and topmost flag.
- `doctor.txt` for the idle check.
- `named-controls.txt` from `snapshot`.
- `window.png` captured after the action, with the window moved to 40,40 so the title is on screen.

A green build is not proof. A status string is not proof of a session unless the matching side effect is visible: Pin must change the window extended style; Start must leave `Idle` and create the recording file in the copied app directory; Refresh must change the symbol-refresh text away from `Not refreshed`.

## Cleanup

```powershell
powershell -NoProfile -File .cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1 cleanup
```

Cleanup stops only the recorded pid after checking its exe path, then deletes that run's copied app directory. It does not delete `evidence/<run-id>/`. It does not stop a process by image name. After a failed launch or drive, run cleanup before retrying so the next launch is not blocked by a leftover pid.

## Helpers

The only helper is `.cursor/skills/verify-tc-dn-hofi3/scripts/verify.ps1`. Invoke it with `powershell -NoProfile -File` and one command: `launch`, `doctor`, `pin`, `snapshot`, `invoke -Name <accessible-name>`, `read -Name <accessible-name>`, or `cleanup`.

Feature recipes: `.cursor/skills/verify-tc-dn-hofi3/features/README.md`.
