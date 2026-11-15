# F08 Verification Evidence

**Date:** 2026-11-05 · **Environment:** Windows 11 Home x64, build 26200; .NET SDK 10.0.403 / Windows Desktop runtime 10.0.14.

## Feasibility gate

Before implementing Studio, the isolated WPF POC built with zero warnings/errors, displayed a visible window (PID 1620, HWND 4851268, DPI 120), closed normally and returned exit 0. Read-only AppsFolder research found 125 shell entries and 40 verified packaged identities. The production bounded worker later returned 122 combined candidates / 40 packaged. Discovery does not launch these applications.

## Builds and automated checks

- Release builds of Core, Studio win-x64, MotionHost win-x64 and both check harnesses passed with zero warnings/errors. Nullable, analyzers and warnings-as-errors remain enabled.
- `dotnet format --verify-no-changes --no-restore` passed for Core, Studio and both check harnesses.
- Full F00–F08 Core harness: **171 PASS / 0 FAIL / 1 explicit SKIP**. F08 contributes **37 checks**. The optional F05 real packaged-instance test was not configured. The inherited F04 harness also labels its two unconfigured packaged activation probes as skipped PASS lines; those are not new activation evidence.
- Studio worker/protocol/process checks plus three real-WPF race checks: **13 PASS / 0 FAIL**, using both the executable apphost and `dotnet ...Checks.dll` entry modes. The test apphost explicitly dispatches `--discovery-worker` and rejects unknown arguments; final process inspection found no remaining Studio or Studio.Checks fixture processes.
- Production discovery returned **Complete, 122 candidates, 40 packaged**. Malformed/oversized/nonzero-exit outputs, timeout, cancellation and pre-cancellation are covered. Exactly 2000 candidates succeed; candidate 2001 returns Failed with zero candidates. Timeout/cancellation cleanup checks verify the owned worker has exited.
- Machine-local Core stdout: `%TEMP%/dms-f08-final-core-checks.log`; Studio stdout: `%TEMP%/dms-f08-final-studio-checks.log`. These logs are local test artifacts and are not user configuration.

Core coverage includes exact legacy source/backup preservation, no-op non-writing migration, repeatable schema upgrade, missing installed targets remaining repairable, future-version precedence, non-numeric schema, strict enums/required properties/unknown or duplicate members, protocol version/source-kind/size/count bounds, resolved duplicate identity, argument case, typed shortcut identity, same-app concurrent rename/repair, atomic app/binding removal, storage-error mapping and real junction rejection.

## Real Windows regression integration

The final Core run used the built MotionHost and complete WindowHost POC outputs through the maintained F06/F07 integration modes:

| Scenario | Observed result |
|---|---|
| F06 process host | PID 6328, HWND 526516; real handoff fade 291 ms; child exited |
| F06 target survives handoff | PID 15452, HWND 3408954 |
| F07 concurrent mutation workers | PIDs 28152 and 20512; both edits survived |
| F07 shared scene, target 0 | scene-one; PID 16816; HWND 0xB079C; HandedOff |
| F07 shared scene, target 1 | scene-one; PID 19360; HWND 0x350080; HandedOff |

The two target processes/windows were distinct. Only owned fixtures were closed. Original scene-manifest schema-1 tests were retained; only current profile-state fixtures changed to version constants.

## Actual Studio UI and scaling acceptance

The maintained `StudioUiChecks --ui` starts the real Studio executable with an isolated state/catalog and copies of the POC target. UI Automation reads and edits actual WPF controls; SendInput exercises keyboard actions. No real user state is used. Each final scale run passed **16 assertions**:

1. Manual keyboard add persists typed target without launching it.
2. Restart restores details.
3. Saved list has a human-readable accessible name.
4. Binding disable and unavailable-scene enable rejection preserve identity.
5. Binding re-enable agrees with disk/F07.
6. Rename and repair retain app and binding IDs.
7. Cancelled add preserves exact bytes.
8. Cancelled removal preserves registration.
9. Confirmed removal atomically removes app and binding.
10. Discovery list has a human-readable accessible name.
11. A real discovered packaged candidate saves explicit AUMID/type.
12. Management never launches either owned target.
13. Unreadable configuration leaves a readable recovery view.
14. Reading legacy home preserves original bytes.
15. Legacy repair leaves the path empty for explicit confirmation.
16. Legacy repair preserves raw text/IDs and exact v1 backup.

Saved-only search is also exercised inside the flow: a nonmatching query empties selection, then clearing the query restores the saved entry without any mutation.

| Actual Windows scale | DPI from GetDpiForWindow | Final UI run | Layout observation |
|---|---:|---|---|
| 100% | 96 | PID 28748, HWND 0x20744; 16 PASS | Home headings, list, search, details and recovery text readable; details remain scrollable |
| 125% | 120 | PID 13276, HWND 0x2D043A; 16 PASS | Readable home/repair status and reachable keyboard actions |
| 150% | 144 | PID 22168, HWND 0x1086A; 16 PASS | Readable home/list/details; additional visual window PID 19000, HWND 0x307E8; accessible list names verified |

Actual Windows Settings standard scale options were changed on the active 2560×1440 display and restored to the starting **125%**. Screenshots were inspected through the computer-use surface. This is actual desktop evidence, not simulated WPF zoom. A first 100% harness attempt failed because Windows denied foreground focus; the rerun explicitly activated the returned owned acceptance window and passed. No assertion was weakened. The optional `DMS_F08_WAIT_FOR_FOCUS=1` harness gate supports this desktop restriction.

## Independent review and corrected findings

- T018: discovery limit exceptions inherit IOException and were swallowed by per-directory tolerance. They now reach Failed; exact/over-limit worker tests pass.
- T019: local directory junctions could redirect validation. A real junction fixture failed before the guard, passed after normalized component-by-component reparse rejection; static review confirmed probes follow the guard.
- T020: saving did not prevent X/Alt+F4 from hiding a background commit. A gated Update/actual window test failed before the Closing guard and passed afterward.
- T021: an older refresh failure could override a newer successful view. A gated read/actual window test failed before the generation check and passed afterward.
- T022: a successful command could hide its subsequent reload failure. The recovery error is now retained; the test checks again at dispatcher idle before closing.
- T023: record diagnostic strings were exposed as accessible list-item names. Explicit display-name bindings and actual UIA assertions now pass.
- T024: the test apphost did not recognize the production worker argument and could re-enter its own checks. Explicit worker dispatch/unknown-argument rejection were reviewed and both launch modes passed; no fixture processes remained.

Follow-up independent read-only review found no remaining definite product P1/P2 in Core migration/registry, local path guards, discovery/client or Studio save/refresh behavior. Reviewers did not run the desktop tests; the interactive evidence above was performed by the primary agent.

## Scope and compatibility limits

F08 is an application-management development build. It does not deliver scene preview/selection, owned shortcuts, an installed launcher, installer/upgrades or licensed animation content. Those remain F09–F14. Packaged identity validity is structural metadata, not proof of activation. Reparse-backed targets require their actual local path; the guard does not claim isolation from malicious same-user concurrent filesystem replacement. Scaling evidence covers the active display and Windows build above, not every monitor/GPU/Windows configuration.
