# F05 Windows API proof

**Date:** 2026-11-04 · **Environment:** Windows 11 x64 build 26200 · **Runtime:** .NET 10.0.403

## Executable window

Ran `specs/006-instance-detection/poc/WindowIdentityProbe.ps1` against the repository's published WindowHost POC.

- `EnumWindows` found exactly one visible, uncloaked candidate for the exact published EXE path.
- `GetWindowThreadProcessId` returned PID 25368 and `QueryFullProcessImageNameW` matched the requested full path.
- The probe minimized the POC window, `ShowWindowAsync(SW_RESTORE)` accepted the restore, and `SetForegroundWindow` succeeded; `GetForegroundWindow` confirmed the same HWND.
- The POC window closed through its normal main-window close path.

## Packaged application identity

- `IApplicationActivationManager::ActivateApplication` returned `S_OK` and PID 13584 for `Microsoft.Paint_8wekyb3d8bbwe!App`.
- `GetApplicationUserModelId` returned that exact AUMID for PID 13584.
- `EnumWindows` found five top-level HWNDs owned by the returned PID: one visible and four hidden. The visible main window was selected for UI inspection; the blank canvas was closed after the check.
- This machine's Paint window is directly owned by its packaged app process. F05 requires the HWND owner process itself to expose the exact AUMID; it does not infer a host/child relationship. Other packaged apps with separate host processes may therefore return `NoUsableWindow` until their host exposes a confirmable identity.

## Limits

- Foreground activation succeeded because the POC was launched by the active test process. A denied-focus behavior test remains an injected contract test; the live desktop cannot deterministically reproduce every foreground-lock condition.
- `FlashWindowEx` returns the window's prior active state, not an operation-success boolean. Production results therefore derive from `SetForegroundWindow`/`GetForegroundWindow`; the taskbar call is verified by an injected call-count test.
