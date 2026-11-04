# F06 Verification Evidence

**Date:** 2026-11-04
**Status:** Complete — automated, child-process, and interactive Windows acceptance passed.

## Environment

- Windows 11 Home x64, build 26200.
- .NET SDK/runtime 10.0.403 from `C:\Users\AAA\.dotnet`.
- The process integration run set `DOTNET_ROOT=C:\Users\AAA\.dotnet`; the system-wide runtime directory contained .NET 8 only.
- Windows 11 build 26200 x64; one primary monitor at 2560×1440, work area 2560×1380, MotionHost DPI 120 (125%).
- No new NuGet packages.

## Build and static checks

- Core Release build — passed, 0 warnings, 0 errors.
- MotionHost Release `win-x64` build — passed, 0 warnings, 0 errors; final build uses `ShowInTaskbar=false` plus explicit `WS_EX_TOOLWINDOW` and no `WS_EX_APPWINDOW`.
- Contract harness Release build — passed, 0 warnings, 0 errors.
- `dotnet format --verify-no-changes --no-restore` — passed for Core, MotionHost, and the contract harness.
- `git diff --check` — passed after code and documentation updates.

## Automated and process integration

Ran the complete F00–F06 contract harness with `DMS_F06_HOST_PATH` and `DMS_F06_TARGET_PATH` pointing to the built MotionHost and published WindowHost POC.

- **96 assertions passed; 0 failed.** The optional real packaged-app integration was skipped because `DMS_F05_TEST_AUMID` was not supplied.
- Protocol checks cover fragmented reads, strict text enum values, unknown/duplicate fields, request/version binding, invalid state transitions, malformed JSON, and rejection of an over-8-KiB frame before payload allocation.
- F05/F06 checks cover existing-instance bypass, host-before-target ordering, one dispatch, non-fatal host startup errors, skip/timeout/crash results, invalid scenes, target failure, thrown F05 dispatch cleanup, and preservation of the target result.
- The real MotionHost process completed the same-user named-pipe handshake and displayed a visible window (**PID 20952, HWND 8651034**); the test's `EnumWindows` PID match verified visibility. The lifecycle handoff completed in **29 ms** on the desktop's current reduced-motion setting.
- The real host's 750 ms watchdog closed its window and the child exited. Parent-disconnect cleanup completed in **65 ms** after abrupt worker exit.
- The desktop acceptance harness killed one test-owned MotionHost process; Windows destroyed its HWND within one second, while the independent target POC stayed alive.
- The real WindowHost POC remained alive through managed MotionHost handoff (**PID 26848, HWND 5638134**) and was closed normally by the test harness.
- The separate F05 real-window activation integration passed (**PID 22248, HWND 2820046**); Windows returned `ForegroundDenied`, which F05 reports without retrying focus.

## Interactive Windows acceptance

- The maintained `--f06-desktop-acceptance` mode injects actual Win32 keyboard and mouse input into the production MotionHost HWND while a test-owned WindowHost POC remains visible and running; see [quickstart.md](quickstart.md) for the reproducible command.
- Full-screen bounds matched the primary monitor `(0,0)-(2560,1440)`; centered bounds matched the work area `(900,480)-(1660,900)`. Both reported DPI 120, topmost style, and `WS_EX_TOOLWINDOW` without `WS_EX_APPWINDOW`.
- With Windows client-area animations enabled, Esc closed the visible MotionHost in **28.0 ms**, Skip closed it in **30.5 ms**, and normal handoff fade completed in **291.3 ms** with `HandedOff`.
- With client-area animations disabled, handoff closed in **30.7 ms** with `HandedOff` (within the 100 ms immediate-close acceptance bound).
- Killing the test-owned MotionHost process removed its HWND in **10.0 ms** with animations enabled and **10.3 ms** with animations disabled; the target POC remained alive in both runs.
- A test-owned WindowHost POC was started independently and kept visible while the host received Esc, Skip, standard fade, and reduced-motion close; its process and HWND remained unchanged after each action. Deterministic F06 coordinator checks verify Skip does not cancel or repeat target dispatch, and the separate real coordinator integration verifies target survival through managed handoff. The system animation preference was returned to its original disabled state and read back successfully after the enabled-animation run.
- The live check caught and fixed two gaps: `ShowInTaskbar=false` alone did not set `WS_EX_TOOLWINDOW` on this Windows build, and a user Skip racing an already-requested handoff could be reported as `HandedOff`. Explicit tool-window style and a `HandoffRequested` protocol state now cover both cases.
