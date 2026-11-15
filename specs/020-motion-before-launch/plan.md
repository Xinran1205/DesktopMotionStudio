# Plan — F19 motion before launch

Reuse the existing application launch gate and its existing-window check. Add an awaitable preparation path that keeps ownership until playback cleanup and dispatch, then rechecks the target identity for a matching process that appeared during playback. Preserve the existing low-level parallel launch contract for existing internal callers and checks.

Define a dedicated IPC v5 pre-launch mode. It requires validated playback, accepts natural completion, and has no Handoff command. MotionHost uses launch controls and finite playback for both built-in runtime types. The render window closes before completion is reported. Wait for renderer disposal before dispatch. A completion budget covers the bounded handshake and work duration. The existing 60-second Test launch timeout covers the gate, renderer and target window budgets. Preview remains a separate request mode.

Treat natural completion and skip as successful preparation. Preserve structured failures and the normal fallback. The new mode respects the shortened presentation for Windows reduced motion. No profile or scene schema migration.

The prior parallel timing is historical F13 evidence. This feature supersedes the timing for real launch entry points. Keep fail-open and bounded playback under the constitution.
