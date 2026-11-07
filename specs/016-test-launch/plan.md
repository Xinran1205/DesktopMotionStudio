# Implementation Plan — F15 测试启动

## Architecture

Studio resolves the bundle's Launcher with the existing installed/development layout and submits the same encoded binding argument used by managed desktop entries. Launcher remains the authority for the latest profile, application identity, diagnostics and fail-open playback. Studio owns request submission, per-view feedback and keyboard access. Wait for the owned Launcher process asynchronously. Closing Studio leaves an already dispatched target running.

Use the existing pinned .NET SDK and WPF surface. Keep the profile schema, shortcut format and Launcher CLI grammar. An injectable Launcher process boundary allows deterministic testing without writing a shortcut or invoking an arbitrary executable from Studio.

## Verification

Exercise fake startup and results first. Run the existing Launcher fixture and two published layouts against an isolated profile and harmless test application. Prove exact arguments, one dispatch, preserved hashes, correct completion feedback and native fallback. Verify Studio shutdown and keyboard access on Windows.
