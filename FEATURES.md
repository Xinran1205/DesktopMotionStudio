# Feature inventory and delivery order

**Status:** Incremental implementation · **Version:** 1.0.11 · **Date:** 2026-11-04

The repository began with requirements documents only. F00–F07 are complete with automated, process, and interactive Windows acceptance evidence. Dependencies below are sequencing gates, not claims of implementation.

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
| 8 | Studio UI / F08 Application discovery and management | Add/search/detail/manage app profiles without broad system mutation | F02, F04, F07 | Specification refined; planning next |
| 9 | Studio UI / F09 Scene library, preview, binding | Browse and preview catalog, select target app, create binding | F03, F07, F08 | Spec baseline |
| 10 | Shell integration / F10 Managed shortcut lifecycle | Explicitly create/repair/disable only verified owned shortcuts | F02, F04, F07 | Spec baseline |
| 11 | Diagnostics / F11 Settings and local diagnostics | Theme, mute, status, bounded redacted logs and recovery actions | F02, F06, F10 | Spec baseline |
| 12 | Distribution / F12 Installer, upgrade and uninstall | Install, update/rollback, preserve config and clean owned artifacts | F10, F11 | Spec baseline |
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
