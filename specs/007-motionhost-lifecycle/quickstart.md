# F06 Quickstart

## Build and run contract checks

From the repository root in Windows PowerShell:

```powershell
dotnet build .\src\DesktopMotionStudio.Core\DesktopMotionStudio.Core.csproj -c Release
dotnet build .\src\DesktopMotionStudio.MotionHost\DesktopMotionStudio.MotionHost.csproj -c Release -r win-x64
dotnet build .\tests\DesktopMotionStudio.Core.Checks\DesktopMotionStudio.Core.Checks.csproj -c Release
dotnet run --project .\tests\DesktopMotionStudio.Core.Checks\DesktopMotionStudio.Core.Checks.csproj -c Release --no-build
dotnet format .\src\DesktopMotionStudio.Core\DesktopMotionStudio.Core.csproj --verify-no-changes --no-restore
dotnet format .\src\DesktopMotionStudio.MotionHost\DesktopMotionStudio.MotionHost.csproj --verify-no-changes --no-restore
```

## Windows process integration

Build MotionHost and the existing WindowHost POC. Set `DMS_F06_HOST_PATH` to the built MotionHost executable and `DMS_F06_TARGET_PATH` to the built WindowHost POC executable, then run the harness. The integration case uses a unique executable identity, starts the MotionHost before dispatch, waits for the POC target window, requests fade handoff, and closes only the test POC with `WM_CLOSE`.

```powershell
$env:DMS_F06_HOST_PATH = (Resolve-Path .\src\DesktopMotionStudio.MotionHost\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.MotionHost.exe).Path
$env:DMS_F06_TARGET_PATH = (Resolve-Path .\src\DesktopMotionStudio.WindowHost.Poc\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.WindowHost.Poc.exe).Path
dotnet run --project .\tests\DesktopMotionStudio.Core.Checks\DesktopMotionStudio.Core.Checks.csproj -c Release --no-build
```

If either path is unset or unavailable, only that optional desktop process integration is reported as skipped; all deterministic lifecycle checks still run.

## Manual interactive acceptance

Build the harness and set the two executable paths as in the process-integration section. Run the maintained desktop acceptance mode from an interactive Windows session:

```powershell
$env:DMS_F06_HOST_PATH = (Resolve-Path .\src\DesktopMotionStudio.MotionHost\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.MotionHost.exe).Path
$env:DMS_F06_TARGET_PATH = (Resolve-Path .\src\DesktopMotionStudio.WindowHost.Poc\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.WindowHost.Poc.exe).Path
dotnet .\tests\DesktopMotionStudio.Core.Checks\bin\Release\net10.0\DesktopMotionStudio.Core.Checks.dll --f06-desktop-acceptance
```

The mode injects Esc and a real mouse click into the MotionHost window, terminates an owned test host to verify crash cleanup, verifies full and centered placement, taskbar-excluded window styles, DPI, close latency, fade or reduced-motion behavior for the current Windows preference, and target survival. It does not change system settings. Run once with Windows **Settings → Accessibility → Visual effects → Animation effects** enabled and once disabled to capture both paths; restore the user's starting preference after the manual runs. Each run records process/window IDs and timings to stdout.

The full contract harness also verifies the 750 ms watchdog, abrupt parent-disconnect cleanup, managed target handoff, and F00–F05 regressions.

Do not use this lifecycle host as evidence that video, GPU effects, licensed scene assets, DWM transitions, packaged target apps, or all Windows display configurations are supported.
