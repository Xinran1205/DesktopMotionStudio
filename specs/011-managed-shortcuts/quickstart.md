# F10 Validation Guide

Use the SDK in global.json and a normal Windows 11 x64 session. On the development workstation set DOTNET_ROOT to C:\Users\AAA\.dotnet and use that directory's dotnet.exe. Keep temporary desktop, receipts and state isolated.

1. Run the native ownership proof in the Core checks, covering occupied destination, file ID, edits, hard links, symlinks, and verified delete.
2. Build Core checks, Studio checks and Studio Release. Run the F10 contract checks and complete existing regressions.
3. Publish Studio to a fresh win-x64 framework-dependent directory. Verify Launcher and MotionHost dependency files and Scenes. Use isolated state and desktop fixtures. In a disposable Windows user session, exercise the production known-folder defaults where available.
4. Use the keyboard to create an entry from an enabled binding. Repeat, close Studio, activate the actual .lnk, observe target and renderer, replace the binding, retest and then disable motion and retest native launch.
5. Exercise missing-file repair, same-name collision, edited-file refusal and owned removal. Compare state/backup and unrelated file bytes, and record process cleanup.
6. Record exact commands, environment, PASS/FAIL/SKIP counts and unverified limitations in evidence.md.

### Reproduce the verified isolated flow

```powershell
$env:DOTNET_ROOT = 'C:\Users\AAA\.dotnet'
$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$publish = Join-Path $env:TEMP ("dms-f10-publish-" + [Guid]::NewGuid().ToString('N'))
& $dotnet publish .\src\DesktopMotionStudio.Studio -c Release -r win-x64 --self-contained false -o $publish
& $dotnet build .\tests\DesktopMotionStudio.Launcher.Fixture -c Release -r win-x64
& $dotnet build .\tests\DesktopMotionStudio.Studio.Checks -c Release
$env:DMS_F08_STUDIO_PATH = Join-Path $publish 'DesktopMotionStudio.Studio.exe'
$env:DMS_F06_TARGET_PATH = (Resolve-Path .\src\DesktopMotionStudio.WindowHost.Poc\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.WindowHost.Poc.exe).Path
$env:DMS_F10_FIXTURE_LAUNCHER_PATH = (Resolve-Path .\tests\DesktopMotionStudio.Launcher.Fixture\bin\Release\net10.0-windows\win-x64\DesktopMotionStudio.Launcher.exe).Path
& $dotnet .\tests\DesktopMotionStudio.Studio.Checks\bin\Release\net10.0-windows\DesktopMotionStudio.Studio.Checks.dll --f10-ui
```

Build the WindowHost POC first if it is absent. The check uses a unique temp subtree and launches a copy of the publish. The test-only Launcher links production source and reads only the explicit fixture profile. It is excluded from shipping output. The shipping command accepts only the binding token. Studio accepts an isolated desktop override only alongside an existing fixture profile in a dedicated dms-f10 temp directory. For full Core regression, set DMS_F06_HOST_PATH to the published MotionHost and use the same DMS_F06_TARGET_PATH.
