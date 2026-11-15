# F10 Verification Evidence

## T001 — Native ownership gate, 2026-11-06

Run from the repository root in PowerShell:

```powershell
pwsh -NoProfile -File .\specs\011-managed-shortcuts\probes\native-ownership.ps1
```

Real Windows build 10.0.26200 x64, local NTFS. Final repeatable proof 35 PASS, 0 FAIL. A fresh isolated temp fixture was generated. No real Desktop or Studio profile was touched.

- Non-replacing handle rename returned native 183 for an occupied destination and retained its bytes. Unoccupied same-volume publication succeeded and retained volume/file index across reopen.
- A same-byte copy differed by file identity. A hard link shared identity and exposed a link count of two. A retained mutation handle denied concurrent write/delete opens with native 32, and an existing write handle blocked verification.
- SHA256 was calculated from the exact handle used for disposition. Verified disposition removed its original pathname and kept unrelated bytes. A deliberately raced hard link created after the one-link observation also retained its pathname and bytes.
- A pinned directory and its pinned ancestor resisted renaming. Absolute destination publication into that pinned directory succeeded. Relative FileRenameInfo with a directory handle returned native 87 and is excluded from the design. A junction opened with OPEN_REPARSE_POINT exposed reparse attributes, but opening the child through it did not expose those attributes. Pin and check each ancestor in order.
- Windows Shell Link COM saved 3,043 bytes to memory. The staged link read back the exact target, argument and icon. Publishing via the retained handle preserved its ID, hash, target and argument.
- An empty directory pinned with write sharing could become a junction in place. Pinning with read sharing only blocked that operation with error 32. Creating and retaining a regular stage file under the original parent pin, then overlapping a write-sharing parent handle of the same identity, supported absolute no-replace rename. The retained stage or published file kept the parent nonempty. Junction conversion failed with error 145 before and after rename. The checked ancestors remained pinned with read sharing only.

An additional isolated ShellExecute probe activated a GUI-subsystem marker program via an actual .lnk with a spaced path. It delivered exactly the two configured arguments and preserved the .lnk hash. This probe used a fixture executable. Product Launcher integration is tracked by T013.

The proof establishes primitives. Production receipt integration, shell activation and UI acceptance are separate tasks.

## T002–T005 — Standalone Launcher increment, 2026-11-06

The new check harness first failed to build because the managed launch contract was absent. With the contract and runtime in place, targeted checks reported 17 PASS, 0 FAIL. They include canonical legacy IDs, bounded malformed input, one snapshot, disabled and malformed playback fallback, latest scene, absent state, missing target, renamed published Launcher, and a real shortcut pointing at the renamed apphost. The default Core regression includes the 15 fixture-independent F10 cases.

Fresh win-x64 framework-dependent Studio publish staged exactly five files each under Launcher/ and MotionHost/. The independent review caught unusable partial root payloads caused by transitive executable project copies. Setting Private=false on those references kept explicit staging and eliminated both duplicates. A real published Launcher process rejected extra CLI parameters with exit 2. Valid product shortcut activation, actual handoff and process-level native fallback remain T013.

Core full regression ran against the published MotionHost and a real WindowHost fixture. **336 PASS, 0 FAIL, 1 optional packaged-instance SKIP** (DMS_F05_TEST_AUMID unset). Studio regression **53 PASS**, fresh published F09 actual UI **15 PASS**. F09 UI run exercised Stop, Esc, removal and abrupt-close cleanup with isolated state and default system motion preference. This run did not assert a specific preference value. Release build and whitespace verification passed for the affected code. No Studio profile or real desktop link was changed.

Independent launch review verified the single-dispatch path and found no further actionable issues after the packaging fix. Local logs dms-f10-native-ownership-final.log, dms-f10-launch-checks.log, dms-f10-core-regression-final.log, dms-f10-studio-regression-final.log and dms-f10-f09-published-regression.log were captured under TEMP.

## T006–T007 — Durable receipts, 2026-11-06

Core Checks `--f10-receipt-checks` reported 22 PASS, 0 FAIL on an isolated NTFS fixture after the initial build failed on the absent receipt API. Coverage includes strict schema and member checks, 16 KiB bound, old binding IDs, current and pending proof, blocked replacement preserving exact bytes, and a separate child process holding the lock. The worker persisted pending proof and was terminated before release. A competing transaction timed out at about two seconds. The pending proof was readable after termination, the next transaction could clear it, and the fixture profile and backup bytes matched.

Receipt operations pin ancestry, decline unsafe objects, retain stages through flush and rename, and only replace files in the private receipt namespace under a product lock. The desktop lifecycle is a separate integration gate.

## T008–T010 — Windows shortcut lifecycle, 2026-11-06

Real NTFS and Shell Link integration reported 15 shell and 16 lifecycle PASS. The lifecycle run covered concurrent and repeated create, a same-name foreign file, missing and stale repair, edited and moved entries, desktop scope changes, pending proof before and after publish, explicit verified removal and exact profile/backup preservation.

Initial lifecycle tests reproduced four failures. A stage creation time of 134384133594994123 changed to 134384133593853923 on same-name publication. NTFS tunneling invalidated creation time as stable ownership evidence. Keep NTFS volume/file identity, full SHA256 and a unique generation marker in the link bytes. Tests also showed a waiting transaction's record-directory pin could block the active writer. Separate locks/ and records/, open records/ after the per-binding lock, and acquire that lock before pinning the desktop. The unchanged functional assertions then passed. The schema1 shape was refined during development, before any product UI wrote receipts.

Full Core regression with actual host and target fixtures reported 389 PASS, 0 FAIL and the existing optional packaged-instance SKIP. Studio win-x64 Release built with zero warnings or errors. Raw regression log dms-f10-ownership-core.log is under TEMP.

## T011–T015 — Studio, review corrections and final Windows acceptance

The independent native review reproduced a write-attributes-only change of a checked empty directory into a junction. The original absolute child create followed it. Retained-parent NtCreateFile with OBJ_DONT_REPARSE refused this case in the corrected adapter. NtSetInformationFile with a bare same-parent name kept the retained source identity and refused an occupied destination. Directory handles allow shared reads and writes and deny shared deletes. The handle-relative operations enforce scope and let F02 replace its own profile while an ancestor is pinned. A regression that disables the binding after staging exposed the original sharing conflict, then passed with corrected sharing and the final profile lock.

Receipt commit preserves a verified previous generation and publishes the new primary without replace. Tests recover when the primary is absent and preserve a future primary inserted in that gap. Studio handles selection and read generations separately, reports profile failures, and limits the fixture switches to an isolated temp subtree. Review dispositions are in [review.md](review.md).

**Final Release evidence** on Windows 11 x64 build 26200 and actual DPI120 (125%):

| Suite | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Core regression with published MotionHost and real target | 395 | 0 | 1 |
| Studio regression | 57 | 0 | 0 |
| F10 published actual UI and Windows Shell | 12 | 0 | 0 |
| F09 published UI regression | 15 | 0 | 0 |
| F08 published UI regression | 16 | 0 | 0 |
| Launcher contract with published apphost | 17 | 0 | 0 |

Core includes 25 receipt, 17 shell and 17 lifecycle checks. The optional packaged-instance SKIP is DMS_F05_TEST_AUMID unset. Relevant Release builds and analyzers passed with zero warnings and errors. Format and Git whitespace checks passed.

Published Studio keyboard Alt+C/E/M explicitly created, repaired and removed the isolated shortcut. App/profile backups and foreign bytes were verified. Studio exited before Shell activation. The actual .lnk started the test Launcher and one verified target. The first and replacement works reached WindowReady and HandedOff. Disabled motion returned WindowReady with no host. Temporarily removing the fixture bundle host returned Failed/StartFailed and WindowReady with one target. All Shell activations preserved shortcut bytes. The Launcher fixture compiles the production entry source with an isolated profile provider and trace hook, and is never staged into the shipping publish. The shipping apphost independently returned exit 2 for unexpected CLI switches.

Retained local development publish artifacts/studio-f10 has 21 SHA256-verified files. Six final logs and the hash list are in artifacts/checkpoints/f10-20261106. Both component subdirectories have exactly five required files, with no root duplicates. No test or host process remained. The actual user desktop and profile were outside the test scope.
