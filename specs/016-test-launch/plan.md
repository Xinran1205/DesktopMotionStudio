# Implementation Plan — F15 测试启动

## Architecture

Studio resolves Launcher with the existing installed/development layout. The test request uses `--test-binding` with the existing canonical binding token. In an installed bundle the stable shim verifies the selected Launcher image, starts it and forwards the child's exit code for this explicit mode. Launcher reports typed exit codes to Studio without a second dialog and remains the authority for the latest profile, application identity, diagnostics and fail-open playback. Studio owns a bounded asynchronous wait and per-selection feedback. Closing Studio detaches the wait and leaves the Launcher and target running.

Use the existing pinned .NET SDK and WPF surface. Keep the profile schema, shortcut format and the existing `--binding` behavior. The stable installed Launcher shim accepts the two exact forms and only waits for the explicit test form. An injectable process boundary allows deterministic testing. Explicit Studio profile or catalog overrides hide the action.

## Verification

Exercise fake startup and results first. Run the existing Launcher fixture and two published layouts against an isolated profile and harmless test application. Prove exact arguments, one dispatch, preserved hashes, correct completion feedback and native fallback. Verify Studio shutdown and keyboard access on Windows.
