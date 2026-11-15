# Feature Specification: 运行实例检测与激活

**Feature Branch**: `main`
**Created**: 2026-11-03 · **Updated**: 2026-11-04
**Status**: Converged
**Input**: PRD V1.0, TDD V1.0, FEATURES.md, F04 launch contract

## Overview

When the user opens a managed application, Motion Studio must find and activate a confirmed existing instance. If none is running, it starts the target once and waits a bounded time for a usable window. It must not infer identity from the display name, window title, or executable basename, and it must not kill the target when discovery or activation fails.

## User Scenarios & Testing

### User Story 1 - Reopen an already-running application (Priority: P1)

The user opens an application that is already running and is returned to one of that application's confirmed windows without starting a second copy.

**Independent Test**: Start a benign test application, invoke the coordinator again for the same executable identity, and verify that the original process/window is selected and the launcher is not called again.

**Acceptance Scenarios**:

1. **Given** an existing visible window with an exact executable-path or AUMID match, **When** the user opens that application, **Then** the matching window is restored if minimized and activation is attempted, with no new launch request.
2. **Given** another application has the same file name or a similar window title, **When** the requested application is absent, **Then** that unrelated window is never activated.
3. **Given** a confirmed matching process exists but it has no usable window, **When** the user opens the application, **Then** the result reports that no usable window was found and no duplicate process is started.
4. **Given** Windows denies foreground activation, **When** activation is attempted, **Then** the result records that focus was not granted and the target window receives one bounded taskbar notification; the app does not repeatedly steal focus.

### User Story 2 - Launch and wait for a new application window (Priority: P1)

The user opens an application that is not running. The target is dispatched once and remains running even if no window appears before the wait expires.

**Independent Test**: Use a controlled window-host process that creates its window immediately, after a delay, or never; verify the result and process state for each case.

**Acceptance Scenarios**:

1. **Given** no exact matching instance exists, **When** the user opens the application, **Then** F04 dispatches exactly one launch and F05 waits up to 15 seconds for a window belonging to the launched process identity.
2. **Given** the launched process exits, the PID is reused, or a same-name process appears, **When** a candidate window is evaluated, **Then** F05 does not activate a window unless the process identity still matches.
3. **Given** the window appears before the deadline, **When** F05 completes, **Then** it returns the matching window and process identities for the caller to hand off to later features.
4. **Given** no qualifying window appears by the deadline, **When** the wait expires, **Then** F05 returns a timeout result, leaves the application running, and does not block beyond the 15-second window-wait limit.

### Edge Cases

- Minimized, hidden, cloaked, splash, owned, tray-only, or multiple top-level windows. A generic splash window cannot be distinguished from an app window without app-specific metadata or reading title/content; F05 uses only structural window state.
- Process exits during scanning, window closes during activation, PID reuse, inaccessible process metadata, or unsupported identity.
- The foreground lock prevents activation.
- Multiple near-simultaneous requests for the same application ID arrive from one or more launcher processes.
- Packaged apps may choose single-instance or multi-instance behavior; the feature can activate a confirmed existing window, but it cannot change an application's own instance policy.

## Functional Requirements

- **FR-001**: Accept a stable application ID and a typed F04 launch target. Resolve `.lnk` targets through the F04 identity contract; do not launch while resolving identity.
- **FR-002**: Match executables by canonical full path and packaged apps by exact AUMID. A display name, window title, or basename alone is never sufficient to identify an application.
- **FR-003**: Enumerate top-level windows, associate each with its creating process, and only return candidates whose process/window identity is confirmed. Do not capture or log window titles or screen contents.
- **FR-004**: Prefer a usable existing application window. Restore a minimized confirmed window before requesting foreground activation. If identity is confirmed but no usable window exists, return a structured no-window result and do not start a duplicate.
- **FR-005**: If no matching process exists, dispatch F04 exactly once and correlate subsequent windows with the returned process ID plus process creation time and exact target identity; a PID alone is not a durable identity.
- **FR-006**: Bound post-dispatch window discovery to 15 seconds. Timeout, process exit, or identity mismatch returns a structured result and never terminates the target application.
- **FR-007**: Attempt normal Windows foreground activation once. If Windows denies it, report `ForegroundDenied` and issue one taskbar attention notification; never use hooks, input injection, or repeated focus attempts.
- **FR-008**: Serialize concurrent requests for the same application ID within the current Windows user session. After acquiring the gate, each request rechecks current instances; at most one request dispatches F04 while the others reuse/activate the resulting instance. A caller waits at most 30 seconds for the gate, then returns `GateTimedOut` without dispatch.
- **FR-009**: Return structured outcomes for existing-instance activation, new launch/window ready, no usable window, timeout, denied focus, invalid identity, and native errors.
- **FR-010**: Run without administrator rights, network discovery, target process termination, or mutation of target files/configuration.
- **FR-011**: Support an optional synchronous best-effort `beforeDispatch` observer after the no-existing-instance scan and immediately before F04 dispatch. Invoke it only for the new-launch path under the AppId gate. Catch observer exceptions, record the native preparation error on the result, and still dispatch F04 exactly once. Calls that omit the observer retain their existing source behavior.
- **FR-012**: If a Launcher process exits while owning the per-AppId gate, the next waiting request can acquire the abandoned gate within the normal deadline. It must perform the same identity scan before deciding whether to dispatch.

## Business Rules, Inputs & Outputs

- **Input**: stable `AppId`, a valid typed F04 target, an injected F04 launcher, and an optional cancellation token used only to stop the caller's wait (never to kill the target).
- **Output**: a structured result containing outcome, matching PID and window handle when confirmed, elapsed wait, and optional native error code. Do not include window titles or user content.
- **Identity rule**: exact executable path or exact AUMID only. An inaccessible or ambiguous identity is not a match and must not cause an unrelated window to be activated.
- **Window rule**: hidden, cloaked, owned, and tool windows are not reported as ready. Foreground and the Windows process main-window signal provide deterministic preferences; equal best candidates return `AmbiguousWindow`. The feature does not read title text or pixels, so an unowned visible splash window can qualify as a window-ready signal. `WindowReady` means a structurally usable window exists; it does not mean app content has finished loading.
- **Concurrency rule**: requests for different AppIds proceed independently; requests for the same AppId serialize per user session and perform a fresh identity scan after acquiring the gate.
- **Timeout rule**: the 15-second limit starts after F04 returns a successful dispatch result. Timeout does not imply process failure and does not trigger relaunch.

## Success Criteria

- **SC-001**: Exact executable-path and AUMID matches activate only a confirmed target window; same-name unrelated apps produce zero false matches.
- **SC-002**: Deterministic same-process and separate-process concurrent-request tests for one AppId observe exactly one F04 dispatch and the same confirmed process/window identity for all callers that complete successfully.
- **SC-003**: New-window polling returns within 15 seconds and distinguishes ready, process-exited, identity-changed, and timeout outcomes.
- **SC-004**: A simulated foreground denial returns `ForegroundDenied` and makes one notification call with no retry loop.
- **SC-005**: A timeout or no-window result leaves the target process alive and does not modify unrelated files.
- **SC-006**: The optional pre-dispatch observer runs zero times for an existing instance and once before one dispatch for a new instance; an observer exception is reported without suppressing or repeating the target dispatch.
- **SC-007**: Kill a separate gate owner before dispatch with another process still holding a handle to the gate. The next request acquires the same gate and dispatches the verified target exactly once.

## Dependencies and Assumptions

- Depends on F04 typed launch targets/results and F02 stable application IDs.
- Windows 11 x64 interactive user session; no elevation.
- F05 coordinates processes and windows only. It does not infer that a visible window means application content has finished loading.
- A packaged application's own single-instance/multi-instance policy remains authoritative; F05 does not patch or inject into it.

## Change History

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-11-03 | Initial feature baseline. |
| 0.2.0 | 2026-11-04 | Define exact identity matching, no-window fail-safe, 15-second bounded wait, foreground-denial fallback, and same-AppId request coalescing. |
| 1.0.0 | 2026-11-04 | Finalize independent user stories, Windows API research, measurable acceptance criteria, and verified desktop proof. |
| 1.0.1 | 2026-11-04 | Bound same-AppId gate wait to 30 seconds; define window readiness as structural visibility and document the generic splash-classification limit under the no-title-read rule. |
| 1.0.2 | 2026-11-04 | Converge all ten tasks with 67 executed contract assertions and real Windows EXE/AUMID integration evidence. |
| 1.0.3 | 2026-11-04 | Add the F06-required best-effort pre-dispatch observer contract and regression criteria without changing existing F05 launch behavior. |
