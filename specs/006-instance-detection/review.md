# F05 independent review and convergence

**Date:** 2026-11-04 · **Result:** Pass · **Status:** Converged

## Specification analysis

| Area | Result |
|---|---|
| Functional coverage | FR-001 through FR-010 map to resolver, process/window platform, coordinator, and contract or desktop tests. |
| Acceptance coverage | SC-001 through SC-005 have automated or real Windows evidence in `evidence.md`. |
| Task coverage | T001 through T010 are implemented and checked; no task is unmapped. |
| Constitution | No conflict found. The implementation uses normal user rights, exact identities, bounded synchronization, structured outcomes, and testable Windows seams. It reads no title or pixels, injects no input, and never terminates a target. |
| Plan | Shared `.lnk` resolution is used by F04 and F05; Win32 calls are behind `IWindowPlatform`; same-AppId synchronization uses a session-local named semaphore; polling uses an injectable monotonic clock. |
| Failure handling | Invalid input, unavailable identity, missing launch stamp, PID reuse, process exit, timeout, gate timeout, cancellation, ambiguity, and foreground denial have explicit outcomes. Timeout and cancellation leave the process running. |
| Compatibility | Verified on Windows 11 x64 build 26200 with .NET 10.0.403. No third-party dependency was introduced. |

## Implementation review

- Shortcut COM reading now lives in `ShellLinkTargetReader`; `LaunchTargetIdentityResolver` resolves a typed, immutable identity without dispatching a launch. `ApplicationLauncher` consumes that same resolved target and validates it before dispatch.
- The launch result carries an optional process creation file time. F05 refuses to correlate a new PID when that stamp is unavailable.
- Process scans match normalized full EXE paths or case-insensitive exact AUMIDs. Window candidates must belong to the confirmed PID and pass visible, uncloaked, unowned, and non-tool checks. Identity, HWND ownership, and structural visibility are revalidated before activation.
- A minimized window is restored asynchronously. `SetForegroundWindow` is called once. If Windows denies it, one `FlashWindowEx` call requests three finite taskbar-only flashes; the call's return value is not treated as success/failure.
- The same-AppId gate is capped at 30 seconds. The first caller keeps it through launch/window discovery; subsequent callers rescan and reuse the process/window. Different AppIds use different gates.

## Convergence findings

No remaining implementation gaps were found against `spec.md`, `plan.md`, `tasks.md`, or the constitution. Two behavior boundaries were clarified in spec version 1.0.1: gate wait is capped at 30 seconds, and generic unowned splash windows cannot be classified without app-specific data or reading content. This matches the implementation's no-title-read constraint and the existing definition that window readiness does not imply content readiness.

**Final checks:** 67 executable assertions passed; two optional F04 packaged-target branches were skipped, and the F05 packaged-app integration passed. Core, WindowHost POC, and contract harness Release builds passed with zero warnings/errors; both format verification commands and `git diff --check` passed. See [`evidence.md`](evidence.md) for environment and live desktop results.
