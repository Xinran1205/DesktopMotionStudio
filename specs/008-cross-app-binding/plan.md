# Implementation Plan: Cross-application binding

**Branch**: `codex/f07-cross-app-binding` · **Date**: 2026-11-04 · **Spec**: [spec.md](spec.md)

## Summary

Add a Core binding service over F02/F03/F04 and a path-scoped atomic read-modify-write operation. Retain schema 1. F07 receives explicit typed targets from an injected application-registry provider; it does not guess the meaning of the legacy profile string. F08 owns future target metadata persistence. Resolved targets/scenes can be supplied to F06 without putting launch behavior in the binding service.

## Technical Context

- C# / .NET 10.0.403, nullable/analyzers/warnings as errors; no new packages.
- State remains F02 JSON schema 1, 4 MiB / 1000 apps / 5000 bindings limits; atomic replace and last valid backup remain.
- Windows current-user named mutex per normalized repository path; two-second acquisition deadline; synchronous transaction preserves mutex thread affinity.
- Testing: executable contract harness, temp directories, real child workers for concurrent updates and real two-target Windows integration. No product UI or network.

## Constitution Check

All ten principles pass before and after design: binding operations do not launch; only owned state is written; scene and target identities remain separate; F04/F05 remain launch authorities; no format change/new dependency/license; errors and concurrency are covered; feature has its own artifacts and verified commit gate.

## Architecture and Design Decisions

1. Add `IStudioStateStore.Load/Update`; `StudioStateRepository.Update` locks, loads/validates, invokes one transition, validates and atomically saves. Returning the identical state reference means no write/backup rotation. Callbacks must not mutate input lists and exceptions propagate before any persistence.
2. `Save` and `Update` share a path-keyed named mutex (`Local` namespace on Windows; SHA-256 of canonical case-normalized path). Busy is an additive repository error. Abandoned ownership proceeds only through a fresh validated load. Save is snapshot replacement, never a merge; editing clients must use Update.
3. `BindingService` takes `IStudioStateStore`, `Func<SceneCatalogScanResult>`, `Func<ApplicationProfile, LaunchTarget?>`, and optional target-validation delegate (default F04 resolver). The provider uses the supplied current profile. Provider exceptions fail with TargetUnavailable/SceneUnavailable and never persist.
4. New binding uses random ID. Reassign/enable/disable retains ID. Case-insensitive app lookup matches F02. New state uses array copies. Disable/remove do not consult unavailable external dependencies.
5. `Resolve` precedence: invalid request → storage failure → missing app → unbound → disabled → invalid typed target → unavailable scene → ready context. No non-ready result includes a launch context. Ready means current structural eligibility, not guaranteed future activation or AUMID installation.
6. Resolve scans catalog again every call; duplicate IDs fail closed even if an injected provider incorrectly marks both available. Scene display/version comes from current catalog manifest.

## Project Structure

```text
src/DesktopMotionStudio.Core/State/StudioState.cs             # IStudioStateStore, Busy
src/DesktopMotionStudio.Core/State/StudioStateRepository.cs   # Update + shared write lock
src/DesktopMotionStudio.Core/Bindings/BindingContracts.cs
src/DesktopMotionStudio.Core/Bindings/BindingService.cs
tests/DesktopMotionStudio.Core.Checks/F07Checks.cs            # domain/storage/worker/integration checks
tests/DesktopMotionStudio.Core.Checks/Program.cs              # F07 worker/runner dispatch
specs/008-cross-app-binding/{spec,plan,tasks,research,data-model,quickstart,evidence,review}.md
```

## Validation Strategy

First add domain/transaction contracts. Implement repository transaction then binding stories incrementally. Exercise duplicate/no-op, ID stability, target types and failures, catalog failures, read-only resolution, unknown/corrupt/oversized storage, I/O failure and two-process contention. Real Windows fixtures copy complete POC output to two unique directories, bind one shared scene, resolve targets, then use F06/F05 to confirm distinct target PID/HWND without any target encoded in scene. Build/format/full regressions, independent review pass, Spec Kit Analyze and Converge before completion.

## Complexity Tracking

No new schema or dependency. The target provider is the explicit seam to F08; it avoids introducing an ambiguous string format. Cross-process locking is required by FR-007 and is not a promise to merge stale whole-state Save calls.
