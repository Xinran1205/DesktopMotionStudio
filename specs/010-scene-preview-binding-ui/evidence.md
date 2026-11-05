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

## Production shared surface — T011/T028

The temporary unsupported-playback guard is removed with the real SceneSurface integration. Preview and Launch windows now own the same compiled vector-orbit control, pure timeline and paint implementation. Definition data controls accent/glyph/transforms; no scene-ID switch or target authority enters drawing. The host captures the read-only system client-area animation preference once per session; lookup failure uses static geometry/no fade. Preview explicitly labels simulated handoff, offers keyboard Stop/Esc, completes the finite sequence and reports Completed. Launch interrupts at actual target Ready. Every frame's GDI objects are deterministically disposed; drawing failure closes/reports RendererFailed.

Tests preceded implementation: the new surface project failed for missing SceneSurface; real selected-scene process checks produced6 failures with UnsupportedPlayback under the old guard. After implementation, **10 production drawing/window checks passed**, covering exact fixed-time Preview/Launch pixels in both injected motion branches, normal changing output, reduced stillness, explicit simulation/Stop controls, absence of suppressed paint errors, independent authored second work and1x1 viewport safety. These injected policy cases are not actual OS-toggle/scaling evidence.

Final full Core regression: **301 PASS/0 FAIL/1 optional packaged-instance SKIP**. Actual scene Preview31596/HWND0x3B074E naturally Completed3279ms after Started for a3000ms definition; Stop32416/HWND0x1004AA Exited27ms; Launch handoff23424/HWND0x17049C HandedOff289ms. Window lookup is constrained to the session's own HostProcessId. Actual scene+target31700/HWND1574044 remained alive and handed off before the complete sequence ended. Existing lifecycle handoff294ms, F07 targets21828/HWND0x1C0390 and29748/HWND0x6200D4 both HandedOff. Real WindowHost activation remains covered. Core/host/check formatting and Release/analyzers pass0 warnings/errors.

Independent read-only surface/protocol/resource review reports no actionable P1/P2. Local logs `%TEMP%/dms-f09-surface-red.log`, `dms-f09-playback-process-red.log`, `dms-f09-playback-process-green.log`, `dms-f09-surface-full-regression.log` retain exact scope. T012–T021 remain open for serial preview coordination, actual Studio library/binding UI, repeated/failure integration, actual system preference/scaling and final Converge. F09 is not complete.

## Target-free serial preview coordination — T012

Core PreviewRequest/session contracts expose validated playback, request identity, display mode, Started/Completion/error and owned disposal only. MotionHostPreviewSessionFactory always emits Preview with duration+500ms watchdog; no target/handoff/profile/dispatcher capability reaches this boundary. One asynchronous Channel reader owns the slot and monotonic request tickets. Old disposal finishes before new Start; failed or two-second-timed-out cleanup blocks future hosts. Startup is bounded3s, playing10s, and owner close drains commands, awaits cleanup and rejects later work. Current is immutable; StateChanged is a fresh-read invalidation signal and observer exceptions cannot interrupt cleanup.

**18 controlled races/contract checks passed**, including mode/snapshot/watchdog, natural Completed versus Skip, startup Stop/cancel, replacement disposal ordering, queued supersession, late old completion, invalid/pre-cancelled input preserving active state, precise compatibility error, start failure, cleanup exception/timeout, startup timeout, idempotent owner close and close during queued replacement. A new queued-cancel regression reproduced1 actual failure: latest C cancelled behind B waiting for A's gated disposal left Current at stale Starting. The fix settles old ownership and publishes current Stopped before completing C. Red and green logs: `%TEMP%/dms-f09-preview-queued-cancel-red.log`, `dms-f09-preview-green.log`; initial missing contracts are recorded in `dms-f09-preview-red.log`.

Independent read-only review reports no remaining P1/P2 after cancellation/deadline/notification ordering fixes. **31 Studio regression checks passed**, including these18, all existing discovery/worker/WPF races and actual122-candidate/40-packaged discovery. This does not claim new F09 external UI acceptance.

Actual coordinator integration creates three successive request-bound visible hosts and confirms every prior process exited before the next Start. Final run: preview0 PID24784/HWND0xF04C2, preview1 PID32544/HWND0x1E04CE, preview2 PID4456/HWND0x4D02CE. Superseded results remain tied to old requests; Stop closes the last; owner close rejects another host. Processes are observed through retained identity handles and no target is supplied to the coordinator.

Final full Core regression: **306 PASS/0 FAIL/1 optional packaged-instance SKIP**; production drawing/window checks **10 PASS**; Release/analyzers/format checks pass. Selected-scene natural Completed3279ms/3000ms definition, Stop31ms, handoff287ms; actual target-ready scene handoff and existing F06/F07/WindowHost regressions remain passing. Logs: `%TEMP%/dms-f09-coordinator-process.log`, `dms-f09-coordinator-studio-regression.log`, `dms-f09-coordinator-full-regression.log`. No real profile/state file was used.

Current gate: T013–T021 remain unbuilt/unverified (library/cover, current-playability binding transactions, picker/home, Studio preview/close wiring, actual UI/failure/scaling/motion preference and final Converge). F09 is still in progress.

## Studio library and binding UI — T013–T018/T029

Implemented SceneLibraryWindow, bounded/frozen PNG/JPEG cover loader, BindingPickerWindow, current-playability eligibility, home bound work/cover/Preview/Replace, shared preview status and owner cleanup. Cover source is bounded4MiB/4096², decoded within512px on both axes with OnLoad and frozen independent pixels. Optional missing/bad/unsafe art falls back. F07 default remains manifest-compatible; Studio's optional predicate reads current playback inside transaction-time catalog validation. State2/manifest1 are unchanged.

Tests preceded the boundaries. Core added13 eligibility checks: transactional playability, shared scene, unavailable no-op/resolve/replacement/enable, exception, stable identities/unrelated settings, removal after selection, Busy/I/O and default compatibility. Studio added11 cover cases,5 library/lifetime cases,3 save-error parent/picker cases and3 preflight ordering cases. Full Studio regression **53 PASS**, including actual discovery122 candidates/40 packaged, prior18 coordinator races and prior WPF/worker checks. Existing external F08 acceptance **16 PASS**, PID7788/HWND0x1F09BA/DPI120; its scene fixture now supplies valid playback while preserving all original assertions.

Independent review exposed signed-but-bad PNG/JPEG escaping FileFormatException (2 red regressions), queued playback surviving library close (1 red), pending scans restarting after Stop, and Applied followed by conflicting reload being misreported by the parent. Typed fallback, unconditional bounded close, shared PreviewIntentController and verified/preserved picker outcomes close those gaps. All regressions pass; no test assertion was removed or relaxed. An initial external replacement assertion used record/list reference equality after deserialization; it now compares complete serialized application data and still verifies all IDs, arguments, settings and unrelated bindings.

Actual external F09 acceptance initially **14 PASS** at DPI120, PID11468/HWND0x1606CC; natural preview27152/HWND0x290786. It browses two works, previews before app registration with exact state/backup preservation and unchanged fixture target count, rejects empty target selection, binds one work to two apps, replaces/cancels, verifies unavailable playback, Esc, library return, normal Studio close and abrupt parent exit. A later acceptance case explicitly exercises the home Stop button.

Studio now stages the matching built host and dependencies under MotionHost/ for both output and publish. Real build/publish validation caught MSBuild self-referencing metadata batching: files initially used wrong names/destinations. A separate source item list with qualified metadata fixes exact destinations. Fresh publish `%TEMP%/dms-f09-studio-publish-09b8e249033546df8ad4d5a3e47e60e8` contains the executable, DLL, deps/runtimeconfig and Core DLL. That published Studio independently passed the same14 UI cases, PID27412/HWND0x1004A8 and preview8856/HWND0xF01F4. This is framework-dependent publish verification, not an installer claim.

Logs: `%TEMP%/dms-f09-binding-ui-red.log`, `dms-f09-eligibility-green.log`, `dms-f09-cover-signed-red.log`, `dms-f09-ui-intent-green.log`, `dms-f09-binding-error-ui-green.log`, `dms-f09-external-ui-green.log`, `dms-f09-publish-ui.log`, `dms-f09-f08-ui-regression.log`.

## Actual desktop scaling and motion preference — T020 (verification in progress)

Native Windows Settings controlled the active display2 at2560x1440; display1 remained disconnected. Original values were125% and Animation effects On. Actual keyboard/browse/preview/bind/replace/error/cleanup acceptance passed at100% (14 checks; Studio11136/HWND0xB066E and preview11964/HWND0xB9035C, bothDPI96),125% (DPI120) and150% (14 checks; Studio16744/HWND0x60948, preview32664/HWND0x7C046A, bothDPI144). Actual library screenshots at100% and125% were inspected, with readable controls, safe fallback art and no clipped actions; an additional150% visual pass is recorded below when complete.

At restored125%, Windows Animation effects Off was read back through the actual system API: Studio11612/HWND0x140A78, preview28100/HWND0x9A0926; all15 cases (including preference assertion) passed. Animation effects was then restored On and read back True: Studio15920/HWND0x3005BC, preview23432/HWND0xE0938; all15 passed. Injected still-pixel checks remain separate from these actual OS preference runs. Settings screenshots confirmed the original125%/On values before the final visual follow-up.

## Real renderer failure and handoff budget — T019/T030 (final regression pending)

Full Core after eligibility:319 PASS/0 FAIL/1 optional packaged-instance SKIP. An additional real selected-scene crash is observed through an owned identity handle; an actual production scene UI thread is suspended through its exact HWND thread, leaving the parent alive and no caller Stop. Crash returned Failed/exit12ms; suspended UI returned TimedOut and exited1513ms under1500ms parent deadline (PID31512/HWND0x5305BC). These are production-host failures, supplementing the existing controlled ownership fixtures.

The first targeted run caught a306ms scene handoff with the historical250ms fade. Production fade is now200ms, keeping the300ms end-to-end assertion unchanged; rerun scene handoff235ms, PID4572/HWND0x4E0814. Crash26644/HWND0x370970 cleaned12ms; hung UI31512/HWND0x5305BC cleaned1513ms. `%TEMP%/dms-f09-real-failure-green.log` retains the failing boundary; `dms-f09-real-failure-final.log` records correction. Full final builds/format/regression and final Analyze/Converge remain pending until separately recorded.

## Final convergence evidence — 2026-11-05

T020 completed:150% final observed library screenshot showed full readable selection/details/actions without clipped controls; full16-case keyboard workflow (including home Stop and actual preference assertion) passed, Studio24076/HWND0x310938 and preview13596/HWND0xA80926, bothDPI144, system animationsTrue. Scaling was then restored to125% in native Settings and read back visually; final fresh published Studio independently confirmed DPI120 and animation preferenceTrue. No resolution/orientation/display-connectivity/transparency settings were changed. Original125%/Animation On are restored.

Final automated production-source checkpoint: **321 Core PASS/0 FAIL/1 documented optional packaged-instance SKIP;53 Studio PASS/0 FAIL;10 production drawing PASS/0 FAIL**. Release/analyzers and formatting passed for affected projects. Real lifecycle handoff238ms; F07 target29360/HWND0x3F05A0 and13884/HWND0x360938 both HandedOff. Early owned-parent31388/child26736 cleanup3ms; running4256/32036 cleanup4ms. Actual scene renderer crash24036/HWND0x250A8E cleaned9ms, hung UI20736/HWND0x7C0814 cleaned1518ms with live parent/no Stop. Logs `dms-f09-final-Core.log`, `dms-f09-final-Studio.log`, `dms-f09-final-Drawing.log`.

Independent final review strengthened only the test failure injector after that full checkpoint: opening a thread by raw ID could race with ID reuse. It now verifies the opened handle's process identity against a retained host process handle before Suspend, tracks successful suspension and restores only that owned handle. Follow-up source review is clean. The resulting15-check real playback/process suite passed: scene handoff240ms, crash22632/HWND0x3F07F6 cleanup11ms, UI hang1420/HWND0x4007F6 cleanup1518ms. No production code changed after the321/53/10 checkpoint. Log `%TEMP%/dms-f09-owned-thread-final.log`.

Final framework-dependent publish: `%TEMP%/dms-f09-final-publish-33d3f11e15fe4166a6428e99b181e67e`. Build and publish include the trusted development work and exactly the required five MotionHost files. Actual published catalog2 checks passed. Actual published Studio **16 UI checks passed** under restored125%/Animation On: Studio28744/HWND0x7703A8/DPI120, preview7840/HWND0x590940/DPI120. This includes the new home Stop action. Logs `dms-f09-final-publish-build.log`, `dms-f09-final-published-ui.log`. A complete installer and licensed content remain separate F12–F14 scope.

Spec Kit Analyze:12 FR +5 SC keys,31 mapped tasks,100% coverage,0 unresolved ambiguity/duplication/constitution conflicts. Convergence inspected all3 stories/11 acceptance scenarios,9 plan decisions and10 constitution principles. No missing/partial/contradicting/unrequested implementation remains in F09 scope; no convergence tasks appended. Independent final review found no unclosed P1/P2. The originally observed optional packaged-instance SKIP remains explicit and is not converted into a pass or broad compatibility claim.
