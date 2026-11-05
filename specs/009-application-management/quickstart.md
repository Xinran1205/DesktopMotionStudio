# F08 Validation Guide

Build the pinned .NET projects and run the existing executable check harness. F08 contract checks use temporary configuration/catalog paths; full regression keeps the F06/F07 target-path environment variables from their quickstarts.

```powershell
# Use an installed .NET SDK matching global.json. This workstation's pinned SDK:
$dotnet = 'C:\Users\AAA\.dotnet\dotnet.exe'
$env:DOTNET_ROOT = Split-Path $dotnet
& $dotnet build .\src\DesktopMotionStudio.Studio -c Release -r win-x64
& $dotnet build .\tests\DesktopMotionStudio.Core.Checks -c Release
& $dotnet build .\tests\DesktopMotionStudio.Studio.Checks -c Release
$env:DMS_F08_STUDIO_PATH = (Resolve-Path .\src\DesktopMotionStudio.Studio\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.Studio.exe).Path
& $dotnet .\tests\DesktopMotionStudio.Core.Checks\bin\Release\net10.0\DesktopMotionStudio.Core.Checks.dll --f08-checks
& .\tests\DesktopMotionStudio.Studio.Checks\bin\Release\net10.0-windows\DesktopMotionStudio.Studio.Checks.exe

# Actual interactive acceptance; complete target output is copied to owned fixtures.
$env:DMS_F06_TARGET_PATH = (Resolve-Path .\src\DesktopMotionStudio.WindowHost.Poc\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.WindowHost.Poc.exe).Path
& .\tests\DesktopMotionStudio.Studio.Checks\bin\Release\net10.0-windows\DesktopMotionStudio.Studio.Checks.exe --ui
```

Build the POC first if its output is absent, as described in the F06 quickstart. If Windows denies foreground focus, set `DMS_F08_WAIT_FOR_FOCUS=1`, start UI acceptance, then activate the test-owned Studio window while it waits. Do not activate unrelated windows during SendInput acceptance. The default worker checks also support `dotnet ...Studio.Checks.dll`; unknown test arguments fail with exit 2.

Studio normal launch uses current-user local state. UI acceptance must launch with an explicit isolated `--state-file <absolute fixture file>` and optional `--catalog-root <absolute fixture root>` so add/repair/remove cannot alter real user state. No custom state path is exposed in ordinary product UI.

Acceptance: add a local POC executable without launching it; close/reopen and confirm saved target; filter saved list; inspect details; rename/repair with stable IDs; cancel then confirm remove; exercise unavailable targets/config failures. Repeat keyboard/scaling checks at 100/125/150% actual available desktop settings and restore starting settings. Record any missing evidence instead of marking that task complete.

For ordinary development use, run the built Studio executable. Alt+A adds, Alt+S saves a name/entry, Alt+R confirms or changes the target, Alt+D requests removal, and Esc cancels the editor before saving starts. Saving completes before the editor can close. Legacy entries require explicit target confirmation; configuration errors disable edits and show recovery text. This build has no installer yet. Application/scene selection and managed shortcuts arrive in F09/F10.
