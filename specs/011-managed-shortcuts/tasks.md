# Tasks — F10 Managed Shortcuts

**Input** [spec.md](spec.md), [plan.md](plan.md), [data-model.md](data-model.md), [research.md](research.md), [contracts](contracts/managed-shortcuts.md), [quickstart.md](quickstart.md)

## Phase 1 — Safety proof

- [x] T001 Prove same-volume no-replace publication, file identity, edit/replace resistance, hard links, reparse rejection, and retained-handle delete in an isolated NTFS directory. Record commands and results in specs/011-managed-shortcuts/evidence.md and preserve a runnable proof in specs/011-managed-shortcuts/probes/. (AC-04, AC-07, SC-001)

## Phase 2 — Standalone launch

- [x] T002 [US1] Add tests for canonical Base64Url strict UTF-8 parsing with 512 ASCII character and 128 UTF-16 code unit bounds, migrated IDs, invalid or future profile, missing binding and target, disabled/missing playback, audio mute independence, selection from one snapshot and recursion in tests/DesktopMotionStudio.Core.Checks/F10LaunchChecks.cs. (AC-02, AC-03)
- [x] T003 [US1] Implement strict binding request and resolution in src/DesktopMotionStudio.Core/Shortcuts/ManagedLaunchRequest.cs. Keep profile2 unchanged and use F04 target resolution and F09 playable definition. (FR-002, FR-003)
- [x] T004 [US1] Wire independent native/animated single dispatch, bounded failure messages and exit status in src/DesktopMotionStudio.Launcher/Program.cs. Include verified recursion protection in launch resolution. (FR-003)
- [x] T005 [US1] Stage Launcher payload with correct paths in src/DesktopMotionStudio.Studio/DesktopMotionStudio.Studio.csproj. Publish a fresh win-x64 output and verify runtime layout. (FR-008)

## Phase 3 — Owned shortcut lifecycle

- [x] T006 [US3] Add receipt and fault tests for 16 KiB limit, schema1, exact members, canonical matching ID, current/pending proof, malformed/future storage, 2-second lock and crash recovery in tests/DesktopMotionStudio.Core.Checks/F10ReceiptChecks.cs. (AC-08, AC-09)
- [x] T007 [US3] Implement bounded versioned receipt and transaction lock in src/DesktopMotionStudio.Core/Shortcuts/ShortcutReceiptStore.cs. Preserve raw unknown or invalid bytes. (FR-006)
- [ ] T008 [US2] Add Windows integration checks for Shell Link fields, original-byte preservation, hard links, edit, copy, move, missing/redirected root, publish race and file sharing in tests/DesktopMotionStudio.Core.Checks/F10ShortcutChecks.cs. (AC-04, AC-05, AC-06, AC-07)
- [ ] T009 [US2] Implement read-sharing-only ancestry pins, a verified overlapping publish pin while holding the stage child, staged COM Shell Link, bounded proof (1–65536 bytes, one hard link, SHA256, identity/creation time), no-replace publish and retained-handle removal in src/DesktopMotionStudio.Core/Shortcuts/WindowsShortcutPlatform.cs. (FR-002, FR-004, FR-007)
- [ ] T010 [US1] Implement state validation, status, create, repair and remove with durable pending proof and post-commit verification in src/DesktopMotionStudio.Core/Shortcuts/ManagedShortcutService.cs. (FR-001, FR-004, FR-006, AC-01, AC-08, SC-003)

## Phase 4 — Studio and real Windows acceptance

- [ ] T011 [US2] Test selected-binding action boundaries, stale results, error display, keyboard operations and no profile mutation in tests/DesktopMotionStudio.Studio.Checks/F10StudioChecks.cs. (AC-04, AC-05, AC-06, AC-07, SC-004)
- [ ] T012 [US2] Connect shortcut status and Create/Repair/Remove UI in src/DesktopMotionStudio.Studio/MainWindow.xaml and MainWindow.xaml.cs. Publish only on explicit action. Use isolated fixture injection for tests. (FR-001, FR-005)
- [ ] T013 [US1] Run actual published .lnk activation with Studio exited, current binding replacement, animated handoff and disabled/failure native fallback in tests/DesktopMotionStudio.Studio.Checks/F10UiChecks.cs. Record real process, target and cleanup evidence in specs/011-managed-shortcuts/evidence.md. (AC-01–AC-03, SC-002)
- [ ] T014 [US2] Complete isolated actual Windows keyboard create/repair/remove/conflict checks and full affected build, analyzers, format and regression in specs/011-managed-shortcuts/evidence.md. (SC-001, SC-003, SC-004)
- [ ] T015 Record independent review, fixes and convergence against all FR/AC/SC in specs/011-managed-shortcuts/review.md. Update FEATURES.md and README.md after completion.

## Dependencies

T001 gates all production filesystem work. T002 → T003 → T004 → T005. T006 → T007, T008 → T009, both feed T010. T010 → T011 → T012. T005 and T012 gate T013–T014. T015 closes the feature after evidence and fixes. All automated checks run under isolated profile/desktop paths.
