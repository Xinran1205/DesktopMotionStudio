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
$a = Join-Path $PWD 'artifacts/f12-vm-a'
$b = Join-Path $PWD 'artifacts/f12-vm-b'
& .\installer\Build-Setup.ps1 -Version 0.12.7 -OutputDirectory $a
& .\installer\Build-Setup.ps1 -Version 0.12.8 -OutputDirectory $b
dotnet run --project tests/DesktopMotionStudio.Core.Checks -c Release -- --f12-built-lifecycle $a $b
```

The isolated lifecycle check uses the published payloads and the production coordinator with a temporary installation root and a dedicated test registry key.

For a normal install, the program path is `%LOCALAPPDATA%\Programs\DesktopMotionStudio`. The current-user data root is `%LOCALAPPDATA%\DesktopMotionStudio`. Use Windows **Installed Apps** to uninstall. User state, backups, diagnostics and unresolved shortcut evidence remain under the data root. Edited or unverified files are reported as retained. If removal is interrupted, Setup can resume the same recorded removal, then a second run can install into an empty program slot. The detached uninstaller keeps one digest-verified work copy per entry binary in the user's temporary directory. An interrupted copy uses a private stage and cannot replace the runnable helper.

## Clean-account runbook

Use a disposable Windows 11 x64 VM and an interactive standard-user account. Transfer the two Setup executables, each package's `setup.sha256`, `entry.sha256` and `payload.manifest.json`, the self-contained WindowHost test target and `Test-GuestPrerequisites.ps1`. Generate `entry.sha256` on the build host from `entry\DesktopMotionStudio.Entry.exe` when assembling the media. Keep the guest disconnected throughout the run. Use ordinary Explorer or a native Windows PowerShell window in the guest. A process launched from a packaged desktop app can inherit file-system redirection, even when it reports no package identity. The installer refuses redirected writes.

```powershell
& .\Test-GuestPrerequisites.ps1 -CandidateA .\0.12.7 -CandidateB .\0.12.8 |
    Tee-Object -FilePath .\guest-preflight.json
```

The preflight validates package hashes, version order, Windows build, token groups, observed network adapters, standard .NET Desktop runtime locations and empty product slots. Compare the reported hashes with the build host record before running either unsigned package. Save a VM snapshot after preflight.

### T009 — Install and launch

1. Run A and decline the initial prompt. Record exit `1` and repeat preflight to verify that the program root, Start menu link, data root and uninstall entry remain absent.
2. Run A and approve installation. Record exit `0`, the version shown in **Installed Apps** and the Studio window started from the Start menu.
3. Add the copied WindowHost executable to Studio, bind an included scene, create a managed desktop link and close Studio. Activate the link through Explorer. Record the overlay and actual target window. Repeat with the installed MotionHost image temporarily moved aside within its own folder. Confirm the target window appears without the overlay, then restore the exact image before continuing.
4. Leave Studio open and attempt B. Record the Busy response, A's unchanged version and the existing Studio process still running. Close the test processes.

### T010 — Rollback and upgrade

Record SHA256 for `active`, the two stable entry executables, the managed desktop link, `state\studio-state.json`, its `.bak` and a separate test sentinel. Change a Studio setting twice beforehand to create a backup. Record the uninstall entry's `DisplayVersion`.

In the disposable profile, hold a read-only file handle while running the normal Setup B. Keep the handle open until the error dialog is closed and Setup returns. No program file is edited by this fault.

```powershell
$root = Join-Path $env:LOCALAPPDATA 'Programs\DesktopMotionStudio'
$setupB = (Resolve-Path .\0.12.8\DesktopMotionStudio.Setup.exe).Path
function Invoke-BlockedUpgrade([string]$Selector) {
    $held = [IO.File]::Open((Join-Path $root $Selector), 'Open', 'Read', 'Read')
    try { (Start-Process -FilePath $setupB -Wait -PassThru).ExitCode }
    finally { $held.Dispose() }
}
Invoke-BlockedUpgrade 'active'
# Confirm exit 4, unchanged A and a new active.next before proceeding.
Invoke-BlockedUpgrade 'active.next'
```

The first attempt stages B while keeping A active. The second attempt blocks B's commit after the old selector moves. Record the rename from `active` to `active.previous` and back using a `FileSystemWatcher` on the program root if capturing the transition. Check exit `4`, A's exact selector and stable entry hashes, A's registry version and a working A launch. Close Studio and run B with both handles released. Record exit `0`, B's version, unchanged stable paths, both entry hashes matching B's published entry image, and unchanged managed link, profile, backup and sentinel bytes. Run A again and verify exit `5` and unchanged B. Use the isolated lifecycle and child-interruption checks to retain additional evidence at the abrupt termination boundaries. If an abrupt exit interrupts a stable entry name change, rerun the same Setup B to finish.

### T011 — Installed Apps removal and reinstall

1. In the disposable VM, create distinct bindings and owned links for the clean, edited, replaced, moved and missing cases. Use distinct local copies of the test target if needed. Record filenames and hashes for the links, profile, backup, diagnostics and an unrelated file. Make controlled changes only to test links and record their resulting bytes.
2. Remove Desktop Motion Studio through **Settings → Apps → Installed apps**. Record the result dialog, absent registration and program root, deletion of the clean owned entry, and unchanged edited, replaced, moved and unrelated files. Verify the missing entry remains absent and unresolved receipts remain. Exercise the redirected or unsupported desktop case separately from a VM snapshot.
3. Reinstall B and open Studio. Verify that saved preferences and bindings reappear and compare retained file hashes. Save results and clean up the disposable VM.

Record workstation smoke tests, isolated process tests and the clean-account run separately. Evidence from a filtered administrator or a machine with an external .NET runtime cannot close the clean-account gate.
