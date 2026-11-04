# Implementation Plan: 应用启动适配器

**Branch**: `main` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

## Summary

Implement a typed launch contract with three Windows adapters: direct EXE, resolved EXE shortcut, and AUMID activation. Return structured status, optional PID, and native error data. Keep all shell arguments out of user-authored command strings.

## Technical Context

**Language/Version**: C# / .NET 10 Windows x64
**Dependencies**: BCL `ProcessStartInfo`, Windows Shell Link COM, `IApplicationActivationManager`
**Storage**: None
**Testing**: fake adapter contract checks plus bounded Windows integration tests
**Target**: Windows 11 x64

## Constitution Check

- Least privilege; no global hooks or injection: pass.
- Typed inputs; no arbitrary shell command: required.
- User arguments passed without manual concatenation: required.
- F04 does not do window discovery/handoff: enforce boundary.

## Design

- Model launch target as a discriminated union (`ExecutableTarget`, `ShortcutTarget`, `PackagedTarget`).
- Adapter dispatcher rejects unsupported target kinds before any side effect.
- EXE adapter validates file and argument limits, then starts exactly once with a separate argument list.
- Shortcut adapter resolves link data through COM, validates EXE target, then delegates to EXE adapter with link arguments/working directory.
- Packaged adapter calls `ActivateApplication` and preserves HRESULT/PID.
- Catch and translate only expected IO, permission, Win32, COM, and process-start failures.
- COM instances release deterministically; no unmanaged COM object escapes the adapter.

## Project Structure

```text
src/DesktopMotionStudio.Core/Launch/
  LaunchRequest.cs
  ApplicationLauncher.cs
  ExecutableLaunchAdapter.cs
  ShellLinkLaunchAdapter.cs
  PackagedAppLaunchAdapter.cs
tests/DesktopMotionStudio.Core.Checks/Program.cs
```

## Validation

Contract checks use fake target/process seams for deterministic errors and argument integrity. Windows integration checks launch a known benign executable and a generated test `.lnk`; AUMID activation uses a known installed test app if available. Never claim a generic AUMID success without a real package test.
