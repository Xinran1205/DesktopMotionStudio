# Feature inventory and delivery order

**Status:** Incremental implementation · **Version:** 1.0.28 · **Date:** 2026-11-06

The repository began with requirements documents only. F00–F11 are complete with automated, process and interactive Windows acceptance evidence. Dependencies below define the delivery order.

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
| 12 | Distribution / F12 Installer, upgrade and uninstall | Install, update/rollback, preserve config and clean owned artifacts | F10, F11 | Clean-account acceptance pending · verified native workstation Setup, rollback and removal · [evidence](specs/013-installer-upgrade/evidence.md) |
| 13 | Content / F13 First licensed scene | One high-quality scene with cleared assets and cross-app handoff | F03, F06, F07 | Spec baseline |
| 14 | Content / F14 Second licensed scene | A visually distinct second built-in scene, reusable across apps | F13 | Spec baseline |

## Selection for first feature

F00 is first because the existing TDD identifies transparent top-level rendering as a high-risk unknown; validating it early prevents building UI and contracts around an infeasible host. It is a bounded prototype, not a product feature release. It must demonstrate full-screen/centered placement, Esc close, DPI-aware monitor selection, transparency, and clean shutdown. Windows App SDK/WinUI 3 integration and GPU-composition remain separate later decisions.

The F IDs here are engineering feature IDs and do not map one-to-one to the PRD F01–F12 capability IDs. The PRD's two proposed launch scenes map to F13 and F14; their specific creative identity and rights require per-asset verification before release.

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
