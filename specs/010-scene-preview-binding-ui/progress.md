# F09 Implementation Checkpoint

**Updated:** 2026-11-05 · **Branch:** `codex/f09-scene-preview-binding`

F08 converged at `90b70c9`. F09 milestones: risk/spec `1837210`, readers/timeline `d209862`, IPC2 `ed460cf`, owned jobs/deadlines `12641f3`, production shared drawing `b9d6436`. Requirements checklist16/16 passes. Task count28;19 complete,9 pending. No owner clarification is pending.

Completed: T001–T012 and T022–T028. Implemented strict scene data/resource policy, original development content, pure timeline, IPC2, creation-time ownership/real resume/failure cleanup, independent runtime/Stop deadlines, shared production drawing and target-free single-session coordination. Replacement waits for actual old process exit; stale completion/cancel cannot alter latest state. Cleanup failure blocks another host. These implementation boundaries have independent reviews with no remaining P1/P2.

Latest verification:306 Core PASS/0 FAIL/1 optional packaged-instance SKIP;31 Studio worker/discovery/preview/WPF checks;10 production drawing/window checks. This includes18 controlled preview cases and5 actual coordinator checks across3 successive visible hosts. Selected scene Completed3279ms, Stop31ms, Handoff287ms; actual target scene handoff passes before sequence end. Formatting, Release builds and analyzers pass. See evidence.md for exact PIDs/HWNDs/logs and red/green regressions.

Resume at T013: WPF library/cover navigation and playable action states, followed by T014 current-playability F07 adapter, T015 picker, T016 home and T017 Studio preview/close wiring. T018–T021 require external UI, repeated/skip/crash/hang/close, actual100/125/150% and both OS motion preferences, final independent review/Analyze/Converge. Those gates remain open; injected motion pixel checks do not substitute for actual OS acceptance. No F10 until F09 converges.

Use `C:\Users\AAA\.dotnet\dotnet.exe` and `DOTNET_ROOT=C:\Users\AAA\.dotnet`. Desktop acceptance uses isolated state/catalog/target fixtures. No user profile is a test fixture.
