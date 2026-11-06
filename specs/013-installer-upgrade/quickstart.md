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
& .\installer\Build-Setup.ps1 -Version 0.12.3 -OutputDirectory $a
& .\installer\Build-Setup.ps1 -Version 0.12.4 -OutputDirectory $b
dotnet run --project tests/DesktopMotionStudio.Core.Checks -c Release -- --f12-built-lifecycle $a $b
```

The isolated lifecycle check uses the published payloads and the production coordinator with a temporary installation root and a dedicated test registry key. For the clean-account gate, transfer only each `setup` directory and a known test target into the disposable VM, then verify `setup.sha256`.

1. From a normal PowerShell session, record Windows edition, build, architecture, token type, SDK and runtime inventory. Build two isolated setup versions and retain old/new file and selector hashes.
2. Install A, interrupt B at the staged and committed selector boundaries, verify registration recovery and A bytes, retry B, test downgrade/repeated operations and remove the fixture. Repeat product acceptance with the release package layout.
3. On a fresh offline Windows 11 x64 VM, use a true standard-user account without the .NET Desktop runtime. Install, open Studio, create a managed link and invoke it through Windows Shell after Studio exits. Exercise renderer fallback and running-process Busy behavior.
4. Upgrade and compare the existing link, profile, backup and unrelated file bytes. Uninstall via Windows Installed Apps with clean and conflicting entries. Reinstall and verify preserved preferences and bindings.
5. Record automatic and real Windows evidence separately. A non-elevated filtered administrator workstation run is a proof, with clean-account acceptance still required.

For a normal install, the program path is `%LOCALAPPDATA%\Programs\DesktopMotionStudio`. The current-user data root is `%LOCALAPPDATA%\DesktopMotionStudio`. Use Windows **Installed Apps** to uninstall. User state, backups, diagnostics and unresolved shortcut evidence remain under the data root. Edited or unverified files are reported as retained. If removal is interrupted, Setup can resume the same recorded removal, then a second run can install into an empty program slot. The detached uninstaller keeps one digest-verified work copy per entry binary in the user's temporary directory. An interrupted copy uses a private stage and cannot replace the runnable helper.
