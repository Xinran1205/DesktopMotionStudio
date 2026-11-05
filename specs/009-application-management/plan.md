# Implementation Plan: My applications

**Branch**: `codex/f08-application-management` · **Date**: 2026-11-04 · **Spec**: [spec.md](spec.md)

## Summary

Introduce schema-2 typed application targets with a non-writing schema-1 migration, a transaction-based ApplicationRegistry, bounded local discovery, and the first WPF Studio application. Reuse F04 validation and F07 binding operations. Do not implement scene-preview UI or shortcuts here.

## Technical Context

- C# / .NET SDK 10.0.403; WPF on Windows x64, no UI NuGet dependencies; nullable/analyzers/warnings as errors.
- Core remains desktop-independent except existing Windows target validation. New `Applications` domain lives in Core; WPF and discovery COM/process orchestration live in Studio.
- Schema 2 adds optional typed target to ApplicationProfile while preserving its legacy string. Old data is validated and projected in memory; reads/no-ops never write. Changed transactions retain the original file as backup.
- Current-user discovery runs in a strict worker mode of the Studio executable. Parent enforces 5s timeout, cancellation, at most 1 MiB stdout, max 2000 candidates, and kills only its own worker on deadline. No user text is evaluated as a command.
- UI uses standard accessible WPF controls, async load/mutations off the UI thread, keyboard mnemonics/tab order and scrollable flexible content. Core and presentation logic remain testable separately.

## Constitution Check

All ten principles pass: add/repair do not launch; discovery reads only local launch metadata; only owned versioned state mutates; Core/Studio/discovery boundaries are explicit; unsupported targets/configs remain recoverable; migration is deterministic/backed-up; no new third-party runtime; verified tasks and commit gate required. Actual Windows/DPI evidence remains mandatory and may not be inferred from a successful build.

## Design Decisions

1. **WPF Studio, separate MotionHost.** The management UI needs mature accessible forms, not renderer APIs. WPF uses the installed Desktop SDK/runtime; selecting it avoids an additional Windows App SDK deployment dependency. Microsoft recommends WinUI 3 for new apps; this project-specific tradeoff is recorded in ADR-003. T001 must prove build/show/close before downstream UI work.
2. **Explicit typed metadata.** ApplicationTargetDefinition stores textual kind, identifier, argument vector, optional working directory. No legacy string type inference. F07 provider reads only Target; legacy null Target means repair required.
3. **Migration without unsolicited writes.** Inspect schema version before deserializing new fields. Reject duplicate/unknown members, unsupported versions and malformed targets without replacing bytes. Valid schema 1 becomes an in-memory schema 2 snapshot with null targets. Changed Update writes schema 2 and backs up exact old bytes; same-reference no-op leaves schema 1 on disk.
4. **Atomic registry commands.** Add/Rename/Repair/Remove patch latest state through Update. Duplicates use F04 resolved executable path + ordinal arguments + working-directory identity, or exact AUMID. Rename/repair retain IDs; remove app and binding together. No discovery or F04 launch is performed while saving UI state.
5. **Discovery trust boundary.** Enumerate Start Menu launch links and explicitly identified packaged AppsFolder entries; validate all candidates through F04 before registration. Worker is disposable and bounded. Failed/cancelled/empty discovery never blocks manual selection.
6. **UI ownership.** Main shows saved profiles only. Add dialog separates discovery search from manual file selection; repair explicitly chooses target type. Details shows target identity and binding status. Removal confirms intent, then performs one registry transaction. Binding toggle uses F07; no optimistic success before disk result.
7. **Testability.** Commands/registry and migration use isolated state/catalog fixtures. Worker protocol has bounded decoding checks. Windows UI acceptance uses a dedicated temporary state path and POC target, confirming add/restart/details/rename/repair/remove and no accidental target launch. Scaling is verified at actual available desktop scales; absent evidence remains an open task.

## Structure

```text
src/DesktopMotionStudio.Core/Applications/{ApplicationTargetDefinition,ApplicationRegistry}.cs
src/DesktopMotionStudio.Core/State/{StudioState,StudioStateRepository}.cs
src/DesktopMotionStudio.Studio/{Program,MainWindow,ApplicationEditorWindow,DiscoveryWorker,DiscoveryClient}.*
tests/DesktopMotionStudio.Core.Checks/F08Checks.cs
specs/009-application-management/{spec,plan,tasks,research,data-model,quickstart,evidence,review}.md
specs/009-application-management/contracts/{application-registry,state-v2,discovery-worker}.md
```

## Validation

Prove the UI/discovery spikes, write migration and registry contract checks first, then implement US1/US4 foundations, US2 discovery and US3 management. Run F00–F08 regressions with previous real target integration, build/format/analyzers, record actual Windows UI evidence, independent review and Spec Kit Analyze/Converge. Do not close F08 while required UI/scaling acceptance remains unverified.

## Complexity Tracking

Schema 2 is necessary for target type/argument preservation. Legacy raw data remains recoverable. WPF/discovery worker are separate from Core; no renderer changes or arbitrary scene code enter F08.
