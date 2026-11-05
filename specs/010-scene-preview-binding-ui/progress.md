# F09 Implementation Checkpoint

**Updated:** 2026-11-05 · **Branch:** `codex/f09-scene-preview-binding`

F08 converged at `90b70c9`; F09 risk/spec milestone `1837210`, reader/content/timeline milestone `d209862`. Requirements checklist16/16 passes. Task count27;16 complete,11 pending. No owner clarification is pending.

Completed: T001–T010, T022–T027. IPC2 now carries explicit Launch/Preview and a closed, bounded, validated immutable playback snapshot, retains lifecycle-only Launch diagnostics and single-dispatch fail-open, and restricts natural Completed to Preview. Real old-peer and future-playback host tests preserve precise compatibility errors. Independent protocol/failure-path review has no remaining P1/P2.

Verification:277 Core PASS/0 FAIL/1 explicit optional packaged-instance SKIP, including104 F09 checks, actual276ms lifecycle handoff and F07 targets14988/0xC0230 and22192/0x1403E2 both HandedOff. Core/host/check formatting and all relevant Release builds pass. Prior foundation evidence includes actual F08 UI16 checks at DPI120, content build/publish validation and reader/path/timeline regressions. See evidence.md for exact scope and local logs.

Owned creation-time job, real resume/failure cleanup, independent parent deadline and bounded Stop are now implemented and independently reviewed. Final verification:293 Core PASS/0 FAIL/1 optional packaged SKIP,16 ownership checks and13 Studio checks. Actual F06 handoff291ms. See evidence for process IDs/timings. Next: T011 shared production surface and T012 preview coordination. The temporary host capability guard intentionally rejects playback/Preview until T011 integrates actual drawing. No formal F09 library/preview/binding UI is implemented. Do not claim the POC or lifecycle regression as production scene drawing.

F09 remains incomplete. No F10 work until F09 passes actual scene rendering/handoff, failures/concurrency, system reduced-motion/scaling, independent review and final Converge. Use `C:\Users\AAA\.dotnet\dotnet.exe` and `DOTNET_ROOT=C:\Users\AAA\.dotnet`. Desktop acceptance uses isolated state/catalog/target fixtures.
