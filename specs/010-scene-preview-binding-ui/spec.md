# Feature Specification: 动效库预览与绑定界面

**Feature Branch**: `codex/f09-scene-preview-binding`
**Created**: 2026-11-03 · **Revised**: 2026-11-05
**Status**: Complete · **Version**: 1.0.0
**Input**: PRD library/application journeys, TDD preview isolation/shared execution, F03/F06/F07/F08.

## Goal

Let the owner browse trusted complete built-in works, watch a bounded preview without opening any target application, and bind or replace a work for a saved application. Home reflects the committed relationship. This feature establishes truthful scene playback and library interaction; licensed launch content remains F13/F14.

## User Scenarios & Testing

### User Story 1 - Browse complete works (Priority: P1)

The owner opens the built-in library and inspects a work's cover, name, description, style/theme information and availability. Theme inspiration never restricts the target application.

**Why this priority:** Work identity and compatibility must be clear before preview or binding.

**Independent Test:** Use an isolated catalog with two valid works, duplicate IDs, unavailable resources and missing optional artwork; verify visible identity, fallback and action states.

**Acceptance Scenarios**:

1. **Given** valid built-in works, **When** library opens, **Then** names, covers or readable fallbacks, descriptions, theme/style and availability agree with trusted catalog data.
2. **Given** duplicate IDs, missing/corrupt resources or unsupported playback, **When** inspected, **Then** the problem is visible and unavailable actions cannot report success; other valid works remain usable.
3. **Given** an empty/unreadable catalog, **When** library opens, **Then** a recoverable empty/error state appears and My applications remains available.

### User Story 2 - Preview without launching an application (Priority: P1)

The owner previews a selected complete work, can stop it, and returns safely to Studio. Preview uses a clearly identified simulation of target handoff, without activating or modifying a real target.

**Why this priority:** The owner needs to assess the actual work before committing a binding.

**Independent Test:** Preview an authored test work with no target registration; verify visible changing scene output, completion/skip/crash cleanup and unchanged target-process count/state bytes. Exercise the same work through the launch renderer and compare the shared scene execution.

**Acceptance Scenarios:**

1. **Given** a playable work, **When** Preview is selected, **Then** its complete timed visual sequence plays using the same scene definition and execution as launch, with simulated handoff and no target launch or activation.
2. **Given** an active preview, **When** Esc/Stop is used, the work ends, Studio closes or playback fails, **Then** owned preview resources are released within a bounded policy and Studio reports the actual result.
3. **Given** rapid repeated preview or changing selection, **When** another preview is requested, **Then** at most one owned preview remains active and stale results cannot replace the latest status.
4. **Given** the Windows reduced-motion preference, **When** preview plays, **Then** the renderer uses its reduced-motion path without disabling skip or watchdog cleanup. Preview does not change that system preference.

### User Story 3 - Bind, replace and inspect from home (Priority: P1)

The owner selects an already registered supported application and binds the chosen work. From home the owner can preview or replace its binding without editing configuration.

**Why this priority:** This completes the application-to-work management flow on the verified F07 rules.

**Independent Test:** Bind one work to two distinct saved applications through Studio, replace one, restart, and inspect both home details and persistent IDs. No application process is launched during these actions.

**Acceptance Scenarios:**

1. **Given** a valid work and saved supported app, **When** binding is confirmed, **Then** F07 commits one enabled relationship, home shows its current name/cover/status and the same work remains available to other apps.
2. **Given** an existing binding, **When** another work is chosen and saved, **Then** appId and bindingId remain stable and unrelated registrations/settings/bindings are unchanged; cancelling changes nothing.
3. **Given** no target selection, a removed/legacy-unconfirmed app, unavailable work, Busy/I/O failure or a concurrent edit, **When** binding is attempted, **Then** the actual failure is shown, no false success appears and valid prior state is preserved.
4. **Given** the app's binding on home, **When** Preview or Replace is selected, **Then** the intended complete work/app is selected; preview remains read-only and replacement follows the same validation/transaction rules.

### Edge Cases

- Missing optional cover versus missing required playback assets; corrupt metadata; duplicate scene IDs; unsupported newer scene/presentation/runtime versions.
- A scene disappears after selection; app is removed or repaired concurrently; stale asynchronous catalog or preview results.
- Repeat preview, skip while starting, renderer start failure/crash/hang and Studio exit; no orphan overlay or target activation.
- Theme description/cover is informational, never an executable target or app-specific restriction.
- Ordinary keyboard use, scrolling, actual 100/125/150% scaling, reduced motion and recoverable errors.

## Requirements

### Functional Requirements

- **FR-001:** Add a built-in library reachable from My applications, with name, cover/fallback, description, style/theme and explicit availability. Home remains the default destination.
- **FR-002:** Load only trusted local catalog content through F03 validation. Keep scene identity/version separate from presentation/playback compatibility; protect unknown newer versions and unsafe paths. Missing optional artwork uses a fallback; required playback failures reject preview.
- **FR-003:** Preview plays the selected complete work using the same definition/execution as launch. A status card or unrelated animation cannot stand in for an unsupported work; show unavailable playback instead.
- **FR-004:** Preview never launches/activates a target or receives authority to select an executable. Simulated handoff is identified as preview. Preview/browsing/cancellation do not write profile state or shortcuts.
- **FR-005:** Enforce one active owned preview, bounded startup/runtime/cleanup, Esc/Stop, parent-exit/crash cleanup, cancellation and stale-result rejection. Return a structured result and readable failure/retry state.
- **FR-006:** Honor reduced motion in the shared renderer, maintain keyboard controls and avoid changing system preferences. Current Studio styling is retained; theme/mute settings UI is F11.
- **FR-007:** Let the owner choose a saved app and bind/replace a complete work through F07 latest-state transactions. Each app has one current binding; multiple apps may share a work regardless of theme.
- **FR-008:** Preserve appId/bindingId on replacement, no-op and cancellation behavior, unrelated changes and prior valid bytes on failure. Refresh actual saved state; display current errors if a post-commit reload fails.
- **FR-009:** Home shows current bound work name/cover/status with Preview/Replace actions, including recoverable unavailable-scene status. Do not claim shortcut creation/health or dispatch a test launch in this feature.
- **FR-010:** Operate locally with ordinary rights; no arbitrary scene code, plugins, network fetch, elevation, global hooks or target-file mutation. Validate identity, bounds, versions and ownership at process/catalog boundaries.
- **FR-011:** Keep F00–F08 behavior and state compatibility passing. Any scene/runtime/IPC extension has an explicit independent version and protects existing/unknown formats.
- **FR-012:** Prove risky shared playback and preview isolation with a minimum runnable experiment before formal integration. Record actual process/window, visual, cancellation, reduced-motion and scaling evidence before claiming completion.

### Business Rules, Inputs & Outputs

- **Inputs:** Selected trusted complete scene; saved application identity for binding only; current catalog/state and Windows motion preference.
- **Outputs:** Read-only library/preview status, bounded owned scene playback, or a committed/refreshed binding result.
- **Scene presentation:** Human-facing cover/description/style/theme tied to stable scene identity, with safe optional fallbacks.
- **Playable scene:** Validated complete definition plus an explicitly supported built-in execution capability; no target identity.
- **Preview session:** Owned request identity, selected scene snapshot, lifecycle/result; no executable or app-dispatch authority.
- **Rule:** No import/export, user scene editing, per-effect sliders, arbitrary scripts/DLLs, test launch or owned-shortcut actions in F09.

## Success Criteria

### Measurable Outcomes

- **SC-001:** Real Studio acceptance browses two isolated works, previews one without target registration/activation or profile writes, binds one work to two apps and replaces one while retaining IDs.
- **SC-002:** An authored complete test work produces visible timed scene output through preview and launch using the shared execution; completion, Esc/Stop, repeated requests, host crash and parent exit all clean up owned resources within the documented bounds.
- **SC-003:** Invalid/missing/future/unsafe catalog inputs and removed/unconfirmed targets/Busy/I/O failures have automated coverage with preserved valid bytes and accurate UI errors.
- **SC-004:** Logged-in Windows evidence covers keyboard browse/preview/bind/replace at 100%, 125%, 150% actual scaling and both reduced-motion branches, restoring starting settings.
- **SC-005:** Relevant Release builds/static/format checks and F00–F08 regression/integration checks pass; independent review and Spec Kit Analyze/Converge leave no unverified required tasks.

## Assumptions

- Depends on F03/F06/F07/F08. The initial baseline omitted F06 from the preview dependency; it is now explicit.
- F13/F14 deliver licensed launch works. F09 may include an original neutral development work/fixture to validate complete shared playback; it must not be represented as either licensed launch theme or release-quality content.
- Existing lifecycle-only host output is not evidence of actual scene preview. Unsupported scene content remains unavailable until its execution is supported.
- Renderer choice, metadata structure, version extensions and numeric lifecycle limits are Plan decisions; no formal runtime implementation precedes Plan/Tasks/Analyze.

## Change History

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-11-03 | Initial independent feature baseline; refine before implementation. |
| 0.2.0 | 2026-11-05 | Define library/presentation, truthful shared playback with target-free preview, F07 binding/replacement, home integration and real risk/scaling/reduced-motion gates; retain licensed content and shortcuts as separate features. |
| 1.0.0 | 2026-11-05 | Verified complete library/preview/binding/home implementation;31 tasks, independent review, actual desktop/publish and final SDD convergence evidence recorded. |
