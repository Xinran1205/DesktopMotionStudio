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

## T005a — Fixed entries and program staging

The actual process-forwarding tests used isolated apphost fixtures. They verified exact F10 binding arguments and the selected working directory before and after switching versions. A missing active selector selected the verified previous version. A malformed primary, changed image or missing selected executable failed closed. The Studio development bundle kept its bundled Launcher location and 21-file publish layout.

The preparation tests validated bounded archive and inventory matching, rejected path traversal, case aliases and a nested Windows junction, and preserved competing files. NTFS no-replace directory publication refused a product root occupied after staging. After publication the new directory still had no active selector. Promoting the verified selector enabled the selected generation, and a foreign primary survived a refused promotion.

The real Start menu-format Shell Link retained the fixed Studio target and an empty argument list. Its write handle was closed before Windows Shell readback. A same-name link arriving between preparation and publication retained its bytes. All link tests used an isolated directory.

The 0.12.1 test build verified every one of 870 archive files, plus native startup of all three components from their bundled runtimes. A real isolated preparation and activation used that archive and the published self-contained stable entry binary. Both renamed entry copies started and rejected the fixture install location with exit 2, with external DOTNET_ROOT paths set to a missing directory and extraction isolated to the test directory. The forwarding checks used the same production forwarding logic with fixture executables.

| Suite | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Core regression including 28 F12 checks | 453 | 0 | 1 |
| Studio including real discovery protocol | 67 | 0 | 0 |
| Published 0.12.1 archive/layout/apphost checks | 5 | 0 | 0 |
| Published package staging and activation | 3 | 0 | 0 |

The existing optional packaged-instance case was skipped because DMS_F05_TEST_AUMID was unset. Release build and whitespace checks passed. Windows registry and the real Start menu were outside this increment. Setup registration, recovery of interrupted registration and standard-account acceptance remain open.

The test build is retained at artifacts/f12-t005-4e40bd07 and hashes and logs at artifacts/checkpoints/f12-t005a-20261106.

## T005b–T008 — Setup lifecycle

Two unsigned self-contained Setup candidates were built. In each package, the embedded archive, inventory and fixed entry image matched their source SHA256. Both Setup apphosts reached their argument parser with the external runtime search path pointed at an absent location.

The production coordinator ran against each published payload under a temporary Windows program root and a dedicated current-user registry key. The lifecycle verified A install, controlled B failure after retiring A, exact A selector and registration recovery, B retry, stale A rejection, unchanged stable Launcher and shortcut bytes, removal of the installed payload and owned links, and reinstall with the retained profile. Versions 0.12.3 and 0.12.4 each contained 870 payload files.

Actual child-process interruption covered five initial installation checkpoints, three selector and registration upgrade checkpoints, and four uninstall checkpoints. Separate controlled fault tests covered a claimed but unpublished upgrade stage, a changed dependency at activation and a stale removal request after reinstall. Receipt cleanup verified orphan and previous-generation desktop entries, reparse rejection, bounded discovery, and preservation of moved, edited, busy, wrong-install and unsupported items.

| Suite | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Core regression including 97 F12 checks | 522 | 0 | 1 |
| Studio including published discovery | 67 | 0 | 0 |
| Published Setup resources and apphost smoke | 8 | 0 | 0 |
| Two published payloads through isolated Windows lifecycle | 6 | 0 | 0 |

The optional packaged-instance test was skipped because DMS_F05_TEST_AUMID was unset. No current personal program registration, desktop entries or profile was used. The work copy of the detached uninstaller is reused by image digest. The mapped-image unlink proof was refused by Windows, so the verified work copy can remain in the user's temporary directory.

The workstation has a filtered administrator token, a private .NET SDK and no Windows Sandbox or local VM command. T009–T011 remain open for a clean Windows 11 x64 standard account, offline startup, actual installed Shell launch and Installed Apps removal. Final convergence and F13 entry depend on that evidence.

Candidates are retained at artifacts/f12-candidate-0.12.3-01ff9893 and artifacts/f12-candidate-0.12.4-72f8b047. Hashes and raw logs are in artifacts/checkpoints/f12-lifecycle-20261106.
