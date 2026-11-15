# F10 Independent Review and Convergence

**Date** 2026-11-06

## Review findings and dispositions

| Finding | Disposition and evidence |
|---|---|
| Absolute child create could escape a pinned empty directory after an attribute-only junction change | Retained-parent NT relative opens refuse reparse traversal. Same-parent no-replace handle rename. The Windows regression reproduces the junction conversion and verifies zero files in the redirected target. |
| Unconditional receipt replacement could overwrite a future or corrupt record appearing after validation | Verified-handle handover to previous generation followed by no-replace primary publication. Gap recovery and future occupant tests preserve both generations. |
| Eligibility could go stale between staging and publication | Existing profile writer lock guards final state resolution and publish. Returning the identical snapshot preserves profile and backup. A controlled disable after stage prevents publication. |
| Fixture switch could target real data | Dedicated temp subdirectory required. Real Desktop and default profile are refused. The published Studio process rejects both invalid combinations. |
| A→B→A could leave shortcut UI stale | Select identity plus independent read generation, final read on the current selection. Controlled test releases an older result after verified create and checks Remove remains enabled. |
| Profile load errors could look like ineligible binding | Storage errors map to storage or busy feedback. |
| Invariant uppercase could collide for distinct Unicode legacy IDs | Key normalization accepts an uppercase mapping only within OrdinalIgnoreCase equivalence. Tests cover case variants and distinct long-s/ASCII IDs. |
| Root executable copies appeared in early publish | Explicit child staging with transitive copies disabled. Verified child bundles and shipping parser. |

The native/filesystem and storage/UI reviewers verified the fixes independently. No open P1 or P2. Review sources were read-only.

## Traceability

| Specification | Evidence |
|---|---|
| FR-001, AC-01 | Explicit WPF actions, no files on browse, final eligibility, concurrent and repeated create |
| FR-002, FR-003, FR-008, AC-02, AC-03, SC-002 | Strict binding token, release layout, real Windows Shell .lnk activates isolated Launcher, real handoff and native fallback |
| FR-004, FR-007, AC-04, AC-05, AC-06, AC-07, SC-001 | Windows native proof and production adapter checks for identity, hard links, conflict, scope, repair and removal |
| FR-005, SC-004 | Studio selection/keyboard state and published F10, F08, F09 UI |
| FR-006, AC-08, AC-09, SC-003 | Pending/previous receipt recovery, real process interruption, I/O and lock failures, preserved bytes |

Convergence checked eight functional requirements, nine acceptance scenarios, four success criteria, the plan decisions and ten constitution principles. F10's scope has no remaining code gaps. The Shell acceptance uses the production Launcher source compiled with an isolated entry point. The shipping binary's default profile and stable installed path will receive clean-account validation with F12. This development milestone retains the optional packaged-instance skip and the local fixed NTFS scope.
