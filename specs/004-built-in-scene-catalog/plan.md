# Implementation Plan: 内置场景库

**Branch**: `main` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

## Summary

Implement a read-only filesystem catalog over a supplied trusted root. It loads immediate scene folders through the F01 manifest reader, verifies referenced files stay within each folder and are not reparse points, and returns per-scene errors without aborting the scan.

## Technical Context

**Language/Version**: C# / .NET 10
**Dependencies**: F01 core manifest reader and BCL filesystem APIs
**Storage**: read-only packaged files
**Testing**: temporary directory contract checks
**Target Platform**: Windows 11 x64

## Constitution Check

- Trusted packaged input and no user code execution: pass.
- No writes, network, or application launch: pass.
- File path containment and reparse-point rejection: required.
- One corrupt entry cannot hide other valid scenes: required.

## Design

- `BuiltInSceneCatalog.Scan(root)` returns deterministic items sorted by folder name.
- Each item has scene directory, optional parsed manifest, validation status and diagnostic code.
- Enumerate only direct child folders; reject reparse-point scene folders and manifests.
- Resolve every asset against the scene root, enforce canonical containment using Windows case-insensitive path comparison, and reject reparse points along each path component.
- After scanning, mark all instances of duplicate `sceneId` as invalid.
- Catch expected IO/access failures per folder and keep scanning.

## Project Structure

```text
src/DesktopMotionStudio.Core/Scenes/BuiltInSceneCatalog.cs
tests/DesktopMotionStudio.Core.Checks/Program.cs
```

## Validation

Checks cover stable ordering, valid resources, missing manifest/asset, bad manifest, duplicate IDs, outside-root paths and resilience across neighboring entries.
