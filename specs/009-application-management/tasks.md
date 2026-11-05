# Tasks: My applications

**Input:** spec.md, plan.md, data-model.md, research.md, contracts/*

## Phase 1: Feasibility and setup

- [X] T001 Prove WPF build/show/close and current-user discovery metadata with an isolated fixture; record evidence in `specs/009-application-management/evidence.md` before UI implementation.
- [X] T002 Create the Studio project and record WPF rationale in `docs/DECISIONS.md`; retain Core/renderer separation in `src/DesktopMotionStudio.Studio/DesktopMotionStudio.Studio.csproj`.

## Phase 2: US4 - Compatible state foundation

- [X] T003 [US4] Add migration/strict-format checks in `tests/DesktopMotionStudio.Core.Checks/F08Checks.cs`: unchanged legacy bytes/raw strings/IDs, first mutation backup, idempotence, future-before-kind rejection, duplicate properties, limits, and unavailable typed paths still loading.
- [X] T004 [US4] Add typed definitions in `src/DesktopMotionStudio.Core/Applications/ApplicationTargetDefinition.cs` and optional profile Target/schema 2 in `State/StudioState.cs`; update `StudioStateRepository.cs` for strict bounded version-aware reads and non-writing schema-1 migration. Preserve 128/120/2048 field bounds, 256 arguments/30000 chars, 4 MiB/1000-app/5000-binding limits.
- [X] T005 [US4] Update only current-state/next-state fixtures in `tests/DesktopMotionStudio.Core.Checks/{Program,F07Checks}.cs` to version constants; preserve Scene schema tests unchanged and extend F02 compatibility docs.

## Phase 3: US1 - Registration

- [X] T006 [US1] Add registry command and duplicate identity checks in `tests/DesktopMotionStudio.Core.Checks/F08Checks.cs`, including path/argument/AUMID equality, shortcut identity, unchanged backups and invalid targets.
- [X] T007 [US1] Implement transactional Add/Rename/Repair/Remove and explicit F07 target provider in `src/DesktopMotionStudio.Core/Applications/ApplicationRegistry.cs` with stable IDs and latest-state patches.
- [X] T008 [US1] Harden `src/DesktopMotionStudio.Core/Launch/{ShellLinkTargetReader,LaunchTargetIdentityResolver}.cs` against shortcut search/tracking and network paths before filesystem probes; extend F04 checks/history.

## Phase 4: US2 - Discovery

- [X] T009 [US2] Define/test strict discovery output and timeout/cancellation/limit outcomes in `src/DesktopMotionStudio.Core/Applications/ApplicationDiscovery.cs` and `tests/DesktopMotionStudio.Core.Checks/F08Checks.cs`.
- [X] T010 [US2] Implement bounded STA Start Menu/AppsFolder worker and parent process client in `src/DesktopMotionStudio.Studio/{DiscoveryWorker,DiscoveryClient,Program}.cs`, with 5s/1MiB/2000-entry bounds and only owned-worker termination.

## Phase 5: US1/US3 - Studio management UI

- [X] T011 [US1] Implement accessible saved-only home and add dialog in `src/DesktopMotionStudio.Studio/{MainWindow,ApplicationEditorWindow}.*`, including empty/loading/error states, manual file picker, discovery selection, cancellation and no accidental launch.
- [X] T012 [US3] Implement details/rename/repair, confirmed atomic removal and F07 binding toggle in `src/DesktopMotionStudio.Studio/{MainWindow,ApplicationEditorWindow}.*`; retain IDs and display actual storage/target/binding results.
- [X] T013 [US2] Add saved/discovery search and stale-result cancellation in `src/DesktopMotionStudio.Studio/{MainWindow,ApplicationEditorWindow}.*`; supply icon fallback and nonblocking refresh.

## Phase 6: Acceptance and convergence

- [X] T014 Run Release/format/static checks and complete F00–F08 regressions with real F06/F07 integration; record results in `specs/009-application-management/evidence.md`.
- [X] T015 Verify actual Windows keyboard add/restart/details/rename/repair/cancel/remove and unreadable-state recovery using isolated state and a test-owned executable; record no accidental process launch and PID/HWND in `evidence.md`.
- [X] T016 Verify readable/operable UI at 100%, 125%, 150% actual desktop scaling per SC-004; record each environment/result explicitly and leave missing cases open.
- [X] T017 Independently review, Analyze and Converge spec/plan/tasks/code; remediate findings, update `review.md`/`FEATURES.md` and commit the verified milestone.

## Dependencies

T001 gates T002 and downstream UI. T003→T004→T005 precedes registry; T006→T007; T008 precedes discovery/registration acceptance. T009→T010; T011 follows registry/worker; T012/T013 follow home. T014–T017 require all implementation. Migration and UI/discovery research can proceed independently; shared State/Program files are edited sequentially.

## Incremental strategy

First deliver typed persistence and registry contracts with all prior regressions preserved. Then isolated discovery and manual-add Studio slice, then remaining management actions. Actual desktop/scaling evidence is a completion gate; no dependent Feature starts while it remains open.

## Phase 7: Convergence

- [X] T018 Let discovery candidate/directory-limit errors reach the worker Failed result rather than being swallowed as I/O errors; add exact/over-limit worker construction checks in `tests/DesktopMotionStudio.Studio.Checks/Program.cs` per FR-005 and discovery contract (partial, P2).
- [X] T019 Reject reparse components along local path ancestry before target/shortcut filesystem probes in `src/DesktopMotionStudio.Core/Launch/LaunchTargetIdentityResolver.cs`; add isolated link-path regression in `tests/DesktopMotionStudio.Core.Checks/F08Checks.cs` and document the local-only capability limit per FR-011 (partial, P2).

## Phase 8: Convergence

- [X] T020 Prevent title-bar/Alt+F4 close while a registry save is pending in `src/DesktopMotionStudio.Studio/ApplicationEditorWindow.xaml.cs`, then allow successful save to close normally; add delayed-store window regression in `tests/DesktopMotionStudio.Studio.Checks` per FR-010/FR-012 so a background commit cannot leave the home stale (partial, P2).
- [X] T021 Ignore a stale failed refresh in `src/DesktopMotionStudio.Studio/MainWindow.xaml.cs` just as a stale successful refresh is ignored; add a gated-store race check proving an older failure cannot disable a newer readable view per FR-010/FR-012 and T013 (partial, P2).
- [X] T022 Retain the recovery error when a command succeeds but its subsequent reload fails; add command-then-unreadable store coverage in `tests/DesktopMotionStudio.Studio.Checks` for FR-010/FR-012 instead of replacing the recovery status with an unconditional success message (partial, P2).

## Phase 9: Convergence

- [X] T023 Supply human-readable accessible list-item names in `src/DesktopMotionStudio.Studio/{MainWindow,ApplicationEditorWindow}.xaml` instead of record diagnostic strings; verify saved/discovered item names through actual UI Automation per FR-012 (partial, P2).

## Phase 10: Convergence

- [X] T024 Handle the production discovery-worker mode explicitly in the Studio test apphost and reject unknown arguments so WPF race fixtures cannot recursively spawn whole check suites; run both apphost and dotnet-DLL modes and verify owned worker cleanup per testing strategy and T020 (partial, P2).
