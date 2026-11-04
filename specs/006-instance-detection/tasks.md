# Tasks: 运行实例检测与激活

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [quickstart.md](quickstart.md)

## Phase 1: Shared identity contract

- [x] T001 Add contract checks for exact EXE/AUMID identity and `.lnk` resolution reuse in `tests/DesktopMotionStudio.Core.Checks/Program.cs`.
- [x] T002 Extract Shell Link COM reading into `src/DesktopMotionStudio.Core/Launch/ShellLinkTargetReader.cs` and expose typed, side-effect-free identity resolution in `src/DesktopMotionStudio.Core/Launch/LaunchTargetIdentityResolver.cs`; update `src/DesktopMotionStudio.Core/Launch/ApplicationLauncher.cs` to share the reader.

## Phase 2: Windows API proof

- [x] T003 Run the real-window and packaged-app identity proof on Windows and record HWND/PID/path/AUMID/visibility/focus results in `specs/006-instance-detection/poc/evidence.md`.

## Phase 3: User Story 1 - Activate an existing app window

**Independent test**: A fake platform returns an exact existing process/window match; verify activation is attempted and the launcher dispatch count remains zero.

- [x] T004 Add tests for exact path/AUMID matching, same-basename rejection, hidden/cloaked/owned filtering, ambiguity, PID reuse, and foreground denial in `tests/DesktopMotionStudio.Core.Checks/Program.cs`.
- [x] T005 Implement process/window models and the injectable platform contract in `src/DesktopMotionStudio.Core/Instances/WindowModels.cs` and `src/DesktopMotionStudio.Core/Instances/IWindowPlatform.cs`.
- [x] T006 Implement Windows process/window discovery, exact identity validation, candidate scoring, restore, foreground activation, and one bounded taskbar notification in `src/DesktopMotionStudio.Core/Instances/WindowsWindowPlatform.cs`.

## Phase 4: User Story 2 - Launch once and wait for a new window

**Independent test**: A fake clock/platform covers immediate, delayed, missing, exited, changed-identity, canceled, and concurrent same-AppId outcomes without relying on desktop timing.

- [x] T007 Add coordinator tests for the 15-second limit, no process kill, missing creation stamp rejection, same-AppId single dispatch across separate coordinator processes, separate-AppId independence, and the structured result states in `tests/DesktopMotionStudio.Core.Checks/Program.cs`.
- [x] T008 Implement per-session same-AppId serialization, F04 dispatch, process-stamp correlation, bounded polling, and cancellation semantics in `src/DesktopMotionStudio.Core/Instances/ApplicationInstanceCoordinator.cs`.
- [x] T009 Add real WindowHost and optional packaged-app integration cases to `tests/DesktopMotionStudio.Core.Checks/Program.cs`; verify exact identity/PID/HWND and close test windows normally.

## Phase 5: Review and convergence

- [x] T010 Run formatting, analyzers, Release build, all contract checks, independent review, and convergence; record results in `specs/006-instance-detection/evidence.md` and `specs/006-instance-detection/review.md`.

## Dependencies

`T001` precedes `T002`. T003's proof is complete and gates native implementation. `T004` precedes T005/T006. `T007` precedes T008. T009 depends on the complete coordinator and Windows platform. T010 follows all implementation and test tasks. Tasks share the single executable harness and are intentionally sequential.

## Implementation strategy

Deliver User Story 1 first so exact identity matching and existing-window activation can be validated independently. Then add User Story 2 without changing the established activation contract. Native activation remains behind `IWindowPlatform`; all timeout/concurrency paths use deterministic fakes before real desktop verification.
