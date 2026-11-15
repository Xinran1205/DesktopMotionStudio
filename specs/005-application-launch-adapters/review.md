# F04 independent review and convergence

**Date:** 2026-11-04 · **Result:** Pass

| Area | Review result |
|---|---|
| Spec and plan coverage | Every F04 requirement and success criterion maps to implementation tasks and the recorded test evidence. |
| Architecture | Typed launch requests and results are isolated in `LaunchRequest`; EXE, Shell Link, and packaged activation paths remain behind `ApplicationLauncher`. Window discovery is deferred to F05. |
| Safety | Direct launches use `UseShellExecute=false` and `ArgumentList`; shortcuts are resolved and restricted to EXE targets; AUMID input rejects control characters and values beyond the Windows API limit. No elevation request, network discovery/download, or target-file mutation is introduced. Windows may resolve an explicitly configured network-backed launch path. |
| Error handling | Invalid input returns `InvalidTarget`; expected process/IO/permission/COM failures return `StartFailed` with native/HRESULT data when available. COM objects and native argument buffers are released in `finally` blocks. |
| Compatibility | Windows-only behavior is guarded; the AUMID COM call runs on an STA thread. Verified on Windows 11 x64 build 26200 with .NET 10.0.403. |
| Regression | The shared F01–F04 harness passed 45 checks; release build and both format verifications passed. |

No unresolved discrepancy blocks F04. Repeated launch coordination and activation of an already-running app are explicitly assigned to F05.

## F05 contract extension

F05 adds `ProcessCreationFileTimeUtc` to `LaunchResult` as an optional value. The EXE adapter captures it immediately after `Process.Start`; packaged activation attempts to capture it immediately after `ActivateApplication`. F05 fails closed with `IdentityUnavailable` when this stamp is unavailable. The F04 launch statuses and single-dispatch behavior are unchanged.
