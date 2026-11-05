# Implementation Plan: Scene library, preview and binding

**Branch:** `codex/f09-scene-preview-binding` · **Date:** 2026-11-05 · **Spec:** [spec.md](spec.md)
**Input:** F09 spec v0.2.0 and F08 milestone `90b70c9`.

## Summary

Extend the trusted catalog with independently versioned presentation/playback sidecars, add a bounded declarative vector timeline and one shared WinForms drawing surface, then provide target-free preview and WPF library/binding flows. Preserve profile schema 2 and scene-manifest schema 1. Explicitly version new playback IPC as 2; never silently add fields to v1. Risk spikes gate production integration.

## Technical Context

- C#/.NET SDK 10.0.403; WPF Studio and WinForms MotionHost; no new third-party packages.
- Core owns strict contracts, pure timeline evaluation, preview orchestration and F07 transactions. Graphics/Win32 jobs remain Windows adapters; no System.Drawing in Core.
- Preview orchestration uses a single asynchronous command reader and monotonic request tickets. Dispose old ownership before new Start; cleanup uncertainty blocks further hosts. Current is an immutable snapshot; StateChanged invalidates UI state rather than carrying stale queued payloads. See contracts/preview-coordination.md for startup/playback/cleanup budgets and cancellation/close semantics.
- Optional `scene.presentation.json` schema 1 carries bounded description/style/theme/cover. Required `scene.playback.json` schema 1 carries supported runtime identity and complete bounded keyframes. Optional presentation failure falls back; playback failure is unavailable.
- `vector-orbit` runtime version 1 is a compiled general renderer, not a switch on scene IDs. Maintainer-authored keyframes select transforms/opacity/accent; no expressions, scripts, arbitrary modules or user editor. New compatible works add data without editing the launcher.
- The evaluator takes elapsed time; Timer only requests redraw and Stopwatch measures monotonic time. Shared paint maps frames to the viewport. Reduced motion uses static geometry with bounded completion, not ordinary pulse/rotation.
- Existing profile limits remain 1000 apps / 5000 bindings / 4 MiB. Library bounds: 256 scene directories; sidecars 64 KiB, playback snapshot 4 KiB encoded / 8 keyframes. Cover bounds: 4 MiB / 4096x4096 source pixels, decode for display at no more than 512px width.
- Duration 1000-8000ms; connect/Ready each <=1s and visible-start <=750ms; a parent deadline independent of both UI dispatchers enforces a visible watchdog <=10s, even when host UI is hung. Stop allows 1s graceful then 1s forced owned-process cleanup. Launch preserves F06 <=300ms handoff when the real target is ready, without waiting for complete playback.
- Extend maintained executable harnesses. Actual Windows acceptance must prove visual output, isolation, cleanup, reduced motion and scaling; pure tests cannot establish display behavior.

## Constitution Check

All ten principles pass before and after design. Preview has no app/target/dispatcher authority. Launch keeps fail-open behavior. Only owned renderers enter non-inherited kill-on-close jobs. State remains transactional/versioned, unknown formats are protected, content is trusted local data, no dependencies are added, and PRD/TDD baselines remain traceable. Isolated playback and ownership gates precede production exposure. This feature does not claim installer or licensed-content delivery.

## Decisions and Interfaces

1. Keep base manifests/state and add sidecars, separating optional artwork from required playback. `IsPlayable` differs from F03 file-integrity `IsAvailable`.
2. Reuse safe local resource resolution before F03/sidecar/cover probes, including root ancestry and reparse components. Do not open first and validate afterward. Keep missing/corrupt optional covers nonfatal. Enumerate at most 257 directory entries before loading sidecars: seeing entry257 returns root-level TooManyScenes with no partial usable library, avoiding hidden duplicate IDs and unbounded sorting.
3. Choose one explicit IPC v2 envelope with Mode and immutable validated playback snapshot over parallel scene protocols or per-scene compiled ID registration. v1 peers fail safely to native target launch; v1 is not interpreted as v2. Existing lifecycle-only requests remain valid in Launch mode for F06 diagnostics/regressions; Preview requires supported playback and cannot show a status card.
4. Add natural Completed only for Preview. Launch still completes through Handoff/Shutdown. Snapshot and encoder retain the 8 KiB frame cap; host revalidates bounds/version/request identity.
5. Preview session exposes Started/Completion/Stop/Dispose. A serial generation coordinator closes the previous owned session before starting the latest. Cancellation covers handshake/playback/cleanup; stale completion cannot clear a new slot or replace new status.
6. Create only the new host directly in an unnamed KILL_ON_JOB_CLOSE job using STARTUPINFOEX/PROC_THREAD_ATTRIBUTE_JOB_LIST. Never inherit the job handle or add target processes. Create-then-Assign, even suspended, is insufficient because parent death can occur in between. Failed creation/association reports unavailable playback without an orphan or overlay.
7. Add optional F07 scene eligibility with default-compatible behavior. Studio supplies current IsPlayable eligibility within fresh catalog scanning during transactions; home/library agree. Preserve unavailable bindings rather than removing or retargeting them. Same-binding no-op semantics remain; current unavailability stays visible.
8. Studio adds library/binding windows and home Preview/Replace/name/cover. Retain F08 generation/closed/readable defenses. Load bounded cover bytes using BitmapCacheOption.OnLoad, freeze images, and dispose streams; bad covers fall back.
9. Include an explicitly named original neutral development work and a second isolated fixture. Neither is a F13/F14 licensed launch theme or release-quality content claim.

## Project Structure

- `specs/010-scene-preview-binding-ui/{research,data-model,quickstart,tasks,evidence,review}.md`, `contracts/{scene-library,playback-ipc}.md`, `poc/SharedPlaybackPoc.csproj`.
- Core `Scenes/{SceneAssetResolver,ScenePresentation,ScenePlaybackDefinition,SceneTimeline,SceneLibrary}.cs`; `Preview/{PreviewContracts,PreviewCoordinator}.cs`; `Bindings/BindingService.cs`; existing MotionHost contracts/codec/process factory/coordinator.
- MotionHost `SceneSurface.cs`, HostProtocolClient/Window/Program. Owned-job lifetime belongs to the parent Windows process factory, never the target adapter.
- Studio `SceneLibraryWindow`, `BindingPickerWindow`, `SceneCoverLoader`, `MainWindow`, and `Scenes/studio-pulse/*`.
- Core Checks `F09Checks.cs`; Studio Checks `F09UiChecks.cs` and `PreviewRaceChecks.cs`.

## Verification Sequence

First isolated evaluator/paint POC (Preview versus Launch-simulation, deterministic pixels, real show/close), and separate owned-job parent-exit/hang proof. Then contract tests, metadata/guards, evaluator/IPC/renderer, preview coordinator, UI/binding/home. Run previous regressions, independent review and desktop acceptance before Analyze/Converge. POC Launch-simulation is not production F06 handoff evidence.

## Complexity Tracking

No constitution exception. Sidecars avoid unnecessary state/base-scene migration. IPC v2 is necessary for immutable playback snapshots and natural completion. The vector runtime is bounded; future media/effects need independently versioned capabilities and validation.

Implementation clarification: Studio shares PreviewIntentController across home/library to reserve intent before asynchronous catalog validation; Stop/selection/navigation/close invalidate pending scans before any renderer submission. Binding picker success requires post-commit reload verification, and its parent preserves current errors/conflicts. StageMotionHost copies matching build/publish artifacts through a separate MSBuild source item with qualified destination metadata. A200ms fade leaves scheduling/IPC headroom inside the unchanged300ms total handoff criterion.
