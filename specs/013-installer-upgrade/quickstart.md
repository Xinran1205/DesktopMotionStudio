# F12 Verification Guide

Run from the repository root with the pinned SDK on PATH:

```powershell
$out = Join-Path $PWD 'artifacts/f12-local-payload'
& .\installer\Build-Payload.ps1 -Version 0.12.1 -OutputDirectory $out
& .\specs\013-installer-upgrade\probes\Test-Payload.ps1 -OutputDirectory $out
& .\specs\013-installer-upgrade\probes\Test-Selector.ps1
dotnet publish src/DesktopMotionStudio.Entry -c Release -r win-x64 --self-contained true -o (Join-Path $out 'entry') `
    -p:Version=0.12.1 -p:RuntimeFrameworkVersion=10.0.14 -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None
dotnet run --project tests/DesktopMotionStudio.Core.Checks -c Release -- --f12-stage-install $out (Join-Path $out 'entry/DesktopMotionStudio.Entry.exe')
```

The output directory must be new. These commands build an offline payload and fixed entry, then verify program publication and activation under an isolated temporary root. See [evidence.md](evidence.md) for the verified scope.

## Full installation acceptance

Build two unsigned validation packages on the development host:

```powershell
$a = Join-Path $PWD 'artifacts/f12-check-a'
$b = Join-Path $PWD 'artifacts/f12-check-b'
& .\installer\Build-Setup.ps1 -Version 0.12.7 -OutputDirectory $a
& .\installer\Build-Setup.ps1 -Version 0.12.8 -OutputDirectory $b
dotnet run --project tests/DesktopMotionStudio.Core.Checks -c Release -- --f12-built-lifecycle $a $b
```

The isolated lifecycle check uses the published payloads and the production coordinator with a temporary installation root and a dedicated test registry key.

For a normal install, the program path is `%LOCALAPPDATA%\Programs\DesktopMotionStudio`. The current-user data root is `%LOCALAPPDATA%\DesktopMotionStudio`. Use Windows **Installed Apps** to uninstall. User state, backups, diagnostics and unresolved shortcut evidence remain under the data root. Edited or unverified files are reported as retained. If removal is interrupted, Setup can resume the same recorded removal, then a second run can install into an empty program slot. The detached uninstaller keeps one digest-verified work copy per entry binary in the user's temporary directory. An interrupted copy uses a private stage and cannot replace the runnable helper.

## Workstation runbook

Run on Windows 11 x64 in an ordinary non-elevated interactive session. Compare each candidate's SHA256 with `setup.sha256`. Use Explorer or a native Windows PowerShell window. Before starting a test installation, verify that the program, data, Start menu and uninstall slots are empty. Leave existing user files and cloud-synced desktops alone. Use a local test target and a local Shell Link with the same installed Launcher path and binding argument as a managed desktop entry.

1. **Install and launch.** Run Setup and check the current-user version registration. Open Studio from Start and confirm its runtime loads from the installation. Close Studio, open the local test link through Shell and confirm one target window plus the expected animation outcome. Repeat with the test installation's MotionHost image temporarily unavailable, then restore its verified bytes. Check the busy case while Studio is running.
2. **Upgrade and rollback.** Run the two-package lifecycle command above to force rollback after retiring the old selector, retry and refuse the stale installer. Compare stable paths, entry hashes, links and profile/backup bytes. Use the real workstation upgrade and the isolated tests together. If entry replacement is interrupted, rerun the same Setup.
3. **Uninstall and reinstall.** Run the registered uninstall command for the test installation. Check that registration, program and Start menu slots disappear and that profile, backup and diagnostics remain byte-for-byte intact. Run the lifecycle check above and the focused cleanup checks below for reinstall, owned link removal and conflicts.

```powershell
dotnet run --project tests/DesktopMotionStudio.Core.Checks -c Release -- --f12-cleanup-checks
dotnet run --project tests/DesktopMotionStudio.Core.Checks -c Release -- --f12-uninstall-checks
```

Record the actual account token, network conditions, loaded runtime and which cases used isolated fixtures. The historical `Test-GuestPrerequisites.ps1` remains available for additional compatibility testing. A separate VM is optional.
