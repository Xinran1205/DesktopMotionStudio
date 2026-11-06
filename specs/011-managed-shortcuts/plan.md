# Implementation Plan — F10 Managed Shortcuts

**Branch** codex/f10-managed-shortcuts · **Date** 2026-11-06

**Spec** [spec.md](spec.md)

## Summary

Deliver an explicit desktop entry for an enabled, playable binding, an independent Launcher that resolves current state, and verified repair/removal. Gate production filesystem mutations on the Windows native proof in Phase 0.

## Technical Context

C# and the repository's pinned .NET 10 SDK, WPF Studio, Windows 11 x64, Windows Shell Link COM and Win32 file handles. Reuse the F02 state repository, F04 typed target resolver and launch adapter, F05 instance coordinator, F06 lifecycle coordinator and F09 scene definitions. Keep receipts separate from profile schema2. No additional NuGet dependency. Bound link reads to 64 KiB, receipt reads to 16 KiB per binding and lock acquisition to 2 seconds. Paths used by Shell Link must be canonical local paths under the supported limits established by the Windows proof.

## Constitution Check

| Principle | Enforcement |
|---|---|
| Target application wins | F05 single-dispatch path, F06 fail-open, explicit native fallback for disabled or missing motion |
| Least privilege/local first | Current-user local desktop and private state only, no elevation, network or target writes |
| Explicit ownership | Exact position, identity, SHA256 and regular single-link file checked on the same retained handle as mutation |
| Stable boundaries | Core owns launch request and shortcut lifecycle. Windows adapter handles .lnk and file operations. Studio is presentation |
| Capability-based compatibility | Block unsafe/unsupported roots and busy handles. Verify native Windows evidence |
| Small increments | Native proof, Core contracts, independent Launcher, lifecycle, Studio, acceptance |
| Quality | Typed failures, bounded parsing and lock, real concurrency/failure cases |
| Versioning | Receipt schema1 independent of profile2, manifest1 and IPC2 |
| Dependencies | BCL, existing repo and Windows APIs only |
| Git | Review and commit each validated boundary |

## Project Structure

- src/DesktopMotionStudio.Core/Shortcuts/ — contracts, launcher request resolution, receipts and native operations.
- src/DesktopMotionStudio.Launcher/ — standalone Windows entry point and runtime wiring.
- src/DesktopMotionStudio.Studio/ — selected application actions and publish staging.
- tests/DesktopMotionStudio.Core.Checks/ — parser, resolver, fault injection and native file/shell checks.
- tests/DesktopMotionStudio.Studio.Checks/ — Studio state/keyboard and published application acceptance.

## Phase 0 — Research and risk gate

Validate same-volume no-overwrite publication, preserved file identity, retention of a checked handle through delete, exact-byte checks, hard-link rejection and parent path anchoring. Use handle-relative NT opens with no reparse traversal and same-directory handle rename. Use fresh isolated directories. Verify Shell Link round-trip and arguments. Document results in [research.md](research.md) and [evidence.md](evidence.md). Fallback for a filesystem that cannot provide these properties is Unavailable.

## Phase 1 — Data and contracts

See [data model](data-model.md), [interface contract](contracts/managed-shortcuts.md) and [quickstart](quickstart.md). The Launcher loads one validated state snapshot and selects animation for a playable enabled binding. Audio mute is separate. Existing instance coordination owns dispatch and fallback. Validate resolved executable identity and reject DMS component targets, including shortcut indirection. Resolve independent of receipt health, since a receipt governs mutations and the binding governs launch.

## Phase 2 — Implementation and acceptance

Use a receipt-first publication protocol. Keep current and pending proof during repair. Preserve a validated previous receipt when changing the primary name, with no-replace renames for both generations. Every deletion is through the retained validated handle. Serialize final binding validation and publication with profile writes using an unchanged-state transaction. Bind actions to an explicit selected ID, handle stale reloads, and expose errors in Studio. Publish the independent Launcher alongside the existing MotionHost payload and verify actual .lnk activation after Studio closes. Use a separate fixture executable with the same Launcher source and an isolated store for Shell activation tests. Complete regression, independent review and convergence before marking F10 complete.
