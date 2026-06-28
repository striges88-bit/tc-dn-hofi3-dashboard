# Lessons

Record feedback-driven mistake patterns here after reviews, corrections, or fixes.

## Active Rules

- When the session context approaches 50% full, stop at a clean handoff point for `/compact`; record completed verification, remaining work, and the next pending command before continuing substantial work.
- When a WPF project references a layer named `Application`, fully qualify `System.Windows.Application` in `App.xaml.cs` because the project namespace can shadow the WPF type.
- Keep the `Application` layer independent from concrete Infrastructure services such as `JsonlMarketEventStore`; expose source/recorder interfaces in Application and compose JSONL/Binance adapters only in Desktop or another outer layer.
- After adding package references, run restore before `--no-restore` verification; otherwise compile errors can be caused by stale assets rather than source code.
- Do not request or store Binance API keys for public market-data features; USDS-M depth snapshots, diff depth streams, and aggTrade streams are public.
- Treat manual ticker refresh and proxy settings as outer infrastructure/config features, not as part of indicator formula or Application pipeline logic.
- For CryptoExchange.Net `ApiProxy`, normalize HTTP proxy hosts with an explicit `http://` scheme before passing them to the library; its handler builds a URI from `proxy.Host` plus `proxy.Port`, so a bare host can fail at runtime.
- For C# top-level program files, helper types declared after top-level statements are namespace-level types; do not mark them `private`.
- Live Binance WebSocket dry runs may fail inside the sandbox even when normal network is enabled; rerun the exact command with user-approved escalation before diagnosing Binance or proxy code.
- `IndicatorSample.ExchangeToReceiveLatency` is nullable; summary and latency tooling must handle missing latency values explicitly.
- Robust z-score needs warm-up and a denominator floor before its output is treated as signal evidence; early flat `NOFI` history can make MAD effectively zero and produce enormous `Z_OFI`.
- Keep CLI option parsing for tools in a public, testable class instead of burying it inside a top-level `Program.cs`; replay/reporting modes then get deterministic tests without invoking live network paths.
- When implementing TC-DN-HOFI3 stability, include both parts of the documented gate (`1s stable z` and `2 of last 3` fast-z persistence) unless explicitly deferring one; otherwise the implementation silently becomes more conservative than the spec.
- For WPF chart/UI work, a passing ViewModel test is not enough; verify container sizing and non-empty rendered geometry/points because an `Auto` row plus unconstrained `Canvas` can make live data invisible.
- For editable ticker ComboBox UX, never leave `Start` enabled when the typed search text does not resolve to the selected symbol; otherwise the app can launch a stale previous symbol while the input displays an invalid ticker.
- After refreshing a large symbol list, reset the filtered view to the full active list instead of applying stale search text from the previous selection.
- When plotting heterogeneous indicator series together, avoid raw shared scaling if one series is bounded and another is a z-score; use semantic visual normalization, label the transformed line clearly, and keep raw metric values unchanged.
- Do not count a WPF visual smoke as complete from Codex if the launched process has no `MainWindowHandle` or UI Automation top-level window; record automated checks separately and leave visual verification user-side.
- If threshold-normalized TFI makes the combined chart unreadable, do not keep tuning the overlay; revert to raw TFI or move the transformed series into a separate lane.
- For signed indicator chart colors, avoid assigning green/red as static series identities when both series can be positive or negative; encode series identity and sign separately, and keep secondary context lines lower-opacity than dominant signals.
- Do not put another user's absolute profile path into durable project instructions; prefer `%USERPROFILE%` or a project-relative path, and state the fallback if an optional local skill is missing.
- In PowerShell scripts, do not compute default parameter values from `$PSScriptRoot`; under some launch forms it can be empty during parameter binding. Accept an empty parameter and resolve `$PSScriptRoot` or `$PSCommandPath` inside the script body.
- In PowerShell helper functions, do not mark mutable collection parameters as mandatory when an empty collection is valid; parameter binding can reject the call before the function body runs.
- For scripts launched with `powershell.exe` on Windows PowerShell 5.1, avoid .NET Core-only APIs such as `ProcessStartInfo.ArgumentList` and `WaitForExit(TimeSpan)`; use `StartInfo.Arguments` and millisecond timeout overloads instead.
- For local daemon/service startup on Windows, do not run long-lived launchers directly in a visible shell. Use a hidden/no-window process with a bounded timeout, then poll service status/endpoints separately and kill only the verified launcher tree if startup hangs.
- After a coherent, verified work slice, proactively commit in Git instead of letting reviewed changes accumulate; if scope is mixed, secrets/generated state are involved, or verification is incomplete, stop at a clean point and ask before committing.
- Codex may inherit a stale PATH after Git/GitHub CLI installation; verify common install paths and use absolute executables for the current session, then restart Codex/terminal for refreshed Machine PATH.
- Codex may also inherit a stale PATH after WinGet package installation; if a user-scoped tool was just installed, probe the WinGet package directory under `%LOCALAPPDATA%\Microsoft\WinGet\Packages` before declaring it unavailable.
- When evaluating optional memory tools, distinguish upstream existence/API confirmation from local installation status; "not found in PATH" is only a local availability result, not evidence that the project or CLI does not exist.
- When replacing a preferred external memory tool, keep the old spike as historical/secondary unless there is a concrete reason to delete it; update retrieval priority and tests so stale candidate docs do not rank as current.
