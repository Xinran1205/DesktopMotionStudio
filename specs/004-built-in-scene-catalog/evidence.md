# F03 verification evidence

**Status:** Passed · **Date:** 2026-11-04

- Automated contract harness passed 32 checks across F01, F02, and F03.
- F03 cases covered missing root, deterministic ordering, valid nested resource, duplicate IDs, missing resource, missing manifest, malformed manifest, and a parent traversal reference. The valid scene remained available alongside unrelated invalid entries.
- `dotnet format src/DesktopMotionStudio.Core --verify-no-changes`: passed.
- `dotnet format tests/DesktopMotionStudio.Core.Checks --verify-no-changes`: passed.
- `dotnet build src/DesktopMotionStudio.Core -c Release`: passed with 0 warnings and 0 errors.
