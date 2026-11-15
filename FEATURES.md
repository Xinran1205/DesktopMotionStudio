# Feature inventory and delivery order

**Status:** Incremental implementation · **Version:** 1.0.38 · **Date:** 2026-11-08

The repository began with requirements documents only. F00–F12 are complete under the recorded acceptance scope. Dependencies below define the delivery order.

| Order | Module / Feature | Outcome | Depends on | Status |
|---:|---|---|---|---|
| 0 | Engineering foundation / F00 Windows host feasibility spike | Confirm a .NET/Win32 transparent overlay, keyboard close, monitor/DPI behavior and cleanup on target Windows before committing to rendering architecture | — | Complete (current desktop POC) |
| 1 | Core contracts / F01 Domain and versioned scene manifest | Validate scene metadata, stable IDs, asset references, and safe unsupported-version handling | F00 | Complete |
| 2 | Persistence / F02 Local profile repository | Atomic, versioned binding/settings storage and safe migration behavior | F01 | Complete |
| 3 | Scene library / F03 Built-in scene catalog | Discover packaged trusted scenes and assets without executing user content | F01 | Complete |
| 4 | Launch / F04 Application launch adapters | Represent executable/shortcut/packaged launch identities and return structured results | F01 | Complete |
| 5 | Window coordination / F05 Instance detection and activation | Activate existing instance; bounded wait for newly launched app window | F04 | Complete |
| 6 | Renderer / F06 MotionHost lifecycle and fail-open | Isolated renderer process, skip, timeout, crash cleanup, safe fade fallback | F01, F03, F05 | Complete |
| 7 | Binding / F07 Cross-app scene binding | Bind any compatible complete built-in scene to supported app, reusable scene IDs | F02, F03, F04 | Complete |
| 8 | Studio UI / F08 Application discovery and management | Add/search/detail/manage app profiles without broad system mutation | F02, F04, F07 | Complete; real UI at 100/125/150% |
| 9 | Studio UI / F09 Scene library, preview, binding | Browse and preview catalog, select target app, create binding | F03, F06, F07, F08 | Complete; library/preview/binding/home, real process/publish and actual100/125/150% + OS motion acceptance verified |
| 10 | Shell integration / F10 Managed shortcut lifecycle | Explicitly create, repair and remove verified owned shortcuts | F02, F04, F05, F06, F07, F08, F09 | Complete; durable receipts, actual Shell activation and fallback verified |
| 11 | Diagnostics / F11 Settings and local diagnostics | Theme, mute, status, bounded redacted logs and recovery actions | F02, F06, F08, F09, F10 | Complete · [Windows evidence](specs/012-settings-diagnostics/evidence.md) |
| 12 | Distribution / F12 Installer, upgrade and uninstall | Install, update/rollback, preserve config and clean owned artifacts | F10, F11 | Complete · workstation Setup, installed Shell launch and fallback, isolated rollback and ownership · [evidence](specs/013-installer-upgrade/evidence.md) |
| 13 | Studio / F15 Test launch | Launch the actual bound application from Studio before creating a desktop link | F05, F06, F09, F10, F11 | Complete · Alt+T, actual launch, warm reuse and fallback · [evidence](specs/016-test-launch/evidence.md) |
| 14 | Content / F13 零界 and launch performance | First original pure-tech work with full preview, short real-window handoff and two composition prototypes | F03, F05, F06, F07, F09, F10, F15 | Complete in published Windows bundle · [evidence](specs/014-first-licensed-scene/evidence.md) |
| 15 | Studio / F16 Home and product icon | Polish the application-to-test-launch path and integrate one Windows product icon | F08–F13, F15 | Complete · 0.16.0 local candidate · [evidence](specs/017-studio-home-polish/evidence.md) |
| 16 | Studio / F17 Bilingual client | English and Simplified Chinese for product text and built-in works, plus paired GitHub READMEs | F11, F13, F16 | [Spec](specs/018-bilingual-client/spec.md) · [Evidence](specs/018-bilingual-client/evidence.md) |
| 17 | Studio / F18 Motion display area | Choose the recommended, compact, large, or full-screen animation area | F09, F11, F13, F17 | [Spec](specs/019-motion-display/spec.md) · [Evidence](specs/019-motion-display/evidence.md) |
| 18 | Launch / F19 Motion before launch | Finish or skip an enabled animation before dispatching a cold target | F05, F13, F15, F18 | [Spec](specs/020-motion-before-launch/spec.md) · [Evidence](specs/020-motion-before-launch/evidence.md) |
| 19 | Renderer / F20 Local MP4 feasibility | Real native playback, first frame, fixed fades and ordering in an isolated probe | F18, F19 | [Verified probe](specs/021-mp4-feasibility/evidence.md) |
| 20 | Renderer / F21 Production MP4 playback | A packaged video session with the existing preview and launch experience | F20 | [Verified on Windows](specs/022-mp4-playback/evidence.md) |
| 21 | Studio / F22 Local MP4 import | Local video library, validation, persistence and three playback options | F21 | [Verified on Windows](specs/023-local-mp4-import/evidence.md) |
| 22 | Content / F23 First curated MP4 work | An original six-second performance and verified release assets | F21, F22 | [Verified on Windows](specs/024-first-mp4-work/evidence.md) |
| 23 | Studio / F24 AI motion creation guide | Copyable production prompts and verified MP4 export guidance | F22, F23 | [Verified on Windows](specs/025-ai-motion-guide/evidence.md) |
| Later | Content / F14 Second licensed scene | A visually distinct work using rights-cleared licensed content | F13, F23 and rights clearance | [Spec baseline](specs/015-second-licensed-scene/spec.md) |

## Selection for first feature

F00 is first because the existing TDD identifies transparent top-level rendering as a high-risk unknown; validating it early prevents building UI and contracts around an infeasible host. It is a bounded prototype, not a product feature release. It must demonstrate full-screen/centered placement, Esc close, DPI-aware monitor selection, transparency, and clean shutdown. Windows App SDK/WinUI 3 integration and GPU-composition remain separate later decisions.

The F IDs here are engineering feature IDs and do not map one-to-one to the PRD F01–F12 capability IDs. The PRD's two proposed launch scenes map to F13 and F14; their specific creative identity and rights require per-asset verification before release.

Develop the built-in works one at a time with distinct styles. User-requested directions include anime, technology, comedy and cinematic spectacle. The development bundle currently contains Studio Pulse.

See the [MP4 workflow](docs/MP4_WORKFLOW.zh-CN.md) for the current stage and the [prior roadmap](docs/NEXT_STAGE.zh-CN.md) for the code works and workstation release acceptance.

## Decomposition note

Each feature spec must include goal, user scenarios, functional requirements, rules, inputs/outputs, boundaries, failures, measurable acceptance criteria, technical dependencies, and tests. Product behavior remains aligned to PRD V1.0 unless an ADR records a justified correction.

## Change history

| Version | Date | Change |
|---|---|---|
| 1.0.0 | 2026-11-03 | Initial module map and dependency order. |
| 1.0.1 | 2026-11-03 | Align F00 with isolated Win32 host POC; add second scene to cover PRD launch scope. |
| 1.0.2 | 2026-11-04 | Record F00/F01 delivery status. |
| 1.0.3 | 2026-11-04 | Record F02 delivery status. |
| 1.0.4 | 2026-11-04 | Mark F02 complete and start F03. |
| 1.0.5 | 2026-11-04 | Record F03 completion and start launch adapter design. |
| 1.0.6 | 2026-11-04 | Complete F04 with Windows launch evidence and start F05 instance coordination. |
| 1.0.7 | 2026-11-04 | Complete F05 with cross-process synchronization and live Windows EXE/AUMID evidence; start F06 MotionHost lifecycle. |
| 1.0.8 | 2026-11-04 | Record F06's process/pipe evidence and 287 ms handoff; desktop acceptance remained open. |
| 1.0.9 | 2026-11-04 | Complete F06 after 96 passing checks and Windows 11 desktop acceptance for Skip, Esc, host-crash cleanup, no-taskbar style, placement, reduced-motion close, and a 291.3 ms standard fade. |
| 1.0.10 | 2026-11-04 | Complete F07 binding lifecycle and transactional state updates after independent review, 134 total passing checks, and real same-scene/two-target handoff. |
| 1.0.11 | 2026-11-04 | Start F08 with explicit application-management, typed-target, legacy migration and Windows UI acceptance specification. |
| 1.0.12 | 2026-11-05 | Complete F08 WPF management, typed metadata/migration and bounded local discovery after independent review, 171 Core checks, 13 Studio checks and 16 UI assertions at each actual 100/125/150% scale; advance to F09. |
| 1.0.13 | 2026-11-05 | Refine F09 with explicit F06 dependency and truthful shared-playback/target-free preview requirements; plan24 tasks and verify isolated drawing and creation-time ownership risks before production integration. |
| 1.0.19 | 2026-11-05 | Verify F09 reader/resource/content/timeline increment:78 targeted checks,248-pass full Core regression,13 Studio checks,16 actual F08 UI checks and copied/published scene-data validation. Review fixes tracked as T025/T026; IPC/UI remain open. |
| 1.0.20 | 2026-11-06 | Complete F09 with31/31 tasks, independent review/convergence,321 Core/53 Studio/10 drawing checks,16-case published UI and actual100/125/150% plus OS motion verification. Save user-requested pause in docs/RESUME.md; F10 remains a baseline. |
| 1.0.21 | 2026-11-06 | Resume F10, complete specification analysis and pass 22 isolated Windows ownership and Shell Link checks. |
| 1.0.22 | 2026-11-06 | Complete F10 with 395 Core checks, 57 Studio checks and 12 actual published UI and Shell checks. |
| 1.0.23 | 2026-11-06 | Start F11 with independent spec, 12 preference Core checks and Fluent theme resource proof. |
| 1.0.24 | 2026-11-06 | Complete F11 with published keyboard and Shell checks, real Windows contrast recovery and independent convergence. |
| 1.0.25 | 2026-11-06 | Specify F12 offline install, rollback, stable Launcher and receipt-driven cleanup. |
| 1.0.26 | 2026-11-06 | Verify F12 reproducible self-contained payload and selector crash behavior. Reject MSI registration rollback after native proof. |
| 1.0.27 | 2026-11-06 | Verify F12 stable entry forwarding, guarded package extraction, Start menu link and inactive program publication. |
| 1.0.28 | 2026-11-06 | Build self-contained Setup and verify install, upgrade/rollback and uninstall/reinstall with isolated Windows registration. |
| 1.0.29 | 2026-11-07 | Verify Setup, Start menu launch, real selector rollback and registered removal on Windows. Stage media for clean-account acceptance. |
| 1.0.30 | 2026-11-07 | Trace the original test-launch requirement to F15 and schedule it after F12 acceptance and before the licensed works. |
| 1.0.31 | 2026-11-07 | Close F12 using the revised workstation acceptance and recorded compatibility scope. |
| 1.0.32 | 2026-11-07 | Close F15 with installed and unpackaged evidence. Record the requested scene directions and pause feature work for user familiarization. |
| 1.0.33 | 2026-11-07 | Refine F13 as the original pure-tech work 零界. Add F16 homepage and icon polish, then validate one combined candidate before scheduling F14. |
| 1.0.34 | 2026-11-08 | Complete F13 with original art, finite launch playback, safe handoff and real Windows evidence. Advance to the F16 homepage and icon. |
| 1.0.35 | 2026-11-08 | Complete F16 and verify the combined 0.16.0 local candidate, including isolated upgrade and real Windows UI. |
| 1.0.36 | 2026-11-08 | Add English-first GitHub documentation and optional English Studio UI, with separate language storage and preserved user names. |
| 1.0.37 | 2026-11-08 | Add user-selected animation placement and play bound motion before a cold application launch. |
| 1.0.38 | 2026-11-08 | Preserve the two code works and define the MP4 playback, import, curated work and AI authoring stages. |
