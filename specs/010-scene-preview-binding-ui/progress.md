# F09 Implementation Checkpoint

**Updated:** 2026-11-05 · **Branch:** `codex/f09-scene-preview-binding`

F08 converged at `90b70c9`. F09 milestones: risk/spec `1837210`, readers/timeline `d209862`, IPC2 `ed460cf`, owned jobs/deadlines `12641f3`. Requirements checklist16/16 passes. Task count28;18 complete,10 pending. No owner clarification is pending.

Completed: T001–T011 and T022–T028. Strict read-only scene data, bounded resources, original development work, shared timeline, closed IPC2 snapshot, creation-time job ownership/real resume/failure cleanup, independent runtime/Stop deadlines and production SceneSurface are implemented. Preview has an explicit simulated handoff and natural Completed; Launch keeps real target-ready interruption. Legacy lifecycle-only diagnostics remain supported. Current protocols/surface/ownership have independent reviews with no remaining P1/P2.

Latest verification:301 Core PASS/0 FAIL/1 optional packaged-instance SKIP;10 production drawing/window checks; prior same-boundary Studio discovery/races13 PASS. Actual selected scene Preview Completed3279ms, Stop27ms, Handoff289ms; true scene+target handoff passed before full sequence completion. Formatting/Release/analyzers pass. See evidence.md for logs, HWNDs, PIDs and historical regressions.

Next: T012 target-free serial preview coordinator and controlled races; T013–T017 library/cover/binding/home/preview wiring. F09 actual Studio UI, repeated/skip/cleanup integration, actual100/125/150% and both OS motion branches, independent final review/Analyze/Converge remain T018–T021. Injected pixel/policy checks do not substitute for those actual desktop gates. No F10 work until F09 converges.

Use `C:\Users\AAA\.dotnet\dotnet.exe` and `DOTNET_ROOT=C:\Users\AAA\.dotnet`. Desktop acceptance uses isolated state/catalog/target fixtures. No user profile is a test fixture.
