# Tasks — F11 Settings and Diagnostics

**Input** [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts](contracts/settings-diagnostics.md), [quickstart.md](quickstart.md)

## Phase 1 — Preferences

- [x] T001 [US1] Add checks for defaults, exact no-op, invalid values, stale snapshots, profile/backup preservation and old/future schema in tests/DesktopMotionStudio.Core.Checks/F11SettingsChecks.cs. (FR-002, FR-003, AC-02, AC-03)
- [x] T002 [US1] Implement transactional preference read and compare-and-save in src/DesktopMotionStudio.Core/State/StudioSettingsService.cs. Use existing profile2 and preserve unrelated state. (FR-002, FR-003, AC-02, AC-03)
- [x] T003 [US1] Prove Fluent Light/Dark/System dictionary loads and contrast palette switching in an isolated Windows harness under specs/012-settings-diagnostics/probes/. Actual OS transitions remain T010. (FR-001, AC-01)
- [ ] T004 [US1] Apply palette and preference UI in src/DesktopMotionStudio.Studio/Styles.xaml, theme controller and SettingsWindow. Add controlled theme/selection cases in tests/DesktopMotionStudio.Studio.Checks/F11SettingsChecks.cs. (FR-001, FR-002, AC-01, SC-001)

## Phase 2 — Bounded local diagnostics

- [ ] T005 [US2] Test strict typed events, 128 records, 32 KiB per generation, 64 KiB committed, concurrent and interrupted handover, invalid/unknown storage and secret injection in tests/DesktopMotionStudio.Core.Checks/F11DiagnosticChecks.cs. (FR-004, FR-005, AC-05, AC-06, SC-002)
- [ ] T006 [US2] Implement typed codec, recovery and rolling private store in src/DesktopMotionStudio.Core/Diagnostics/. Share no-reparse native IO with F10. (FR-004, FR-005, AC-05, AC-06)
- [ ] T007 [US2] Wire terminal phase, result, bounded timing and fallback reporting into src/DesktopMotionStudio.Launcher/Program.cs. Demonstrate failed diagnostic writes cannot redispatch or block renderer cleanup. (FR-004, FR-005, AC-04, AC-06, SC-003)

## Phase 3 — Diagnostics UI and acceptance

- [ ] T008 [US2] Implement diagnostic view, version and capability display, explicit report copy and directory open in src/DesktopMotionStudio.Studio/DiagnosticsWindow.xaml and code-behind. (FR-006, AC-04, AC-05, AC-08)
- [ ] T009 [US3] Wire selected application repair using F10 service and checked eligibility in src/DesktopMotionStudio.Studio/DiagnosticsWindow.xaml.cs. Verify conflict, unavailable and stale-selection paths. (FR-007, AC-07, AC-08)
- [ ] T010 Record published actual Windows keyboard/theme and launcher/diagnostic failure acceptance and regression in specs/012-settings-diagnostics/evidence.md. (SC-001, SC-002, SC-003)
- [ ] T011 Run independent review and convergence and record results in specs/012-settings-diagnostics/review.md. Update FEATURES.md and README.md.

## Dependencies

T001 → T002 → T003 → T004. T005 → T006 → T007. T004 and T007 gate the diagnostics UI. T008 → T009 → T010 → T011. Every automation uses isolated configuration and fixture paths.
