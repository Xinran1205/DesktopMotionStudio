# Feature Specification: MotionHost lifecycle and fail-open

**Feature Branch**: `main`
**Created**: 2026-11-04
**Status**: Implementation in progress · v0.3.0
**Input**: PRD V1.0, Technical Design V1.0, F00–F05 contracts, project constitution

## Goal

For a managed launch whose target is not already running, show an optional, isolated visual host while the launcher starts the real application. Esc, a visible Skip action, deadline, host crash, invalid host input, and renderer failures must end only the visual layer. An existing application must be activated without starting MotionHost. The launcher remains the only component that chooses and starts the target application.

This feature delivers the production process/lifecycle boundary and a basic lifecycle surface. It does not implement artistic scene rendering, video/GPU effects, DWM window capture, Studio UI, or managed shortcuts; later scene and launcher features provide those capabilities.

## User Scenarios & Testing

### User Story 1 - Optional animation during a new launch (Priority: P1)

The user opens an application that is not running. The launcher starts MotionHost immediately before dispatching the application, then waits for the target window independently of the animation.

**Why this priority**: This is the primary product value and proves the host can fail without taking ownership of launch behavior.

**Independent Test**: Use a fake F05 window platform and a controlled MotionHost session; verify ordering, one target dispatch, window handoff, and host cleanup.

**Acceptance Scenarios**:

1. **Given** no matching target process and a valid built-in scene, **When** the user opens the managed entry point, **Then** a unique MotionHost request is started before exactly one F04 dispatch and the target wait proceeds concurrently with the visual lifetime.
2. **Given** the target window becomes ready, **When** the launcher signals handoff, **Then** MotionHost performs the bounded standard fade and exits without starting, activating, or modifying the target.
3. **Given** an exact existing target window, **When** the user opens the managed entry point, **Then** F05 activates it and no MotionHost process or animation is started.

### User Story 2 - Skip and bounded lifetime (Priority: P1)

The user can stop the visual layer with Esc or an accessible, visible Skip button while the target application continues its launch and window wait.

**Why this priority**: The overlay must remain dismissible and must never hold the real application hostage.

**Independent Test**: Trigger each skip input while the fake target launch is still waiting; assert the host exits and the F05 launch/wait completes normally.

**Acceptance Scenarios**:

1. **Given** the host is visible and the target is still starting, **When** the user presses Esc or activates Skip, **Then** the host disappears within 500 ms, reports a skip outcome when IPC remains available, and does not cancel, repeat, or terminate the target launch.
2. **Given** the host remains visible without handoff or skip, **When** its 15-second visual deadline expires, **Then** it closes itself and the target process remains unaffected.
3. **Given** system reduce-motion is enabled or the fade cannot initialize, **When** handoff is requested, **Then** the host closes immediately without blocking target availability.

### User Story 3 - Safe failure and cleanup (Priority: P1)

The launcher and host communicate through a versioned, per-request, same-user local channel. Either process may fail without leaving a stuck overlay or cancelling the other process's responsibilities.

**Independent Test**: Inject invalid protocol data, connection timeout, child crash, and parent disconnect; assert bounded cleanup and no extra target dispatch.

**Acceptance Scenarios**:

1. **Given** invalid arguments, unsupported protocol version, mismatched request ID, malformed or oversized message, or missing scene, **When** MotionHost receives the request, **Then** it exits with a structured failure where possible and creates no display window.
2. **Given** MotionHost crashes or its IPC disconnects, **When** F05 is waiting for the target window, **Then** F05 continues that wait and reports the target outcome normally; the target is not killed or relaunched.
3. **Given** the launcher exits or closes the channel, **When** MotionHost is visible, **Then** MotionHost closes within one second and releases its window and IPC resources.
4. **Given** target dispatch fails, target window wait times out, or handoff fails, **When** the launcher reports that outcome, **Then** the visual overlay exits within one second and the target process is left in its existing state.

## Functional Requirements

- **FR-001**: MotionHost MUST run in a separate process from the launcher and target application. It MUST NOT receive a target executable path, launch arguments, or authority to start, stop, or modify the target.
- **FR-002**: F05 MUST invoke an optional best-effort pre-dispatch callback only after confirming no existing matching target instance and immediately before F04 dispatch. Callback failure MUST be recorded as a non-fatal preparation error and MUST NOT prevent or duplicate F04 dispatch. Existing public F05 calls remain source-compatible.
- **FR-003**: A valid new-launch request MUST use a unique request ID and the installed built-in scene ID/version and display mode already validated by F01/F03. MotionHost MUST NOT accept arbitrary executable content, user paths, URLs, or scene code.
- **FR-004**: The local IPC contract MUST have an independent protocol version, one request-bound connection, same-user access, explicit message framing, and a maximum frame size of 8 KiB. Unsupported versions, mismatched request IDs, malformed messages, and unknown message kinds MUST fail closed for the visual host only.
- **FR-005**: MotionHost MUST surface an accessible Skip control and accept Esc while the host is active. Skip only ends the visual lifecycle; it MUST NOT cancel the F05/F04 target launch or its window wait.
- **FR-006**: MotionHost MUST enforce a 15-second maximum visible lifetime measured with a monotonic clock. The launcher MUST independently bound readiness, IPC, fade, and cleanup waits; none may be infinite.
- **FR-007**: When a confirmed target window is ready, the launcher MUST request a standard fade-out with a maximum duration of 300 ms. If the host cannot fade, is skipped, has crashed, or the system requests reduced motion, it MUST close immediately or remain closed. DWM fusion is out of scope.
- **FR-008**: Host startup, initialization, rendering-surface, media, GPU, IPC, or process failures MUST not cancel, repeat, terminate, or modify the target application. A failure result MUST use a stable error code and may include a native error code; diagnostics MUST exclude screen content, window titles, scene assets, and secrets.
- **FR-009**: If the launcher disconnects or exits, MotionHost MUST detect channel closure and close within one second. If MotionHost exits unexpectedly, the launcher MUST keep the F05 target wait running and observe child exit without blocking it.
- **FR-010**: The production host MUST use the built-in manifest's full-screen or centered display mode, show on the primary display in this feature, be absent from the taskbar, remain keyboard operable, and shut down without administrator rights, network access, hooks, input injection, or target-window capture.
- **FR-011**: Existing-instance activation MUST bypass the host. Concurrent F05 calls for the same AppId MUST retain F05 single-dispatch semantics; each request may create at most one host and one target dispatch.
- **FR-012**: The feature MUST be testable without a desktop using injected host-process and clock contracts, and it MUST include Windows desktop evidence for actual show, Esc/Skip, timeout, handoff fade, host exit, and parent-disconnect cleanup.

## Business Rules

1. The target application wins. The MotionHost is optional presentation, never a prerequisite for target dispatch.
2. A skip signal is distinct from the target launch cancellation token. F06 never passes a visual cancellation token to F04/F05.
3. Only a trusted catalog item that passed F03 validation is eligible; if it becomes unavailable between validation and host start, no visual host is shown and the target still launches.
4. A successful `WindowReady` means only that F05 found a structurally valid target window. It does not mean the target has finished loading its content.
5. Standard fade is a visual close animation only. F06 does not capture, embed, resize, or manipulate the target window.
6. If an application was already running, no scene is shown, even briefly.

## Inputs and Outputs

**Launcher inputs**: stable AppId, typed F04 LaunchTarget, a validated F03 scene manifest/catalog entry, and a target-wait cancellation token. The host factory path must be an application-installed absolute path.

**MotionHost start input**: protocol version, request ID, SceneId, SceneVersion, display mode, and bounded visual deadline. No target executable information is sent to the host.

**IPC events**: `Ready`, `Started`, `SkipRequested`, `Handoff`, `Finished`, `TimedOut`, `Failed`, `Shutdown`. `HandoffRequested` is an internal protocol state, not a wire message. Every frame carries protocol version and request ID.

**Launcher output**: the unchanged F05 `InstanceResult`, optional host lifecycle result, and an optional non-fatal host preparation error. Host outcomes do not overwrite or imply success for the target outcome.

## Boundaries, Exceptions, and Limits

- App launch failure: close host and return F05's launch failure.
- Target window timeout/process exit/identity change: close host; preserve F05 outcome and leave any still-running target alone.
- Host executable missing/unavailable, process start failure, pipe unavailable, initialization failure, crash, or protocol mismatch: record host failure and continue F04/F05.
- An unexpected exception from the F05 coordinator after MotionHost has started: attempt bounded host shutdown and disposal, then propagate the original F05 exception; do not leave an orphan overlay.
- User Skip/Esc: close host; continue target window wait without restarting it.
- Host visual deadline: close host; continue target wait if it remains active.
- Parent process exits: host sees pipe disconnect and exits within one second.
- Message above 8 KiB, duplicate start/handoff command, unexpected state transition, or wrong request ID: reject the frame and close host.
- No interactive desktop/session: host is not shown; F05 remains responsible for target handling.
- V1 uses the primary display because target window placement is not known until after launch. Target-monitor selection is deferred to a later renderer feature.
- This feature's surface is a neutral lifecycle shell. Actual scene assets, video/audio, particles, GPU effects, performance degradation, and DWM handoff are later feature scope.

## Test Requirements

- Unit/contract checks: F05 callback ordering and failure isolation; protocol framing/size/version/request binding, strict textual enum parsing, exact reads across fragmented streams, duplicate/unknown-property rejection; lifecycle state transitions; host launch ordering; no-host existing-instance path; skip, timeout, host crash, parent disconnect, handoff, target failure/exception, and one-dispatch semantics.
- Windows process integration: same-user named-pipe handshake, child process identity/exit, process-crash cleanup, and the real host window's Skip/Esc/fade/deadline behavior.
- Manual desktop evidence must state Windows build, display/DPI, host PID/window lifecycle, elapsed cleanup time, and whether the target remained alive. A build alone does not pass desktop behavior.

## Dependencies and Assumptions

- Depends on F01 `SceneManifest` and validation, F03 trusted built-in catalog, F04 typed launch targets, and F05 exact instance detection/activation.
- Additive F05 extension: a best-effort callback inserted between the post-gate existing-instance scan and F04 dispatch; no existing request signature or outcome is removed.
- Target platform is Windows 11 x64 and .NET 10, same interactive user session, ordinary user rights.
- Standard .NET named pipes with `PipeOptions.CurrentUserOnly` are available on the target runtime. Same-user processes are within the trust boundary; the pipe is not an isolation boundary against malicious code already running as that same user.
- The first host ships with a neutral placeholder surface; content rendering is explicitly deferred.

## Success Criteria

- **SC-001**: For an existing-instance request, the observed MotionHost process count is zero and F05 performs no target dispatch.
- **SC-002**: For a new-instance request, host start precedes exactly one F04 dispatch; skip, timeout, failure, and host crash leave that dispatch count unchanged.
- **SC-003**: Skip hides the real host window within 500 ms; handoff fade completes within 300 ms; deadline and parent disconnect close the host within one second.
- **SC-004**: All malformed/unsupported protocol cases are rejected before a window is created, and no frame larger than 8 KiB is allocated or processed as a valid message.
- **SC-005**: F05's original 15-second target-window limit and all existing F00–F05 contract checks continue to pass.
- **SC-006**: Windows desktop evidence verifies that a host crash or parent disconnect removes the overlay without killing or relaunching the target.

## Assumptions

- A user's managed launch entry has already resolved its binding and scene through future F07/F10 work; F06 consumes those validated objects.
- A pre-dispatch callback is synchronous and bounded to process creation only; it does not wait for pipe readiness or animation initialization. MotionHost initialization is asynchronous and failure-isolated.
- System reduce-motion is read by the host; a reduced-motion request suppresses fade animation.
- For this first host shell, full-screen/centered placement uses the primary display's current bounds/work area.

## Change History

| Version | Date | Change |
|---|---|---|
| 0.1.0 | 2026-11-03 | Initial lifecycle baseline. |
| 0.2.0 | 2026-11-04 | Define launcher/renderer responsibility, protocol, skip, deadline, safe handoff, failure matrix, measurable acceptance, test evidence, and additive F05 pre-dispatch seam. |
| 0.3.0 | 2026-11-04 | Clarify orphan-host cleanup if the F05 coordinator throws; require strict frame decoding and structured host failure propagation. |
| 0.4.0 | 2026-11-04 | Preserve an actual Skip/Esc result if user input races with a pending handoff; require a taskbar-excluded host style and record real Windows acceptance. |
