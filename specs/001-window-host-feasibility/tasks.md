# Tasks: Windows 动效宿主可行性

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Setup

- [x] T001 Install a complete local .NET 10 SDK and pin usable feature band in `global.json`.
- [x] T002 Create `src/DesktopMotionStudio.WindowHost.Poc/DesktopMotionStudio.WindowHost.Poc.csproj` for Windows x64.

## US1: Safe display and exit

- [x] T003 Implement argument validation and monitor work-area selection in `Program.cs`.
- [x] T004 Implement top-level layered window, simple visual, Esc/button close and resource cleanup in `Program.cs` (runtime behavior still awaiting verification).
- [x] T005 Build and run invalid-argument smoke check; record results in `evidence.md`.

## US2: Compatibility and failure cases

- [x] T006 Run full/centered interactive desktop checks, including focus and transparency.
- [x] T007 Run single/dual monitor and two DPI checks; capture actual OS/build results in `evidence.md`.
- [x] T008 Review implementation against Constitution and spec; record convergence decision and next technical action.
