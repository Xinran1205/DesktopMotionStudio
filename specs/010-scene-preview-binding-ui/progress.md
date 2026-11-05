# F09 Implementation Checkpoint

**Updated:** 2026-11-05 · **Branch:** `codex/f09-scene-preview-binding`

F08 converged at `90b70c9`; F09 plan/risk-spike milestone is `1837210`. Requirements checklist16/16 passes. The task list now has26 tasks;9 complete,17 pending. No owner clarification is pending.

Completed: T001/T002 isolated drawing/creation-time-job risk proofs; T003-T007 strict metadata/library/resource policy, original development data copied to build/publish, and pure shared timeline; T024 directory257 fail-closed; T025 visible-base Unicode glyph validation. Closed-schema sidecar correction is implemented, while T026 stays open for its T008 IPC injection regression.

Verification:78 targeted F09 checks;248-pass full Core regression at the same production-source checkpoint (then77 F09 checks), including actual289ms MotionHost handoff and two-target F07 integration;13 Studio worker/race checks;16 actual F08 UI checks at DPI120. A later test-only non-midpoint easing assertion passed, explaining78 targeted versus77 in that full run. Core/check format verification and Release builds pass; actual Studio build and framework-dependent publish Scenes directories validate. Independent review found no remaining P1/P2 in the implemented resource/readers/timeline boundary. See evidence.md for scope, logs and actual IDs.

Next: T008 IPC v2 contract/transition tests, then T009/T010/T022/T023 process protocol, native job/resume and independent parent deadline. Preserve fail-open launch and target-free preview. Formal shared paint/session/UI are not implemented yet. Do not copy the POC's deliberately suspended child lifecycle into production without resume/failure cleanup. T026's ignored computed-member attack must be tested in the nested IPC snapshot before closing it.

F09 remains incomplete. No F10 work until F09 passes actual scene rendering/handoff, failures/concurrency, system reduced-motion/scaling, independent review and final Converge. Use `C:\Users\AAA\.dotnet\dotnet.exe` and `DOTNET_ROOT=C:\Users\AAA\.dotnet`. Desktop acceptance uses isolated state/catalog/target fixtures.
