# F04 verification evidence

**Status:** Passed · **Date:** 2026-11-04

## Environment

- Windows 11 x64, build 26200.
- .NET SDK 10.0.403.
- AUMID integration target: installed Microsoft Paint package.

## Automated checks

- The executable contract harness passed all 47 checks across F01–F04; 16 checks exercise F04.
- F04 covered direct EXE launch, missing EXE, invalid working directory, missing and non-EXE shortcuts, malformed/embedded-NUL/oversized AUMIDs, argument count and command-line limits, permission-error mapping, EXE-targeting shortcut resolution, direct and shortcut argument preservation (including spaces and embedded quotes), single dispatch, installed AUMID activation, and a missing app ID within an installed package returning an HRESULT.
- `dotnet format src/DesktopMotionStudio.Core --verify-no-changes`: passed.
- `dotnet format tests/DesktopMotionStudio.Core.Checks --verify-no-changes`: passed.
- `dotnet build src/DesktopMotionStudio.Core -c Release`: passed with 0 warnings and 0 errors.

## Windows integration and cleanup

- A benign `cmd.exe` target and a generated `.lnk` to that executable were started successfully.
- A generated `.lnk` targeting a text file was rejected as `InvalidTarget`.
- AUMID activation returned `Activated` for installed Microsoft Paint. The opened window was closed after the check; a subsequent `list_windows` inspection showed only the Codex window.
- Permission mapping was tested with an injected process starter throwing `UnauthorizedAccessException`; no permission or system setting was changed.

## Limits

- The packaged-app integration check covers one installed AUMID on this machine. Other packaged app families and app-specific activation behavior remain subject to later compatibility testing.
- Main-window discovery, foreground activation, and readiness timeouts are outside F04 and belong to F05.
