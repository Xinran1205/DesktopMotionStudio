# Implementation Plan: 运行实例检测与激活

**Branch**: `main` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

## Summary

Build a per-user-session launch coordinator that resolves exact executable/AUMID identity, finds and safely activates an existing confirmed window, or dispatches F04 once and waits up to 15 seconds for a window correlated to the same process creation. Windows foreground denial is reported and surfaced through one taskbar flash. No target process is killed.

## Technical Context

- **Language/version**: C# / .NET 10.0.403
- **Primary dependencies**: BCL `Process`, `Mutex`, `Stopwatch` through injectable `IInstanceClock`, and the existing Windows window and process APIs.
- **Storage**: None
- **Testing**: Existing executable contract harness; injectable process/window/time seams; Windows desktop integration using the existing WindowHost POC
- **Target platform**: Windows 11 x64, current interactive user session
- **Performance goals**: New-window wait is at most 15 seconds after F04 dispatch; polling interval 100 ms; identity scanning has no unverified fixed latency claim
- **Constraints**: No elevation, hooks, input injection, window-title/content reads, target-process termination, or NuGet dependencies. Native access denied/unknown identity fails closed.

## Constitution Check

- Exact app identity and no content capture: pass.
- Normal user rights only; no hooks/injection or target mutation: pass.
- Testable interfaces around Windows-only behavior: required and pass by design.
- Launch remains fail-open to the target app; F05 only reports window readiness and never kills a process: pass.
- Bounded waiting and structured errors: pass.
- App-specific multi-instance policy is outside this product's control and is documented in the spec: pass.

## Design Decisions

1. **Share shortcut identity parsing with F04.** Extract the current Shell Link COM resolution into one reusable internal service. Expose a typed `LaunchTargetIdentityResolver` so F04 launch and F05 matching cannot disagree about a `.lnk` target.
2. **Use exact process identity.** Executable identity is normalized full path; packaged identity is exact AUMID. Process creation time is part of post-launch correlation. Basename/title matching is prohibited.
3. **Treat window selection conservatively.** Enumerate top-level HWNDs, associate process IDs, inspect visible/cloaked/owned/minimized state, and score candidates. Prefer an exact foreground match, then the process main window, then an unowned visible candidate. Equal best scores return `AmbiguousWindow`.
4. **Keep native calls behind `IWindowPlatform`.** Production P/Invoke lives in one Windows-only implementation. Contract tests inject platform and clock fakes.
5. **Serialize by stable AppId.** A SHA-256-based `Local\` named mutex coordinates Launcher processes and reports owner termination. The owning worker waits, runs the request and releases on the same thread. Acquisition remains cancellable and bounded to 30 seconds. Rescan after successful or abandoned acquisition.
6. **Wait without holding up the target.** Start the 15-second timer after F04 returns. Poll every 100 ms using monotonic time. Cancellation stops this caller's wait only; it never cancels or kills the target.
7. **Respect foreground policy.** Restore a minimized window asynchronously, call `SetForegroundWindow` once, and flash only the taskbar three times in one bounded `FlashWindowEx` call if Windows denies foreground status.

## Project Structure

```text
src/DesktopMotionStudio.Core/
├── Launch/
│   ├── ApplicationLauncher.cs              # F04, delegates shortcut details to shared reader
│   ├── LaunchTargetIdentityResolver.cs      # exact EXE/AUMID identity, shared with F05
│   └── ShellLinkTargetReader.cs              # one Shell Link COM implementation
└── Instances/
    ├── ApplicationInstanceCoordinator.cs    # serialization, existing/new state flow
    ├── IWindowPlatform.cs                   # injectable window/process boundary
    ├── WindowModels.cs                       # identities, process stamps, results
    └── WindowsWindowPlatform.cs              # Win32/DWM implementation

tests/DesktopMotionStudio.Core.Checks/Program.cs
specs/006-instance-detection/{research,data-model,quickstart}.md
```

## Validation Strategy

1. **Small runnable proof first**: use `DesktopMotionStudio.WindowHost.Poc.exe` to confirm HWND enumeration, PID lookup, exact image path, DWM visibility, minimize/restore, foreground success/denial handling, and graceful cleanup on the current Windows desktop. Probe installed Paint AUMID process/window ownership to validate packaged matching.
2. **Contract checks**: exact path/AUMID matches, same-basename rejection, hidden/cloaked/owned filtering, ambiguity, PID reuse, process exit, timeout with fake clock, cancellation without process kill, foreground denial fallback, and same-AppId single-dispatch serialization.
3. **Windows integration**: run a real child window host through the complete coordinator and assert the returned HWND/PID and exact process stamp. Also test the installed packaged app activation/matching path if the current OS exposes a confirmable app window.
4. **Quality gate**: format verification, analyzer build, all existing contract checks, independent review, and spec/plan/task convergence.
