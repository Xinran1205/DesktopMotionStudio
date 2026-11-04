# F05 verification evidence

**Date:** 2026-11-04

**Environment:** Windows 11 Home x64, build 26200 · .NET SDK 10.0.403

**Dependencies:** BCL and Windows APIs only; no NuGet package added.

## Build and static checks

- `dotnet format .\src\DesktopMotionStudio.Core\DesktopMotionStudio.Core.csproj --verify-no-changes --no-restore` — passed.
- `dotnet format .\tests\DesktopMotionStudio.Core.Checks\DesktopMotionStudio.Core.Checks.csproj --verify-no-changes --no-restore` — passed.
- Release builds passed with zero warnings and zero errors for Core, the Windows x64 WindowHost POC, and the contract harness.
- `git diff --check` — passed.

## Automated checks

Ran the complete executable contract harness with `DMS_F05_TEST_AUMID=Microsoft.Paint_8wekyb3d8bbwe!App`.

- **67 executable assertions passed; 0 failed.** Two optional F04 package-specific branches printed their skip result because `DMS_TEST_AUMID` was unset; the F05 real packaged-app integration below did run.
- Coverage includes all previous F00–F04 contracts, typed EXE/AUMID identity resolution, shortcut resolver reuse, exact identity matching, same-basename rejection, candidate filtering, ambiguity, PID reuse, process exit and identity change, launch-stamp fail-closed behavior, bounded timeout, cancellation without process termination, foreground denial, same-AppId and separate-AppId concurrency, cross-process semaphore coordination, and gate timeout.
- The cross-process test starts two independent harness processes; the shared dispatch marker was created once and both callers returned the same process/window identity.

## Windows desktop integration

- The harness copied the benign published WindowHost POC to a unique temporary path so no pre-existing process could match it. F05 returned `WindowReady` with PID `18128` and HWND `786790`.
- It minimized the same window and called F05 again with the same AppId. F05 restored and selected the same PID/HWND. Windows denied foreground activation in this run, so the result was `ForegroundDenied`; the implementation issued one bounded taskbar-only flash request and did not retry focus.
- The test closed the WindowHost through `WM_CLOSE`. PID `18128` exited normally.
- With Paint confirmed absent before activation, the harness activated `Microsoft.Paint_8wekyb3d8bbwe!App`, confirmed exact process AUMID and returned `WindowReady` for PID `436`, then closed that HWND through `WM_CLOSE`. PID `436` exited normally.
- Post-run checks confirmed both test PIDs were gone and the temporary WindowHost and cross-process test directories were removed.

## Limits exercised or documented

- Foreground permission is controlled by Windows. `ForegroundDenied` is a supported result, not a launch failure; no focus bypass is attempted.
- Packaged apps whose window owner process does not expose the requested exact AUMID fail closed. F05 does not infer identity through a parent/child relationship.
- Without reading titles/content or using app-specific metadata, an unowned visible splash window cannot be distinguished generically. `WindowReady` confirms a structurally usable window, not that app content has finished loading.

## F06 additive pre-dispatch integration

- A subsequent F06 regression pass verified that an existing matching instance activates without invoking the observer or target dispatch.
- The new-launch path invoked the host observer before exactly one target dispatch. A simulated observer/process-start exception was recorded in `LaunchPreparationErrorCode`; the target still reached `WindowReady`.
- Existing calls without an observer remain valid. The F06 integration seam is additive to the F05 behavior and does not alter its target identity, wait, or activation rules.
