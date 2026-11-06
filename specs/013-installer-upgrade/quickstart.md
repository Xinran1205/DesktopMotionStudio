# F12 Verification Guide

Run from the repository root with the pinned SDK on PATH:

```powershell
$out = Join-Path $PWD 'artifacts/f12-local-payload'
& .\installer\Build-Payload.ps1 -Version 0.12.0 -OutputDirectory $out
& .\specs\013-installer-upgrade\probes\Test-Payload.ps1 -OutputDirectory $out
& .\specs\013-installer-upgrade\probes\Test-Selector.ps1
```

The output directory must be new. These commands build an offline payload and verify local execution and selector behavior. See [evidence.md](evidence.md) for the verified scope.

## Full installation acceptance

1. From a normal PowerShell session, record Windows edition, build, architecture, token type, SDK and runtime inventory. Build two isolated setup versions and retain old/new file and selector hashes.
2. Install A, interrupt B at the staged and committed selector boundaries, verify registration recovery and A bytes, retry B, test downgrade/repeated operations and remove the fixture. Repeat product acceptance with the release package layout.
3. On a fresh offline Windows 11 x64 VM, use a true standard-user account without the .NET Desktop runtime. Install, open Studio, create a managed link and invoke it through Windows Shell after Studio exits. Exercise renderer fallback and running-process Busy behavior.
4. Upgrade and compare the existing link, profile, backup and unrelated file bytes. Uninstall via Windows Installed Apps with clean and conflicting entries. Reinstall and verify preserved preferences and bindings.
5. Record automatic and real Windows evidence separately. A non-elevated filtered administrator workstation run is a proof, with clean-account acceptance still required.
