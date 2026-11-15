# Tasks: Cross-application binding

**Input**: spec.md, plan.md, research.md, data-model.md, contracts/binding-service.md

## Phase 1: Setup and foundations

- [x] T001 Add repository transaction checks in `tests/DesktopMotionStudio.Core.Checks/F07Checks.cs`: same-reference no-op, callback-once, callback rejection/exception, unknown/corrupt/oversized state, I/O failure, and preserved backup.
- [x] T002 Add `IStudioStateStore` and additive `Busy` error in `src/DesktopMotionStudio.Core/State/StudioState.cs`; implement Update and a shared Save/Update path mutex with two-second acquisition bound in `StudioStateRepository.cs`, retaining schema 1, 4 MiB/1000 app/5000 binding limits.

## Phase 2: US1 - Assign and replace

- [x] T003 [US1] Add assign/two-app/replace/no-op tests in `tests/DesktopMotionStudio.Core.Checks/F07Checks.cs`, including 128-character app-ID bounds, stable BindingId, explicit typed EXE/shortcut/AUMID provider, and preserved profiles/settings/assets.
- [x] T004 [US1] Add result/context contracts in `src/DesktopMotionStudio.Core/Bindings/BindingContracts.cs` and assign/replace in `BindingService.cs` using only Update, fresh supplied profiles, one available SceneId, array copies, and no target-type guessing.

## Phase 3: US2 - Enable, disable, and remove

- [x] T005 [US2] Add toggle/remove/idempotence tests in `tests/DesktopMotionStudio.Core.Checks/F07Checks.cs`, including unavailable providers and exact file/backup byte preservation.
- [x] T006 [US2] Implement SetEnabled/Remove in `src/DesktopMotionStudio.Core/Bindings/BindingService.cs`; preserve IDs/scene, validate on enable, and keep disable/remove independent of external availability.

## Phase 4: US3 - Resolve and diagnose

- [x] T007 [US3] Add precedence/read-only/failure tests in `tests/DesktopMotionStudio.Core.Checks/F07Checks.cs`: all eight resolution statuses, root/asset/duplicate catalog failures, provider exception/null, storage errors, and no context for non-Ready.
- [x] T008 [US3] Implement Resolve in `src/DesktopMotionStudio.Core/Bindings/BindingService.cs`, mapping stable errors and rereading current state/catalog/typed target without launching or persisting.

## Phase 5: Integration and convergence

- [x] T009 Add two-process binding Update and bounded Busy worker checks in `tests/DesktopMotionStudio.Core.Checks/F07Checks.cs` and worker dispatch in `Program.cs`; verify two successful different-AppId edits both survive and same-AppId edits remain one binding.
- [x] T010 Add optional real Windows two-target integration in `tests/DesktopMotionStudio.Core.Checks/F07Checks.cs`: same scene, explicit typed targets, F06/F05 launch, distinct target PID/HWND, and owned fixture cleanup.
- [x] T011 Update additive repository contract history in `specs/003-local-profile-repository/spec.md` and feature progress in `FEATURES.md`; document F07 evidence and review in `specs/008-cross-app-binding/{evidence,review}.md`.
- [x] T012 Run Release builds, format/analyzers, full F00–F07 checks and live two-target integration; perform independent review, Analyze and Converge; record evidence and create a verified Git milestone.

## Dependencies and execution

T001 → T002; T003 → T004 after T002; T005 → T006 after T004; T007 → T008 after T004; T009/T010 follow service completion; T011/T012 close the feature. US2 and US3 can be tested independently from a seeded valid binding, but their shared service file is edited sequentially. Test fixture setup is the only useful parallel work; no shared-file parallel edits are planned.

## Implementation strategy

Prove transaction safety, then assign/replace as the first usable slice. Add toggles/removal and resolution, then real worker/process evidence. No UI/shortcut/application-schema work enters this increment.

## Phase 6: Convergence

- [x] T013 Correct `src/DesktopMotionStudio.Core/State/StudioStateRepository.cs` to distinguish a genuinely missing file from unreadable/non-file configuration; add directory-path Load/Resolve/Remove regression checks in `tests/DesktopMotionStudio.Core.Checks/F07Checks.cs` per FR-008 and US3/AC2 (partial, P2).
- [x] T014 Make every owned target cleanup and temporary-directory cleanup independent in `tests/DesktopMotionStudio.Core.Checks/F07Checks.cs`; record cleanup failures without skipping later targets or masking the original test result per SC-004 and Constitution ownership (partial, P2).
