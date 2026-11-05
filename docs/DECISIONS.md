# Architecture decisions

## ADR-001 — Isolate window-host feasibility before UI implementation

**Date:** 2026-11-03 · **Status:** Accepted for F00

The PRD recommends WinUI 3 for Studio. The TDD marks transparent top-level rendering with Composition, Win2D, video, and DWM as unverified and explicitly allows a separate MotionHost implementation. F00 therefore validates window lifecycle through a small Windows Forms/Win32 prototype before selecting a production renderer. This does not decide the final renderer or replace the Studio UI choice.

**Reason:** The first release must favor reliable target-app launch. Window focus, DPI, transparency, and cleanup are higher-risk prerequisites than application management UI. Windows Forms is included with the .NET Windows desktop SDK, so this check needs no additional rendering package.

**Consequences:** The prototype is disposable or may be adapted into the MotionHost only after interactive desktop evidence. Composition, video, DWM thumbnail handoff, and renderer process isolation still require later POCs and features. A successful build alone does not establish visual correctness.

## ADR-002 — Treat original PRD/TDD as baselines

**Date:** 2026-11-03 · **Status:** Accepted

The original V1.0 files remain intact for traceability. The Constitution, feature specs, plans, and ADRs carry refinements. Before a release, compatibility claims and dependency versions from the 2026-11-02 TDD must be checked against official current sources and actual test results. The TDD's "packaged/unpackaged" language is a recommendation, not a settled deployment contract.

## ADR-003 — Use WPF for Studio management UI

**Date:** 2026-11-04 · **Status:** Accepted for F08; desktop validation passed 2026-11-05

Studio uses WPF on the pinned .NET 10 Desktop SDK. The management surface is accessible forms/lists; MotionHost remains an isolated WinForms process. WPF avoids an additional Windows App SDK deployment dependency and fits the existing .NET runtime/tooling. Microsoft currently recommends WinUI 3 for new desktop applications; this project-specific decision prioritizes the existing runtime, testability, and deployment simplicity. No renderer or product behavior changes follow from it.

References: [Desktop SDK WPF support](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop), [Microsoft framework guidance](https://learn.microsoft.com/en-us/windows/apps/whats-new/coming-to-windows). The original PRD/TDD remain unchanged as baselines; actual keyboard/DPI behavior must be verified in F08.

## Change history

| Date | Change |
|---|---|
| 2026-11-03 | Record initial architecture and document-governance decisions. |
| 2026-11-04 | Select WPF for F08 Studio with explicit runtime and validation tradeoffs. |
| 2026-11-05 | Verify WPF Studio at actual 100/125/150% scaling; refine F02 schema migration and F04 local-only path capability through F08 specifications and regression evidence. |
