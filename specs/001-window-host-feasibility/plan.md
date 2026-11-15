# Implementation Plan: Windows 动效宿主可行性

**Branch**: `main` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

## Summary

Build a standalone Windows x64 prototype that creates a per-monitor, DPI-aware top-level window in full or centered mode. Draw a simple visual and support Esc/button exit. Record build and real desktop evidence separately.

## Technical Context

**Language/Version**: C# / .NET 10
**Primary Dependencies**: Windows Forms (Win32 window wrapper); no NuGet dependency for the prototype
**Storage**: None
**Testing**: build/format plus interactive Windows acceptance checklist
**Target Platform**: Windows 11 x64
**Project Type**: standalone desktop executable
**Performance Goal**: window appears without long blocking; measure before fixing budget
**Constraint**: ordinary user permission, no global hooks or third-party app mutation

## Constitution Check

- Launcher/target app untouched: pass by prototype isolation.
- Least privilege and local-only: pass by design.
- Failure cleanup and compatibility: require real desktop verification before convergence.
- Versioned contracts: not applicable; no persistent data or IPC in F00.

## Decision

Use a minimal Windows Forms top-level window for F00. Windows Forms exposes a Win32 layered window through `TransparencyKey` and provides monitor/DPI handling without native interop. The TDD flags transparent WinUI 3 plus mixed media as unverified and permits an independent MotionHost implementation. This POC isolates the windowing risk without adding Windows App SDK packages. A later experiment can layer Composition/Win2D once the window lifecycle works.

## Project Structure

```text
src/DesktopMotionStudio.WindowHost.Poc/
  DesktopMotionStudio.WindowHost.Poc.csproj
  Program.cs
specs/001-window-host-feasibility/
  spec.md
  plan.md
  tasks.md
  evidence.md
```

## Validation

1. Build Release win-x64 and run format analysis.
2. Run invalid argument smoke check and inspect exit status.
3. On interactive Windows desktop, run full and centered modes; check Esc/button, transparency, focus, monitor geometry and DPI cases.
4. Record results in evidence.md. Do not mark F00 complete from build alone.
