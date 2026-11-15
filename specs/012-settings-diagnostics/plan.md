# Implementation Plan — F11 Settings and Diagnostics

**Branch** codex/f11-settings-diagnostics · **Date** 2026-11-06

**Spec** [spec.md](spec.md)

## Summary

Deliver persisted Studio preferences, theme application, typed local launch diagnostics and access to F10 recovery. Keep diagnostics failures out of the launch and renderer path.

## Technical Context

Pinned .NET 10, existing WPF Studio and Windows 11 x64. Reuse F02 profile2, F05/F06 results, F10 Launcher and ownership service. No external telemetry or additional NuGet dependency. Diagnostics schema1 is independent of profile2 and IPC2. The existing audio preference remains independent of animation. Current vector works have no audio output.

## Constitution Check

| Principle | Decision |
|---|---|
| Target application wins | Log only once terminal dispatch and renderer cleanup are known. Skip logging when storage is busy or unavailable. |
| Least privilege/local first | Store typed diagnostic data per user, no network, no free text payloads. |
| Explicit ownership | Recovery delegates to F10, using its current verification and explicit actions. |
| Stable boundaries | Core owns preference transactions and diagnostic contracts. Studio owns presentation. Launcher emits only terminal outcomes. |
| Capability-based compatibility | Read Windows app theme/high contrast and rendering capability. Surface unreadable or missing values. |
| Small increments | Preference Core, visual behavior, durable diagnostics, integration, acceptance. |
| Quality | Bounds, privacy fixtures, crash and contention cases, actual Windows acceptance. |
| Versioning | Preserve profile2 and existing IPC. Unknown diagnostic schema is read-only. |
| Dependencies | Desktop framework and Windows APIs, pinned SDK. |
| Git | Commit verified increments. |

## Project Structure

- src/DesktopMotionStudio.Core/State/StudioSettingsService.cs — preference read and compare-and-save.
- src/DesktopMotionStudio.Core/Diagnostics/ — typed event, strict codec and bounded local storage.
- src/DesktopMotionStudio.Studio/ — settings, palette, diagnostic summary and recovery navigation.
- src/DesktopMotionStudio.Launcher/Program.cs — emit a bounded terminal event after dispatch and cleanup.
- tests/DesktopMotionStudio.Core.Checks/ and tests/DesktopMotionStudio.Studio.Checks/ — contract, failure, regression and Windows acceptance.

## Phase 0 — Research

Confirm system theme and high-contrast signals, a bounded private store safe for multiple processes, and failure-safe instrumentation. See [research](research.md). Capture a minimal isolated platform proof for any new capability before applying it across the UI.

## Phase 1 — Contracts

See [data model](data-model.md), [interfaces](contracts/settings-diagnostics.md) and [quickstart](quickstart.md). Save preferences by comparing the user's original snapshot within the existing repository update transaction. Retain unrelated application and binding fields. The diagnostic write path accepts only a typed event. It stores compact numeric and enumerated fields. Share formatted summaries generated from the same typed data.

## Phase 2 — Delivery

Implement in task order. Apply theme resources to all existing Studio views and use high-contrast system brushes when required. Bind diagnostics repair to the selected app and revalidate through F10. Regress published Shell launch with a locked or malformed log store, verify keyboard and current Windows palette, run independent review and converge.
