# F02 verification evidence

**Status:** Passed · **Date:** 2026-11-04

- `dotnet run --project tests/DesktopMotionStudio.Core.Checks -c Release`: 25 checks passed, including 10 repository checks.
- Repository checks covered default state, app/binding/settings round trip, prior state backup, backup rotation, future schema rejection and byte preservation, corrupt JSON rejection and byte preservation, broken app reference, duplicate binding, concurrent saves, and files larger than 4 MiB.
- `dotnet format src/DesktopMotionStudio.Core --verify-no-changes`: passed.
- `dotnet format tests/DesktopMotionStudio.Core.Checks --verify-no-changes`: passed.
- `dotnet build src/DesktopMotionStudio.Core -c Release`: passed with 0 warnings and 0 errors.
- Tests use unique temporary directories and remove only those directories after completion.
