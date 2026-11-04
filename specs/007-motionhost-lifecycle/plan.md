# Implementation Plan: MotionHost lifecycle and fail-open

**Branch**: `main` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

## Summary

Add a bounded Windows MotionHost process with a versioned same-user named-pipe protocol and a neutral WinForms lifecycle surface. The launcher starts the host only after F05's existing-instance scan and immediately before F04 dispatch. Host initialization and IPC run asynchronously; F05 continues target dispatch/window detection independently. Skip, host failure, timeout, or parent exit only ends MotionHost. On a ready target window the launcher requests a standard fade; all other terminal target outcomes stop the host.

This feature proves process lifecycle, fail-open orchestration, IPC, and cleanup. It does not render the licensed/media scenes or use DWM/Win2D.

## Technical Context

- **Language/version**: C# / .NET 10.0.403
- **Primary dependencies**: .NET BCL `System.Diagnostics.Process`, `System.IO.Pipes` (`CurrentUserOnly`), `System.Text.Json`; Windows Forms in the isolated host; no new NuGet dependencies.
- **Storage**: None. Pipe name and request state are ephemeral; no files or target-app data are written.
- **Testing**: Existing executable contract harness plus injected host-session/factory and clock; Windows child-process/pipe integration and interactive desktop acceptance.
- **Target platform**: Windows 11 x64, interactive current-user session, ordinary user rights.
- **Project type**: Core library additions plus an isolated Windows desktop executable.
- **Performance goals**: target dispatch is not held for host handshake; host ready/start connection is bounded to 1 second; Skip closes within 500 ms; fade is at most 300 ms; disconnect/timeout cleanup is at most 1 second; visible host lifetime at most 15 seconds.
- **Constraints**: no target path/arguments are sent to host; no arbitrary scene code, elevation, network, global input hooks or injection in production, target-window capture, target termination, or new external dependency. The Windows acceptance harness may inject local keyboard/mouse input into its test-owned window to verify user actions.

## Constitution Check

- Target application remains authoritative and launch continues through visual failure: **pass**. The F05 callback runs before one F04 dispatch and records only a non-fatal preparation failure.
- Least privilege and local first: **pass**. Current-user-only pipe, no network/elevation, only installed host executable.
- Stable boundaries: **pass**. Host contract and process session live behind Core interfaces; WinForms stays inside MotionHost.
- Capability fallback: **pass**. No fade or reduce-motion means immediate host close; no DWM dependency.
- Incremental, testable work: **pass**. Contract/fake tests precede real Windows integration.
- Versioned contracts and errors: **pass**. Protocol v1, explicit request ID, bounded frame, stable error code.
- Deterministic dependencies and licensing: **pass**. BCL only; no media or new code/content rights introduced.

## Architecture and Design Decisions

1. **Use a request-scoped named pipe, not redirected stdio.** The launcher owns one duplex `NamedPipeServerStream` with `PipeOptions.CurrentUserOnly | Asynchronous`; the child connects once through `NamedPipeClientStream` with the same-user option and receives only a random pipe name and request ID in `ArgumentList`. It sends `Ready`, then receives `Start` and later `Handoff`/`Shutdown`. The parent process handle observes child exit. Parent death closes the pipe, so the host exits.
2. **Frame IPC explicitly.** Each message is UTF-8 JSON preceded by a 4-byte little-endian length. Reject zero, over-8-KiB, malformed, unsupported-version, mismatched-request, and invalid-state frames. One connection and one session are allowed per request.
3. **Keep F05 responsible for instance serialization.** Add a source-compatible optional `Action beforeDispatch` callback to `ApplicationInstanceCoordinator.OpenAsync`; invoke it under the existing AppId gate only after exact existing-instance scan is complete and immediately before F04 dispatch. Catch callback exceptions, attach a non-fatal preparation HRESULT to the F05 result, and still call F04 exactly once. Existing callers need no changes.
4. **Start the host process synchronously, initialize it asynchronously.** The callback only creates the request pipe and starts the installed host. It does not wait for IPC or window creation, so slow initialization cannot hold up F04. The process-session task performs connection, handshake, and state tracking in the background.
5. **Separate target and visual outcomes.** `MotionHostLifecycleCoordinator` calls F05 with the pre-dispatch callback, then maps the final F05 outcome to a bounded host handoff/stop request. Host exceptions are contained and never replace F05's result. User skip is a host event only.
   If the F05 coordinator itself throws after creating the host, F06 attempts bounded shutdown/disposal and rethrows the original exception so the host cannot remain orphaned.
6. **Use a neutral, non-taskbar WinForms surface.** Render a clear status card and accessible Skip button over a transparent keyed top-level window. `Esc` and button use the same close path. Full/centered placement follows the validated manifest and current primary display. This is lifecycle UI, not a scene renderer.
7. **Use bounded cleanup of only the child host.** Request graceful shutdown, close the pipe, wait at most one second, then terminate only the launched MotionHost process if it remains. Never kill or restart the target process.
8. **Use standard fade only.** A 250 ms opacity fade is the maximum. Query the Windows client-area animation preference before starting a fade; reduced motion, unsupported opacity, or any fade error closes immediately. No DWM preview or focus-stealing operation is introduced.
9. **Make taskbar exclusion explicit.** `ShowInTaskbar=false` is paired with `WS_EX_TOOLWINDOW` and no `WS_EX_APPWINDOW`; the desktop acceptance test checks the live HWND styles on the supported Windows build.
10. **Preserve Skip during handoff races.** The protocol has a `HandoffRequested` state so Escape/Skip arriving after the launcher's handoff command but before fade completion still reports `Skipped` and cannot alter the target result.

## Project Structure

```text
src/DesktopMotionStudio.Core/
├── Instances/
│   ├── ApplicationInstanceCoordinator.cs  # additive before-dispatch observer
│   └── WindowModels.cs                     # optional preparation error code
└── MotionHost/
    ├── MotionHostContracts.cs              # request, event, error, session interfaces
    ├── MotionHostFrameCodec.cs              # bounded length-prefixed JSON frames
    ├── MotionHostLifecycleCoordinator.cs   # F05 + optional host orchestration
    └── WindowsMotionHostProcessFactory.cs   # current-user pipe and own child process
src/DesktopMotionStudio.MotionHost/
├── Program.cs                              # strict --pipe/--request-id entrypoint
├── HostProtocolClient.cs                   # child side framed IPC
└── MotionHostWindow.cs                     # lifecycle surface, Skip, fade, watchdog
tests/DesktopMotionStudio.Core.Checks/
├── Program.cs                              # dispatches the feature checks and integration modes
├── F06Checks.cs                            # F06 contract, process, and target integration checks
├── F06TestDoubles.cs                       # fake session/factory and F05 platform extensions
└── F06DesktopAcceptance.cs                 # interactive Win32 input, geometry, style, and timing checks
specs/007-motionhost-lifecycle/
├── contracts/motionhost-ipc-v1.md
├── {spec,plan,tasks,research,data-model,quickstart,evidence,review}.md
└── checklists/requirements.md
```

## Validation Strategy

1. Build and format-check Core, MotionHost, and the existing contract harness in Release with warnings as errors.
2. Add deterministic checks for F05 callback order/failure behavior, strict frame bounds/version/request ID/message decoding, fragmented reads, one dispatch, existing-instance bypass, skip, timeout, crash, disconnect, host start failure, target failure/exception, and handoff.
3. Run actual same-user named-pipe/child-process checks on Windows and record process IDs and observed exit/cleanup timing.
4. On the available interactive desktop, verify full and centered placement, taskbar absence, Esc/button, reduced-motion/standard fade behavior, 15-second timeout, parent disconnect, and no target termination.
5. Run the full F00–F06 harness, `dotnet format --verify-no-changes`, release builds/analyzers, prereq and checklist gates, independent review, and spec/plan/task convergence.

## Complexity Tracking

No constitution violation. The additional MotionHost executable is required by process-failure isolation and remains a single small host project; no separate contracts assembly is warranted yet.
