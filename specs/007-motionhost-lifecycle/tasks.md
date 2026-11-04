# Tasks: MotionHost lifecycle and fail-open

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/motionhost-ipc-v1.md](contracts/motionhost-ipc-v1.md)

## Phase 1: Contracts and failure semantics

- [x] T001 Add F06 lifecycle contract checks to `tests/DesktopMotionStudio.Core.Checks/F06Checks.cs` and `F06TestDoubles.cs` for existing-instance bypass, one-dispatch ordering, callback failure, skip, timeout, crash, handoff, target failure, disconnect, and cleanup.
- [x] T002 Add protocol frame tests for length bounds, exact payload reads, malformed JSON, unsupported protocol, mismatched request ID, invalid transition, unknown message kind, and the 8 KiB limit in `tests/DesktopMotionStudio.Core.Checks/F06Checks.cs`.

## Phase 2: F05 pre-dispatch seam

- [x] T003 Extend `InstanceResult` in `src/DesktopMotionStudio.Core/Instances/WindowModels.cs` with optional `LaunchPreparationErrorCode`; keep current positional call sites source-compatible.
- [x] T004 Extend `ApplicationInstanceCoordinator.OpenAsync` in `src/DesktopMotionStudio.Core/Instances/ApplicationInstanceCoordinator.cs` with an optional synchronous `beforeDispatch` observer; invoke it only on the no-existing-instance path under the AppId gate, catch observer failure, record its HRESULT, and still dispatch F04 exactly once.
- [x] T005 Pass the F05 checks, run current F00–F05 contract checks, and update `specs/006-instance-detection/spec.md`, `tasks.md`, `evidence.md`, and `review.md` with the additive F06 integration seam and regression evidence.

## Phase 3: IPC and lifecycle contracts

- [x] T006 Implement versioned request/frame/error/session contracts and the bounded length-prefixed JSON codec in `src/DesktopMotionStudio.Core/MotionHost/MotionHostContracts.cs` and `MotionHostFrameCodec.cs`.
- [x] T007 Implement `MotionHostLifecycleCoordinator` in `src/DesktopMotionStudio.Core/MotionHost/MotionHostLifecycleCoordinator.cs`; start at most one host in F05's pre-dispatch callback, preserve F05's result, hand off only on `WindowReady`, and stop the host on all other terminal states.
- [x] T008 Implement the same-user pipe and child-process session in `src/DesktopMotionStudio.Core/MotionHost/WindowsMotionHostProcessFactory.cs`; use `CurrentUserOnly`, unique pipe/request IDs, explicit `ArgumentList`, bounded handshake, EOF monitoring, graceful shutdown, and termination of only the owned MotionHost child after cleanup timeout.

## Phase 4: Isolated Windows host

- [x] T009 Create `src/DesktopMotionStudio.MotionHost/DesktopMotionStudio.MotionHost.csproj` referencing Core and implement strict argument parsing, named-pipe client handshake, frame validation, and state transitions in `Program.cs` and `HostProtocolClient.cs`.
- [x] T010 Implement the lifecycle surface in `src/DesktopMotionStudio.MotionHost/MotionHostWindow.cs`: transparent full/centered primary-display layout, accessible Skip, Esc, monotonic 15-second watchdog, reduced-motion behavior, bounded fade, and immediate close on EOF, invalid state, or shutdown.

## Phase 5: Integration, review, and convergence

- [x] T011 Add Windows host child-process/pipe integration checks to `tests/DesktopMotionStudio.Core.Checks/F06Checks.cs`; run the host with the existing WindowHost POC target and record PIDs, HWND, handoff, and cleanup behavior in `specs/007-motionhost-lifecycle/evidence.md`.
- [x] T012 Run formatting, analyzers, Release builds, full F00–F06 contract checks, Spec Kit analysis, and code-level convergence review; independently inspect fail-open/error paths and update `specs/007-motionhost-lifecycle/review.md` and `FEATURES.md`.

**Desktop acceptance note:** `F06DesktopAcceptance` verified real Win32 Esc and mouse Skip input, taskbar-excluded styles, full/centered placement, reduced-motion close, standard fade, and target survival on Windows 11 build 26200; see [evidence.md](evidence.md).

## Dependencies

- T001 precedes T003–T005 and T007; T002 precedes T006.
- T003/T004 precede T005 and T007.
- T006 precedes T008/T009; T007/T008 precede T011.
- T009/T010 depend on T006; T011 follows T008–T010; T012 follows all implementation and evidence tasks.

## Implementation strategy

Prove F05 ordering/failure semantics and codec rejection first. Implement the host orchestration against fake sessions before adding the named-pipe process boundary, then build the actual Windows surface. Run the controlled target/host desktop integration only after deterministic tests pass. Keep scene rendering, DWM, media, and GPU work outside this feature.

## Phase 6: Convergence

- [x] T013 Complete the interactive Windows acceptance for FR-012 and SC-003 using `tests/DesktopMotionStudio.Core.Checks/F06DesktopAcceptance.cs`: verify the production no-taskbar host, press Esc and activate Skip separately, confirm each closes the visual window within 500 ms without changing target launch state, verify a MotionHost process crash removes only its HWND, verify reduced-motion closes immediately and standard fade completes within 300 ms, inspect full and centered placement, and record OS build, monitor/DPI, PID/HWND, timings, and target state in `evidence.md`; rerun Spec Kit analysis/convergence and update `review.md` and `FEATURES.md`.
