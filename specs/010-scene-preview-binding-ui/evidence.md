# F09 Risk-Gate Evidence

**Date:** 2026-11-05 · **Status:** In progress; risk gates, readers/content and pure timeline verified. Production F09 remains incomplete.
**Environment:** Windows 11 x64 build26200, current display2560×1440 /125% /DPI120; .NET SDK10.0.403 /Desktop runtime10.0.14.

## Isolated shared playback POC — T001

`poc/SharedPlaybackPoc.csproj` is an isolated WinForms program, not a production Studio feature. Preview and Launch-simulation call the same elapsed-time evaluator and paint method. Only a fixed original neutral test timeline is compiled; it accepts no target path/app/profile/dispatcher. Simulation is explicitly labelled and is not actual F06 target handoff.

Release build passed with **0 warnings /0 errors**. **7 automated checks passed**: matching pixels at six fixed times for both ordinary/reduced paths; changing ordinary timeline; still reduced geometry; clamped endpoints; elapsed-time completion; rejected target/unknown mode arguments. Fingerprints compare actual rendered bitmaps; different phases are separately required to differ.

`dotnet format --verify-no-changes --no-restore` passed for the isolated POC. The final repeated Release check run again passed all7 drawing and6 ownership checks. Full F00–F08 regression is the verified F08 baseline; no production source was changed for these isolated experiments.

Real desktop screenshots were inspected through computer-use. In Launch-simulation two observations about1.5s apart visibly changed ring size/opacity/marker rotation. These are actual drawing observations, not status-card evidence.

| Run | PID | HWND | DPI | Result |
|---|---:|---|---:|---|
| Preview | 19900 | 0x14085E | 120 | Visible authored frame; Completed,274 paints,8018ms |
| Launch-simulation | 21652 | 0x30786 | 120 | Two visibly different frames; Completed,286 paints,8014ms |
| Injected reduced preview | 29316 | 0xF00C4 | 120 | Visible stable geometry; Completed,281 paints,8018ms |
| Fresh Preview + actual Esc | 25724 | 0x120260 | 120 | Escape exit after485ms,19 paints |

The reduced flag is an injected branch test; actual Windows motion-preference toggling remains T020. An initial hidden-start attempt produced0 paints despite Shown firing and is excluded from visual acceptance. The log label was changed to SHOWN_EVENT to avoid treating that event as visibility proof. Timed windows can close before a subsequent capture; exit logs and fresh-window Esc testing distinguish natural completion from skip.

An isolated guard JSON and backup retained exact SHA256 values before/after the reduced run (`0DAB7E3E…58DAF2`, `477E2A54…C09A60`). Owned WindowHost target count stayed0. No user profile path was supplied or read. POC stdout/error logs are machine-local under `%TEMP%/dms-f09-*-poc*.log`.

## Owned-job / early-parent-exit POC — T002

**6 ownership checks passed.** Native job limits use KILL_ON_JOB_CLOSE with a non-inherited unnamed handle. Scope tests verify only the assigned fixture belongs to that job; the current parent and an unassigned process do not. Closing the job kills the assigned fixture and leaves the other alive. Failed post-start assignment cleans only its owned fixture; invalid creation-time association returns a native failure.

The first test intentionally uses post-start assignment as a reference and kills the parent after READY: parent15308 /hung child5012, cleanup4ms. Independent review correctly identified that this does not cover pre-assignment death.

The strengthened `AtomicJobPoc.cs` uses STARTUPINFOEX plus PROC_THREAD_ATTRIBUTE_JOB_LIST with CREATE_SUSPENDED and inheritHandles=false. No subsequent Assign or ResumeThread occurs in this proof. The parent reports child identity from its native creation time; the checker verifies that identity, then kills the parent **before the worker's initial thread resumes**. Parent16784 /suspended child5964 exited with cleanup4ms. The owning job remains the only job handle and closes on parent death; native process/thread/attribute buffers are released. This is a Windows creation-time ownership experiment, not a production adapter.

Independent follow-up source review checked the Win64 layouts (STARTUPINFO104B, STARTUPINFOEX112B, PROCESS_INFORMATION24B), job-list attribute, SafeHandle lifetime, non-inheritance, initial-thread behavior and cleanup. No remaining definite P1/P2 was found in the isolated ownership proof. Production must add a real resume lifecycle and failed-resume cleanup; T023 explicitly requires this rather than copying the suspended experiment unchanged.

## Planning review corrections

Three P2 planning gaps were made explicit before formal integration:

- T022: A live parent's independent non-UI deadline enforces the hard bound even if host UI hangs and the user never selects Stop. Stopwatch/UI Timer alone is insufficient.
- T023: Creation-time job association closes the create-to-Assign orphan window; early-parent-death proof added to T002.
- T024: Entry257 fails the whole library as TooManyScenes before sidecar reads/sorting; no truncated partial library can hide a duplicate ID.

Spec/Plan/Tasks analysis covers12 FRs +5 SCs,24 tasks, zero uncovered keys or unresolved product clarifications. Requirements checklist16/16 passes. These coverage counts are planning completeness, not feature completion.

## Reader / content / timeline implementation

T003–T007 and T024/T025 are implemented. Core uses a shared local ancestry policy, bounded resource reads, strict independently versioned presentation/playback sidecars, immutable definitions/library items and a pure timeline. The original F03 manifest and F08 profile schemas remain1 and2. Entry257 rejects the entire catalog before manifest/sidecar reads or sorting, and a read-observer regression confirms no sidecar reads occur on overflow.

The first test build identified the missing reader/timeline contracts before implementation. Once present, independent review and stronger black-box cases reproduced five actual failures: ignored computed JSON property accepted, two mark-only glyphs accepted, a leading-space device reference accepted, and a constructed invalid library item claiming playability. All five failed before correction and passed afterward. Explicit field sets, visible-base glyph checks and shared namespace/validity rules close these gaps. T026's IPC injection case remains pending with T008.

Verification at this stage:

- **78 F09 checks passed**, covering sidecar versions/closed shapes/required members/identity/runtime, finite numeric limits,2–8 keyframes,64KiB input boundary, optional cover fallback/oversize, profile/backup non-writing, missing/duplicate content, exact256/257 directory bounds, real junction ancestry/resource rejection, locked-file recovery and pure timeline behavior. Eight timeline checks include non-midpoint smoothstep, exact endpoints, reduced still geometry, elapsed-time completion and concurrent determinism.
- Full F00–F09 Core regression at the production-source checkpoint: **248 PASS /0 FAIL /1 explicit optional packaged-instance SKIP** (then77 F09 checks). A subsequent non-midpoint test addition passed in the78-check targeted run; production behavior was unchanged by that test-only addition.
- Real MotionHost handoff289ms; F07 shared scene target0 PID21416/HWND0x5078A and target1 PID23388/HWND0x6050C both HandedOff. This remains lifecycle regression evidence; shared production scene drawing is not yet integrated.
- Studio worker/race checks: **13 PASS**; discovery Complete122 candidates/40 packaged. Existing F08 actual UI acceptance: **16 PASS**, PID24472/HWND0x5080A/DPI120, including manual/discovered add, stable repair/binding IDs, removal, recovery and legacy backup. F09 library UI/scaling are still open.
- Core/Studio/MotionHost/check Release builds passed with0 warnings/errors; Core/check formatting verification passed. The original neutral development data is copied to Studio output, read/validated from that actual output, and visibly labelled development content. A framework-dependent publish output is also checked with `--f09-catalog-check`; this is not an installer claim.
- Independent read-only review found no remaining P1/P2 in the implemented reader/resource/timeline boundary after fixes. UI image decoding/pixel limits, real rendering/IPC and process integration remain later tasks.

Local logs: `%TEMP%/dms-f09-foundation-red.log`, `dms-f09-foundation-green.log`, `dms-f09-foundation-full-regression.log`. No real user state was used. Actual junction fixtures are removed non-recursively before deleting owned temp trees.

## Remaining gates

T008–T023 and T026 remain open: IPC v2/job adapter/independent deadlines, target-free session coordination, shared graphics integration, library/binding/home UI, real shared-scene F06 handoff, repeated/cancel/crash/hang/parent-exit tests, both actual system motion branches,100/125/150% scaling, integrated full regressions and final independent Converge. F09 is **not complete**. Changes to F03 resource bounds and F04's shared local policy have their own specification history and regression evidence.

## IPC v2 implementation — T008/T009/T026/T027

Explicit v2 requests separate Launch/Preview, retain lifecycle-only Launch diagnostics and single native dispatch, reject Preview handoff authority, and allow natural Completed only in Preview. Start/snapshot/keyframes use closed member sets including rejection of ignored computed isValid. Preview's watchdog reserves at least duration+500ms; the visual cap is10s while F05's target wait remains15s.

Tests first exposed two actual host compatibility failures: a future snapshot was decoded while the handshake client was still private, then disposed before the caller could report its precise error. The corrected handshake attempts a bounded250ms Failed(UnsupportedPlayback/UnsupportedProtocol/RequestMismatch) while owning the channel, then disposes. Real future schema/runtime tests verify exact request-bound Failed, no Started/visible window and process exit. Independent read-only follow-up review found no remaining P1/P2.

Final regression: **277 PASS/0 FAIL/1 explicit optional packaged-instance SKIP**; includes104 F09 cases, actual lifecycle handoff276ms and two F07 targets14988/HWND0xC0230 and22192/HWND0x1403E2 both HandedOff. Core, MotionHost and checks formatted; Core/Studio/host/checks/WindowHost Release builds passed0 warnings/errors. Local logs `%TEMP%/dms-f09-protocol-final-regression.log`, `dms-f09-host-compat-red.log`, `dms-f09-ipc-checks.log` preserve red/green evidence.

This is protocol/lifecycle verification. A temporary explicit UnsupportedPlayback guard remains in MotionHost until T011 supplies the shared surface. Jobs, parent-owned deadlines, actual shared rendering, preview coordination and F09 UI/scaling remain open. The earlier Remaining gates paragraph is historical to the reader milestone; current outstanding tasks are T010–T023.

## Owned process and independent deadlines — T010/T022/T023

Production now creates the host with STARTUPINFOEX/JOB_LIST directly in its unnamed non-inherited KILL_ON_JOB_CLOSE job, obtains a managed identity handle while suspended, and resumes its initial thread exactly once. Initial-thread count must be1; resume/initialization failures close ownership and bound cleanup. Native attribute values remain alive through CreateProcess; native thread/process handles are released on success and failure. The target is never assigned to this job.

Session Started/Completion observations are request-bound. Independent thread-pool deadlines enforce connect/Ready1s, Started750ms, visible<=10s and Stop1s grace. All terminal paths close ownership before notifying observers; Dispose adds at most1s forced-exit/protocol cleanup. Stop during startup closes ownership without sending an unsupported pre-Started Shutdown. Ordinary Stop has its own deadline and does not depend on a later Dispose. Outcome/error/nativeError commit once under the state lock.

**16 real Windows ownership checks passed**. They cover native membership before resume, parent/other fixture outside the job, explicit close, pre-resume exception, failed/invalid resume, bad-image CreateProcess failure, parent killed before resume and after verified worker execution, live-parent hung/no-Ready/no-Started/crashed host, startup Stop, visible Stop and Dispose with ignored Shutdown. The main checker blocks waiting for results; cleanup does not require its UI/main thread to pump. Identity checks include process creation times. No caller Stop/Dispose is issued before automatic hang/initialization/crash results.

Final full Core regression: **293 PASS/0 FAIL/1 explicit optional packaged-instance SKIP**. Actual F06 lifecycle handoff291ms; F07 targets32044/HWND0x110040 and15820/HWND0x5800D4 both HandedOff. Early parent26272/child24612 cleanup3ms; running parent19276/child21012 cleanup4ms. Live-parent hang8832 TimedOut937ms (800ms after Started plus startup); no-Ready29972 failed1067ms, no-Started19772 failed876ms, crash31020 failed141ms. Studio discovery/race regression **13 PASS**, actual122 candidates/40 packaged. Release builds and Core/check format pass with0 warnings/errors. An exploratory RID-specific harness invocation skipped WindowHost because its historical relative path assumed the non-RID layout; the final non-RID regression above ran the actual WindowHost checks and retained only the documented optional packaged skip.

Independent read-only review found and corrected two P2 races: startup Stop could be misclassified InvalidMessage, and ordinary Stop could wait for the10s watchdog. Added phase-specific regression checks pass. Follow-up review reports no remaining P1/P2 in this implementation boundary. Local logs: `%TEMP%/dms-f09-ownership-red.log`, `dms-f09-ownership-green.log`, `dms-f09-ownership-final-regression.log`.

Shared rendering/preview/UI tasks T011–T021 remain open. No F09 completion claim is made.
