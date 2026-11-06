# F12 Independent Review

**Date** 2026-11-06

| Finding | Resolution |
|---|---|
| An old uninstall request could affect a reinstalled owner | Revalidate the expected owner while holding the installer gate. Recovery requires the recorded pending removal. A reinstall fixture exercises both stale calls. |
| Startup could race removal and a retained older version could be missed | Share the gate through process creation and probe all recognized generation executables. A locked previous generation blocks removal. |
| A replaced temporary worker could execute before its internal check | Use a pinned local directory, write a new stage, flush and check its digest, publish without replacement and retain the verified image through CreateProcess. |
| Direct temporary write could leave an unusable final executable | The final name appears only after verified publication. The reusable work copy is keyed by its image digest. |
| Upgrade could claim a foreign matching directory or ignore extra entries | Persist directory ownership before no-replace publication and require the same scope and exact tree on recovery. Check the full payload again at activation. |
| Uninstall could strand an unpublished stage after discarding its receipt | Verify its recorded stage name, identity, inventory and directory proofs. Remove the receipt only after its owned data is handled. |
| Replaced directories and modified registration could be treated as owned | Match recorded NTFS directory identities, key class and known registry values and types before removal. |
| A corrupt menu receipt could leave a permanently pending uninstall | Retain it as a conflict while completing independent program and registration cleanup. |

Independent reviewers checked install, upgrade, program ownership, the native scope and detached worker. The final narrow re-review verified stage recovery and final activation validation. T009–T011 still require clean standard-account and actual Installed Apps acceptance. Final F12 convergence is pending those gates.
