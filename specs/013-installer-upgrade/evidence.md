# F12 Verification Evidence

**Date** 2026-11-06 · **Host** Windows 11 Home x64 build 26200, non-elevated filtered administrator token, NTFS · **SDK** 10.0.403

## T001 — Native MSI evaluation

A unique fixture product installed with an embedded cabinet and the expected per-user marker. Version B ran the file operations through InstallExecute, then the deliberate Type 19 action returned 1603. Rollback restored version A's marker bytes and HKCU marker value. It failed to restore Windows Installer's prior registration. Logs contain error 1406 and access denied on installer registration keys, and the old product did not return to installed state.

The probe used only its own fixture identity and paths. Both fixture products were removed through Windows Installer. A final reinstall/uninstall of the isolated A fixture removed the marker and HKCU key left by rollback. The negative result rejected this approach before product MSI authoring. The archived script requires explicit opt-in in a disposable account.

## T002–T003 — Self-contained payload

The existing Studio-only publish left the nested Launcher with five framework-dependent files. The new package build publishes the three production components independently into a clean staging tree. With both external DOTNET_ROOT variables directed to an absent path, all three apphosts reached their argument parser and their traces loaded the local .NET 10.0.14 host and runtime.

The 0.12.0 payload has 870 files and its ZIP is 154,043,301 bytes. The independent check reopened the archive, compared every entry's length and SHA256, checked the runtime notices and refused duplicate root apphosts or test/debug payloads. Two clean builds in different paths produced the same archive SHA256:

```text
D38F092CE1E6A8BE7A9EE44F1AEDC04AAAFAD42B72F5F87E9B976790F75A2F28
```

An attempted repeat into an existing output refused to build and preserved the archive. A separate framework-dependent Studio publish retained its 21-file development layout.

## T004 — Selector proof

Nine Windows checks passed. A child writer flushed the selector stage, then the parent terminated it in three separate runs. Before retiring the primary, the old selector remained readable. With the primary retired and the new selector unpublished, the reader used the old generation from previous. After publication, the reader saw the new selector. Both immutable fixture payloads retained their bytes in every case. A competing foreign primary survived a refused no-replace publication, malformed data failed closed, and a junction was rejected while its direct target was readable.

This is a retained-handle selection proof with marker payloads. Production Setup, real launcher forwarding, registration recovery and clean-account installation are subsequent tasks.

Logs are retained locally at artifacts/checkpoints/f12-20261106. The verified payload is at artifacts/f12-payload-0.12.0-d962d681.
