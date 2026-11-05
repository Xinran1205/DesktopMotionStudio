# F09 Review and convergence

**Date:** 2026-11-05 · **Result:** Complete · **Spec:** 1.0.0

The feature implements all three journeys: browse complete works, read-only target-free preview, and saved-app bind/replace/home inspection. Thirty-one tasks have implementation/evidence. Independent read-only reviewers inspected reader/resources, ownership/protocol, renderer, coordinator and UI boundaries; all definite P1/P2 findings were reproduced or explicitly assessed, fixed and reviewed again.

| Specification keys | Implementation and acceptance |
|---|---|
| FR-001/009 | Library/home name/cover/fallback/details/status, keyboard navigation and intended selection; F09 external UI. No fixed shortcut-health claim. |
| FR-002/010 | Strict local safe catalog/resources/JSON/version/size/count limits; Core reader and eligibility regressions;11 cover cases including signed bad PNG/JPEG. |
| FR-003/006 | Shared production timeline/paint,10 deterministic pixel/window checks, real launch handoff; actual OS animation On/Off and actual DPI96/120/144. |
| FR-004/005 | Target-free Preview contracts, jobs/deadlines, actor and shared UI intent gate;18 controlled coordinator cases,3 preflight cases, queued close and actual crash/hang/parent-exit tests. |
| FR-007/008 | Default-compatible F07 eligibility, current transaction-time playback, stable IDs/no-op/preservation;13 Core eligibility cases,3 picker/parent failure cases and external two-app binding/replacement. |
| FR-011/012 | State2/manifest1 retained, explicit IPC2 and independent sidecar/runtime versions; risk POCs preceded implementation, old/unknown formats remain protected, previous real F00–F08 regressions pass. |
| SC-001/002/003 | Two works, no-app preview, state/backup and fixture-target isolation, shared actual renderer, stable replacement and current safe failures have evidence. |
| SC-004 | Actual100/125/150% keyboard workflows and visual library inspection; actual animation Off/On runs; original125%/On restored and verified by final published Studio. |
| SC-005 |321 Core,53 Studio,10 drawing checks; F08 UI16 and final published F09 UI16; Release/analyzers/format; independent review and SDD assessment. One optional packaged-instance integration is explicitly skipped. |

Analyze checked17 FR/SC keys against31 tasks:100% coverage, no unmapped tasks, unresolved ambiguities, duplicate requirements or constitution conflicts. Converge checked12 FR,5 SC,11 acceptance scenarios,9 plan decisions and10 constitution principles: zero missing/partial/contradicting/unrequested gaps; tasks remain unchanged by the clean convergence assessment. Final task bookkeeping and this review are the implementation workflow's quality closure.

Important fixes include ignored JSON computed members, visible-base Unicode glyphs, root overflow, compatibility errors lost during handshake, startup/ordinary Stop deadlines, native process/thread identity, signed-image decoder errors, UI preflight ordering and preservation of post-commit errors. The250ms fade produced one306ms total handoff;200ms now leaves headroom inside the unchanged300ms assertion. See evidence.md for exact red/green logs, scope and actual PIDs/HWNDs.

The initial PRD/TDD remain historical baselines. No arbitrary content/import/effect editor, test launch, managed shortcut, installer or licensed release-art scope was added. Preview and browsing remain read-only. Windows evidence is scoped to this Windows11x64 desktop, its active display and installed runtime.
