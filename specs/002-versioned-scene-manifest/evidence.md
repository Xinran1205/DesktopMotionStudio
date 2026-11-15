# F01 verification evidence

**Status:** Passed · **Date:** 2026-11-04

- SDK: .NET 10.0.403, Windows x64.
- `dotnet run --project tests/DesktopMotionStudio.Core.Checks -c Release`: 15 contract checks passed.
- Covered valid manifest, future/invalid/missing schema, malformed and duplicate JSON properties, missing field, unsafe and duplicate asset path, invalid ID/type, SemVer pre-release leading zero, build metadata hyphen, and exact/over 1 MiB boundaries.
- `dotnet format tests/DesktopMotionStudio.Core.Checks --verify-no-changes`: passed.
- `dotnet build src/DesktopMotionStudio.Core -c Release`: passed with 0 warnings and 0 errors.
- Review found no disk/network access or execution path in the manifest reader.
