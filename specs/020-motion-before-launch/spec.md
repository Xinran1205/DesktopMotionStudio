# F19 — Play motion before launching

## US1 · See the opening before the app starts

For a new target process with an enabled playable binding, a managed shortcut and Studio's **Test launch** play the work and close its animation window before starting the target. An existing target window is reused through the existing launch route.

## Requirements

- **FR-001** Dispatch a cold target exactly once after the renderer reports a naturally completed animation and closes. Use the same ordering for every shipped work and both product entry points.
- **FR-002** Esc and the skip button close the animation and immediately allow dispatch. A missing, invalid, timed-out or crashed renderer releases the target launch within a bounded time. Preserve the Windows reduced-motion behavior.
- **FR-003** Keep the per-app launch gate across playback and dispatch. Recheck for a matching application after playback in case it was opened independently. Preserve existing window activation and the target's own bounds.
- **FR-004** Keep Preview target-free. Preserve application configuration, bindings, shortcuts, display choices and shipped work lengths. Report natural completion, skip and fallback separately.

## Acceptance

- **SC-001** Observe the real animation HWND with no target process. Verify the actual dispatch sees the animation closed for both natural completion and Esc. Test both built-in runtime types and both launch routes.
- **SC-002** Missing and incompatible renderer components fall back to one native dispatch. Overlapping requests reuse the first target, an external open during playback is recognized, and an explicit cancellation before dispatch remains canceled.
- **SC-003** Verify already-open activation, target-free preview, display override, reduced-motion playback, and exact profile and shortcut preservation.

This feature changes when a cold application starts. For example, the two-second Zero Boundary animation plays before the application's own loading time begins.
