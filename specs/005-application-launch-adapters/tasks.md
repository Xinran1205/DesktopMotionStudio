# Tasks: 应用启动适配器

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

## Phase 1: Typed launch contract

- [x] T001 [US1] Define launch target, status, and result contracts in `src/DesktopMotionStudio.Core/Launch/LaunchRequest.cs`.

## Phase 2: Target adapters

- [x] T002 [US1] Validate EXE path, working directory, argument count, and command-line size, then start with `ArgumentList` in `src/DesktopMotionStudio.Core/Launch/ApplicationLauncher.cs`.
- [x] T003 [US1] Resolve `.lnk` through Shell Link COM, accept only an existing EXE target, preserve shortcut arguments and working directory, and release COM objects in `src/DesktopMotionStudio.Core/Launch/ApplicationLauncher.cs`.
- [x] T004 [US1] Validate AUMID delimiter, control characters, and Windows length limit; activate through `IApplicationActivationManager` on an STA thread and return HRESULT/PID in `src/DesktopMotionStudio.Core/Launch/ApplicationLauncher.cs`.

## Phase 3: Contract and Windows integration checks

- [x] T005 [US1] Add checks for malformed/oversized AUMIDs, invalid targets, direct and shortcut argument fidelity, one dispatch, and structured permission failures in `tests/DesktopMotionStudio.Core.Checks/Program.cs`.
- [x] T006 [US1] Run benign EXE, executable/non-EXE shortcuts, installed/missing AUMID checks, and record Windows results in `specs/005-application-launch-adapters/evidence.md`.

## Phase 4: Review and convergence

- [x] T007 [US1] Verify formatting, analyzers, build, spec coverage, and implementation boundaries; record review in `specs/005-application-launch-adapters/review.md`.

## Dependencies

`T001` precedes `T002`–`T004`; adapter checks depend on their corresponding implementation. `T007` follows all test tasks. No tasks are parallelized because each adapter extends the same facade and the integrated test harness is shared.

## Independent test

Run `dotnet run --project tests/DesktopMotionStudio.Core.Checks/DesktopMotionStudio.Core.Checks.csproj -c Release` on Windows 11. The F04 checks exercise EXE startup, an EXE-targeting `.lnk`, a non-EXE shortcut, malformed targets, permission failure mapping, argument integrity, one-dispatch behavior, and a known installed AUMID supplied through `DMS_TEST_AUMID`.
