# Implementation Plan: 版本化场景清单

**Branch**: `main` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

## Summary

Create a dependency-free .NET 10 core library that parses UTF-8 JSON under a fixed size limit, validates schema and asset paths, and returns structured errors. Keep the parser free of file I/O so F03 can control trusted scene discovery.

## Technical Context

**Language/Version**: C# / .NET 10
**Dependencies**: `System.Text.Json` only
**Storage**: None
**Testing**: focused executable contract checks for valid and invalid manifests
**Target**: Windows 11 x64 product; library itself portable

## Constitution Check

- No user code execution, network, or disk access: pass by API design.
- Versioned contract and unknown-version rejection: required.
- Stable scene ID independent of display name: required.
- Tests cover each validation branch: required before convergence.

## Design

- `SceneManifestReader.Read(ReadOnlySpan<byte>)` returns `SceneManifestReadResult` with either immutable manifest or `SceneManifestError`.
- Deserialize a DTO with `JsonSerializer`, then validate in a separate method. Reject duplicate JSON property names only if they affect contract identity; otherwise fail-safe if parser cannot guarantee semantics.
- Accept only schema version 1. Future versions reject without modifying data. Version 0 or negative is invalid.
- Validate asset path components independent of host OS path separator; no absolute, parent traversal, empty, or duplicate case-insensitive path.

## Project Structure

```text
src/DesktopMotionStudio.Core/
  DesktopMotionStudio.Core.csproj
  Scenes/SceneManifest.cs
  Scenes/SceneManifestReader.cs
tests/DesktopMotionStudio.Core.Checks/
  DesktopMotionStudio.Core.Checks.csproj
  Program.cs
```

## Validation

Release build, analyzers and format check, then run contract checks. Review against each F01 requirement and record evidence before proceeding to F02.
