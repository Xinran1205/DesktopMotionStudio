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

## T009–T011 — Additional workstation proof, 2026-11-07

Windows 11 Home x64 build 26200, filtered administrator, with an external desktop runtime installed. Both Setup images match their prior published SHA256. The first run reached a recoverable inactive installation and returned exit 5 while writing the Start menu link. A native probe observed error 4395 under `OBJ_DONT_REPARSE`. A disposable delete-on-close probe identified the alternative path under the Codex application's private package cache. An ordinary interactive Windows process launched through a short-lived limited task used the physical Start menu directory. Production reparse checks and candidate bytes were preserved.

| Real workstation operation | Observed result |
|---|---|
| Resume Setup A at its normal per-user location | Exit 0. Original owner retained, Start menu link published and HKCU registration reports 0.12.3. |
| Start the installed Studio through its Start menu Shell Link | The stable entry opened the actual 0.12.3 Studio window. The external runtime search variables pointed to an absent path. |
| Run B while Studio is running | Exit 4. A selector SHA256, registration version and Studio process unchanged. |
| Close Studio, run B and rerun stale A | B exit 0 and version 0.12.4. A exit 5 and B selector unchanged. Start menu link, stable Studio and Launcher images, and ownership record retained their exact bytes. |
| Invoke the exact current-user registered uninstall command | Exit 0. The detached worker removed the program root, Start menu link and registration. |
| Reinstall A into the empty product slot | Exit 0 with a new owner, working registration and Start menu entry. |
| Block B's two selector operations in sequence | With `active` held read-only, B exited 4 and left a recoverable `active.next`. With only `active.next` held read-only, B again exited 4. A filesystem watcher recorded `active → active.previous → active`. A's exact selector hash and registration version survived. B retried successfully after releasing the handle. |
| Remove the second test installation | Exit 0. The program, menu, registry and user-data slots were empty at the end. The digest-verified helper remains reusable in the temporary directory. |

The host used the ordinary uninstall command read from registration. The Settings **Installed Apps** flow, managed Desktop Shell launch, renderer failure and conflict retention in a clean offline profile remain in the guest runbook. The workstation's redirected Desktop was left untouched. No application state was created by this smoke test. Short-lived task registrations were removed.

The Windows Hypervisor Platform partition probe succeeded. No provisioned Windows guest was available on this workstation. The Windows PowerShell 5.1 [preflight](probes/Test-GuestPrerequisites.ps1) correctly rejected the host token, connected adapters and external .NET Desktop runtime, while verifying both media hashes and all empty post-test slots. A separate mismatched-executable fixture was rejected before host inspection.

Release regression with the published MotionHost and Studio payloads passed 522 Core checks and 67 Studio checks with no failures. The optional packaged-instance check was skipped because `DMS_F05_TEST_AUMID` was unset.

The local media directory is `artifacts/f12-offline-acceptance-20261107`. It contains both unmodified candidates, payload manifests, the offline runbook and a self-contained WindowHost test target. The target apphost reached its parser with the external runtime path pointed at an absent directory. The media manifest has nine SHA256 records. Its own SHA256 is `AA1B0487BBF8674459734CF631F4C24E74B6810988E7636124C0E3037B5E04FA`. Workstation logs are in `artifacts/checkpoints/f12-cancel-20261107`. The feature remains gated on T009–T011 in the clean guest.

## T013 — Launch during Setup preparation

The new stable-entry regression failed with an unrelated Setup transaction holding the former shared gate. A separate dispatch gate allowed the same verified executable and binding token to run during that hold. A durable removal intent still prevented dispatch. Upgrade and removal interruption regressions passed with the new gate ordering. This proof covers the rebuilt entry implementation. The earlier 0.12.3 and 0.12.4 candidate binaries still contain their original entry code.

## T014 — Retired generation maintenance

Three consecutive fixture upgrades retained current and previous generations while deleting an older verified generation and its receipt. An unrelated version-shaped directory and a foreign file inside another retired generation survived. The foreign file blocked removal of its directory and receipt. After the fixture removed its conflict, rerunning the already installed package completed retirement. The targeted Release suite passed 39 Setup checks and 25 uninstall checks.

## T015 — Unpublished stage cleanup

The old implementation failed both the partial-extraction and ownership cleanup checks. After the fix, a failed final archive entry left no program stage, a raced program slot preserved the unrelated directory and its bytes, and an unrelated file injected into a private stage survived while the file recorded as created was removed. Claimed upgrade stages still survived a killed process and resumed. Targeted Release checks passed 20 payload, 39 Setup, 25 uninstall and 13 entry cases. Stages from abruptly killed processes without durable ownership remain preserved.
