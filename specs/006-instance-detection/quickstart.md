# F05 validation guide

## Prerequisites

- Windows 11 x64 interactive desktop.
- .NET SDK 10.0.403 from `global.json`.
- Build the benign WindowHost POC and the Core contract harness.

## Commands

```powershell
dotnet build .\src\DesktopMotionStudio.WindowHost.Poc\DesktopMotionStudio.WindowHost.Poc.csproj -c Release
dotnet run --project .\tests\DesktopMotionStudio.Core.Checks\DesktopMotionStudio.Core.Checks.csproj -c Release
```

To run the installed packaged-app integration, first ensure the target is not already running, then set its AUMID for this test process:

```powershell
$env:DMS_F05_TEST_AUMID = 'Microsoft.Paint_8wekyb3d8bbwe!App'
dotnet run --project .\tests\DesktopMotionStudio.Core.Checks\DesktopMotionStudio.Core.Checks.csproj -c Release
Remove-Item Env:DMS_F05_TEST_AUMID
```

The integration skips when the exact AUMID already has a running process. It launches only the requested app, then closes the returned test window normally.

For the real-window test, the harness launches `DesktopMotionStudio.WindowHost.Poc.exe`, confirms its process/window identity and activation result, then closes that test window gracefully. It must not enumerate or activate unrelated windows.

## Expected outcomes

- Existing exact-match window: one activation attempt; F04 dispatch count remains zero.
- No match: one F04 dispatch; a matching window returns `WindowReady` before 15 seconds.
- Delayed/missing window, process exit, PID reuse, access denial, or foreground denial: the corresponding structured result; no target process is killed.
- Same AppId concurrent requests: exactly one F04 dispatch; subsequent request rechecks and reuses the resulting process/window.

The test harness uses fakes for timeout, ambiguity, process exit, and denied foreground behavior so those cases do not depend on timing or desktop focus policy.
