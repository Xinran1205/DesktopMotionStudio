# Tasks: Scene library, preview and binding

**Input:** spec v0.2.0, plan, research, data-model and contracts. All tasks require real evidence before marking complete.

## Phase 1: Risk gates

- [X] T001 Build an isolated shared evaluator/WinForms paint POC in `specs/010-scene-preview-binding-ui/poc/SharedPlaybackPoc.csproj`; compare fixed-time pixels for Preview/Launch-simulation, changing output, reduced-path output, actual HWND/show/normal/Esc exit; record honest scope/evidence in `evidence.md` before production renderer work (FR-003/012).
- [X] T002 Prove non-inherited KILL_ON_JOB_CLOSE ownership with an owned worker, terminated parent and hung worker in `specs/010-scene-preview-binding-ui/poc/OwnedJobPoc.cs`; extend to creation-time job-list association and parent death before the worker's initial thread resumes, plus failed association cleanup and no target assignment; record evidence before production preview exposure (FR-005/010/012).

## Phase 2: US1 - Trusted complete-work foundation

**Goal/test:** An isolated catalog yields correct identity/presentation/playability with per-item fallback/errors and zero state writes.

- [X] T003 [US1] Write metadata/path/future-version/size/count/fallback checks in `tests/DesktopMotionStudio.Core.Checks/F09Checks.cs` before readers; preserve scene1/profile2 and F03 behavior.
- [X] T004 [US1] Implement reusable local ancestry/relative asset resolution in `src/DesktopMotionStudio.Core/Scenes/SceneAssetResolver.cs`; apply before F03 and new sidecar/cover probes in `BuiltInSceneCatalog.cs`, retaining diagnostics and ordinary-user/local-only boundaries.
- [X] T005 [US1] Implement strict bounded `ScenePresentation.cs`, `ScenePlaybackDefinition.cs` and `SceneLibrary.cs` readers/snapshots under Core/Scenes; separate IsAvailable, optional fallback and IsPlayable; preserve independent schema/runtime versions and validate all data-model limits.
- [X] T006 [US1] Add original neutral `Scenes/studio-pulse` data/attribution and an isolated second work fixture; copy shipped scene data through `DesktopMotionStudio.Studio.csproj` and MotionHost build layout as needed. Label development content honestly and do not claim F13/F14 delivery.

## Phase 3: US2 - Shared target-free playback

**Goal/test:** Real selected scene output uses a shared definition/evaluator/paint and preview has no target authority.

- [X] T007 [US2] Add pure timeline/finite-value/endpoint/interpolation/reduced-motion checks to `tests/DesktopMotionStudio.Core.Checks/F09Checks.cs`; implement Core `Scenes/SceneTimeline.cs` after T001.
- [X] T008 [US2] Add IPC v2 and state-transition tests in `F09Checks.cs`: old/future version rejection, strict bounded snapshot, request mismatch, Preview requires playback, natural Completed only for Preview, legacy lifecycle Launch and fail-open behavior. Keep F06 assertions meaningful.
- [X] T009 [US2] Implement explicit version2 contracts/codec and lifecycle extensions in Core `MotionHost/{MotionHostContracts,MotionHostFrameCodec,WindowsMotionHostProcessFactory,MotionHostLifecycleCoordinator}.cs`; add optional validated playback to launch coordination without delaying real target-ready handoff.
- [X] T010 [US2] Implement parent-owned safe job adapter with only the created host assigned before Start in Core/MotionHost, after T002; enforce graceful/forced cleanup bounds and handle assignment/start failure without leaks.
- [ ] T011 [US2] Promote shared paint to MotionHost `SceneSurface.cs`; integrate actual drawing/preview simulation/reduced motion/completion with `HostProtocolClient.cs`, `MotionHostWindow.cs` and `Program.cs` after T001/T007–T010. Remove the temporary unsupported-playback guard only with the real surface. Preview must never fall back to the lifecycle status card.
- [ ] T012 [US2] Implement target-free session contracts and serial generation coordinator in Core `Preview/{PreviewContracts,PreviewCoordinator}.cs`; cover cancel during startup, replacement, stale completions and closed-owner cleanup with controlled-session tests in Studio Checks `PreviewRaceChecks.cs`.

## Phase 4: US1/US3 - Library and binding UI

**Goal/test:** Browse and bind/replace from library/home with stable IDs, truthful errors and no app launch.

- [ ] T013 [US1] Implement WPF `SceneLibraryWindow.*` and bounded `SceneCoverLoader.cs`, library navigation, cover/fallback/details/empty/loading/error, keyboard operation and current playable action states; image streams use OnLoad/freeze/close.
- [ ] T014 [US3] Add default-compatible scene eligibility to Core `Bindings/BindingService.cs` and transaction-time current IsPlayable adapter for Studio; test removed work/app, old unavailable binding/no-op, Busy/I/O/concurrent changes and preservation in F09Checks.
- [ ] T015 [US3] Implement `BindingPickerWindow.*` plus confirm/cancel/saved-target validation in Studio; call F07 transactions, preserve IDs/unrelated state and refresh committed state; never hide reload errors with old success text.
- [ ] T016 [US3] Update Studio `MainWindow.*` with bound work name/cover/status, Preview/Replace entry points and intended scene/app selection; retain F08 generation/closed/readable protections and stored unavailable bindings.
- [ ] T017 [US2] Wire library/home Preview/Stop and Studio close to the shared coordinator; keep only one active owned preview, report actual outcome and prevent stale errors/selection changes from overwriting latest UI.

## Phase 5: Validation and convergence

- [ ] T018 Add actual external Studio acceptance in `tests/DesktopMotionStudio.Studio.Checks/F09UiChecks.cs`: two works, preview with no target, state/backup unchanged, bind one scene to two apps, replace with stable IDs, cancel, unavailable/error cases and no target activation. Extend fixture data without weakening F08 assertions.
- [ ] T019 Run Release/analyzers/format/Core+Studio regression checks, preserving F00–F08 real same-scene/two-target integration; prove actual shared renderer under F06 real target-ready handoff, repeated preview, skip/stop/crash/hang/parent-exit cleanup; record actual timings/PIDs/HWNDs in `evidence.md`.
- [ ] T020 Verify real Windows keyboard/visual browse/preview/bind/replace at actual100/125/150% and both reduced-motion preference branches, restore settings, and record each outcome rather than simulated-layout claims.
- [ ] T021 Independently review, Analyze and Converge all spec/plan/tasks/code; append/fix gaps, update `review.md`, `FEATURES.md`, version/compatibility documentation and commit the verified milestone.

## Dependencies and execution

T001 gates T007/T011; T002 gates T010 and production preview exposure. T003→T004→T005 precedes data/UI; T005 precedes T006/T007/T008. T008→T009; T009/T010→T011; T011→T012. T005→T013/T014; T014→T015→T016; T012/T013/T016→T017. T018–T021 require integrated implementation. Shared MotionHost/Binding/MainWindow edits are sequential.

The POCs can be researched independently, but no other feature starts before F09 converges. Isolated rendering proof does not close production ownership/IPC/actual handoff gates. Optional parallel examples after contracts settle: presentation/cover tests can proceed separately from pure timeline tests; read-only review can inspect one completed boundary while another is implemented. All mutation and UI acceptance runs stay sequential.

## Planning review supplements

- [X] T022 Implement the live-parent non-UI deadline in Core/MotionHost session after Started, independent of child Timer/Stop; test parent alive + hung host + no user Stop returns TimedOut and owned-job cleanup within10s in F09Checks/desktop integration (FR-005/012). This gates T019 completion.
- [X] T023 Use STARTUPINFOEX/PROC_THREAD_ATTRIBUTE_JOB_LIST in the T010 Windows adapter, preserving native process/thread handles and non-inheritance; resume the initial thread only after ownership is established and clean the job if resume fails. Cover early parent death before normal handshake, creation/association failure and no orphan. The suspended POC must not be copied without this production resume lifecycle. T002 gates this adapter; T023 gates T011/T019 (FR-005/010).
- [X] T024 Implement root-level TooManyScenes on entry257 before sidecar reads/sorting, with no partial library; add 256/257/entry257-duplicate cases to T003 and bounded enumeration in T004/T005 (FR-002/010). This gates T013.

## Incremental implementation

First prove rendering/ownership risks, then a catalog/read-only preview slice, then library→saved-app binding and home replacement. Finish failures/concurrency and full actual desktop evidence before claiming F09 complete. No licensed content, shortcut/test launch or installer work is folded into this feature.

## Implementation review follow-up

- [X] T025 Require at least one visible base Unicode rune in the bounded glyph, reject mark-only/variation-selector-only strings while accepting visible base plus combining mark; add regressions in F09Checks and strengthen ScenePlayback validation per data-model glyph rule/FR-002 (partial, P2).
- [X] T026 Enforce explicit closed JSON member sets for sidecars, including computed ignored properties such as isValid, and include the same injection case in T008 playback IPC tests; keep future-version precedence and nested keyframe shape checks per FR-002/011 (partial, P2).
- [X] T027 Preserve precise compatibility failures from host-side Start decoding: while the client still owns a writable pipe, send bounded Failed(UnsupportedPlayback/UnsupportedProtocol) before disposal; test the actual host receiving a future snapshot and emitting no Started/display per FR-002/005/011 (partial, P2).
