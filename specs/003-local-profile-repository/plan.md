# Implementation Plan: 本地状态仓库

**Branch**: `main` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

## Summary

Extend the core library with user-state models and a local repository that validates state, reads/writes schema v1 JSON, and preserves the last valid state during failures. Provide a production path factory and injectable path for tests.

## Technical Context

**Language/Version**: C# / .NET 10
**Dependencies**: `System.Text.Json`, BCL filesystem APIs
**Storage**: `%LOCALAPPDATA%/DesktopMotionStudio/state/studio-state.json`
**Testing**: executable contract checks using isolated temporary directories
**Target Platform**: Windows 11 x64

## Constitution Check

- Ordinary user profile and no network: pass.
- Atomic writes and future schema protection: required.
- No third-party app/shortcut side effects: pass by boundary.
- Tests for corrupted input, duplicate/reference rules, reload and backup: required.

## Design

- `StudioState` holds `SchemaVersion`, applications, bindings, and settings.
- `StudioStateRepository.Load()` returns state/error; absent file returns defaults.
- `Save(state)` validates, writes UTF-8 JSON to a random sibling temporary file, flushes, then atomically moves/replaces; on replace it rotates one `.bak` file.
- Keep storage path injectable; production factory derives from `Environment.SpecialFolder.LocalApplicationData`.
- This version reads only schema 1. Future schema is never rewritten.

## Project Structure

```text
src/DesktopMotionStudio.Core/State/
  StudioState.cs
  StudioStateRepository.cs
tests/DesktopMotionStudio.Core.Checks/Program.cs
```

## Validation

Run contract harness for defaults, round-trip, corruption/future schema preservation, model validation, backup, and invalid path behavior; run Release build and format verification.
