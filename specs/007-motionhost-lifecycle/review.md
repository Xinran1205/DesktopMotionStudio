# F06 Independent Review

**Date:** 2026-11-04 · **Result:** Pass — specification, implementation, regression checks, and Windows desktop acceptance converge.

## Specification analysis

| Area | Result |
|---|---|
| Functional coverage | FR-001 through FR-012 map to host contracts, coordinator, process boundary, UI, and recorded Windows evidence. |
| Success criteria | SC-001 through SC-006 have automated, real process, or desktop evidence. SC-003 and FR-012 pass with measured Esc/Skip closure, reduced-motion close, and normal fade. |
| Task coverage | T001 through T013 are implemented, verified, and checked. |
| Constitution | No conflict. Production remains fail-open, same-user, no elevation, no injection, no target capture, and no target termination. Test-only SendInput drives the owned acceptance window. |
| Plan consistency | Core owns protocol/lifecycle; WinForms remains isolated. The launcher starts the visual process before one dispatch. `HandoffRequested` preserves a Skip racing with handoff. |
| Error handling | Host failure is advisory; target launch failure shuts down the host; unexpected F05 exceptions clean up the host and propagate. Stable host errors and native codes are retained. |
| Compatibility | Release builds target .NET 10 / Windows x64. Runtime packaging remains an F12 distribution decision. |

## Code review notes

- IPC frames are size-bounded, strict JSON objects with textual enums, duplicate/unknown-field rejection, request binding, and independent protocol versioning.
- Both pipe endpoints use `CurrentUserOnly`; process launch uses an absolute executable and discrete arguments. Cleanup targets only the owned MotionHost child.
- `ApplicationInstanceCoordinator.beforeDispatch` runs under the AppId gate after exact-instance detection; observer errors are non-fatal and dispatch still occurs once.
- The live Windows run showed that `ShowInTaskbar=false` alone did not yield `WS_EX_TOOLWINDOW` on build 26200. MotionHost now sets the tool-window style explicitly and clears app-window style.
- The live Esc test exposed a protocol race when Skip arrived after a handoff command. The additional `HandoffRequested` state keeps Skip terminal and preserves `Finished` sequencing.
- The test harness copies the complete target POC output so framework-dependent .NET sidecars are present. Its desktop mode uses SendInput only against the test-owned window and never changes Windows preferences.

## Verification summary

- Release builds for Core, MotionHost, WindowHost POC, and the full check harness passed with **0 warnings / 0 errors**.
- `dotnet format --verify-no-changes --no-restore` passed for Core, MotionHost, and checks; `git diff --check` passed.
- Full F00–F06 harness: **96 passed, 0 failed, 1 optional packaged-app integration skipped** because `DMS_F05_TEST_AUMID` was not provided.
- Windows 11 build 26200, one 2560×1440 monitor, 125% DPI: Esc **28.0 ms**, Skip button **30.5 ms**, reduced-motion close **30.7 ms**, standard fade **291.3 ms**. Full and centered bounds, taskbar-excluded styles, and target survival passed.
- Desktop process-crash acceptance removed the owned host HWND in **10.0 ms** with animations enabled and **10.3 ms** with animations disabled; the independent target POC stayed alive.
- Managed target stayed alive through handoff; watchdog and abrupt parent disconnect cleaned up the host, with parent-disconnect cleanup at **65 ms**.

## Convergence

T013 is complete. The implementation, plan, tests, and evidence agree; no known acceptance gaps remain within F06's lifecycle-shell scope. Scene rendering, licensed assets, DWM, and packaged-app live acceptance remain later feature scope.
