# Tasks — F12 Installer, Upgrade and Uninstall

**Input** [spec](spec.md), [plan](plan.md), [research](research.md), [data model](data-model.md), [contract](contracts/installation.md), [quickstart](quickstart.md)

## Phase 0 — Platform proof

- [x] T001 Evaluate native per-user MSI with an embedded cabinet and a post-file-execution rollback failure. Record the failed registration recovery and reject that distribution route. (FR-003, AC-05)
- [x] T002 Prove app-local runtime selection for Studio, Launcher and MotionHost with isolated self-contained publishes and host traces. (FR-001, AC-02)

## Phase 1 — Program package

- [x] T003 [US1] Implement clean three-component publishing, inventory, versions, hashes and dependency provenance under installer/. Test shape and absence of stale/test payloads. (FR-001, FR-002, FR-006, AC-01, AC-02)
- [x] T004 Prove atomic generation selection and recovery around real abrupt process exits. Verify complete old/new fixture payloads, committed versus uncommitted state, reparse and same-name conflicts. Production Launcher forwarding is T005. (FR-002, FR-003, AC-05)
- [x] T005 [US1] Author the self-contained setup executable with fixed root, stable shims, Start menu entry and Windows uninstall registration. Verify platform, scope, ownership, process preflight and payload. (FR-001, FR-002, FR-003, AC-01, AC-03, AC-06)
  - [x] T005a Implement and verify stable entries, guarded archive extraction, inactive program-root publication and explicit activation. Reuse the F10 no-replace Shell Link stage for the Start menu.
  - [x] T005b Embed the payload and entry image in Setup. Add current-user registration, preflight, durable registration intent and recoverable activation. Run the coordinated workflow in a fixture.
- [x] T006 [US2] Wire versioned upgrade, rollback policy, retry and duplicate/downgrade behavior. Test exact prior bytes, user profile and Launcher path in an isolated product-family pair. (FR-002, FR-003, AC-04–AC-06, SC-002)

## Phase 2 — Uninstall

- [x] T007 [US3] Add receipt-key discovery and bounded ownership verification in src/DesktopMotionStudio.Core/Shortcuts/. Test corrupt, future, moved, replaced, missing, busy and orphan records. (FR-004, FR-005, AC-07–AC-09)
- [x] T008 [US3] Add ordinary-uninstall commit cleanup and bounded result reporting. Test rollback/upgrade skip, interruption and re-entry. (FR-004, FR-005, FR-007, AC-07–AC-09)

## Phase 3 — Windows delivery

- [x] T013 Reproduce the stable entry rejecting dispatch while an unrelated setup transaction holds its gate. Separate dispatch from staging while serializing activation and durable removal. Verify current-version forwarding and refusal after removal intent. (FR-003, FR-008, SC-004)
- [x] T014 Verify three consecutive upgrades and prune fully owned versions older than the rollback generation. Verify current/previous generations, file conflicts, unrelated directories and incomplete-cleanup retry. (FR-003, FR-004, FR-009, SC-005)
- [x] T015 Verify partial extraction and failed publication cleanup against exact staged identities. Preserve injected foreign files and durable upgrade retry. (FR-004, FR-010, SC-006)

- [ ] T009 [US1] Run clean standard-account offline Windows install and real Shell launch, fallback and process-lock tests. (SC-001)
- [ ] T010 [US2] Run two-version Windows upgrade, rollback, retry and stale installer checks. (SC-002)
- [ ] T011 [US3] Run Installed Apps uninstall, conflict preservation and reinstall tests. (SC-003)
- [ ] T012 Publish verification evidence, complete independent review and Converge, document install/recovery/retention and update FEATURES.md and README.md. (FR-006, FR-007)

T001 and T002 inform T003 and T004. Both T003 and T004 gate T005 and T006. T007 and T005 precede T008. T009–T011 require a candidate package and isolated Windows acceptance environment. T012 requires all acceptance evidence.

The clean-account gate is pending. Workstation proof now includes native Setup, Start menu launch, Busy, two-version rollback and retry, downgrade refusal, registered removal and reinstall. The host uses a filtered administrator token and has a private .NET SDK. See [evidence](evidence.md) and the [clean-account runbook](quickstart.md#clean-account-runbook).
