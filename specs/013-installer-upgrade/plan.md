# Implementation Plan — F12 Per-user Installation

**Branch** codex/f12-installer-upgrade · **Date** 2026-11-06 · **Spec** [spec.md](spec.md)

## Technical Context

Pinned .NET SDK 10.0.403, existing WPF/WinForms apps and Windows 11 x64. Independently publish self-contained win-x64 components. Use a self-contained setup executable and immutable version directories behind a stable entry point. Use a single version-selector commit and a small recovery journal for cross-resource registration. F10 supplies retained-handle file verification. Installation remains distinct from profile storage and scene licensing.

## Constitution Check

| Principle | Implementation |
|---|---|
| Target app wins | Preserve the installed Launcher entry path and native fallback. Installation never terminates target apps. |
| Local and least privilege | Per-user fixed program root, offline payload, no system service or updater. |
| Ownership | Validate the installation namespace and owned file list. Delete desktop entries only through F10 verified handles. No wildcard removal. |
| Boundaries | Setup owns payload staging, selector and registration. A stable shim starts the selected installed generation. A small uninstall helper invokes Core ownership cleanup. |
| Compatibility | One supported installed product family, ordered versions, unchanged profile2/IPC2. Reject unsupported selector schemas and file/process conflicts. |
| Small increments and quality | Verify bundled runtimes, installed Shell launch and isolated interruption and ownership cases on Windows. |
| Dependencies and Git | Build from the pinned SDK and BCL. Commit each verified increment. |

## Phase 0 — Research and gate

See [research.md](research.md) and [evidence.md](evidence.md). The isolated MSI proof restored old file bytes but failed to restore the old Windows Installer registration in a non-elevated rollback. Use a side-by-side stage and one active-selector commit instead. Verify real process interruption around that commit before integrating the Windows registration and desktop entry.

## Phase 1 — Packaging and installation

Author the Windows package build under `installer/`. Publish Studio, Launcher and MotionHost separately and assemble the existing component layout. Emit hashes and runtime provenance. Keep immutable generation directories within the fixed program root. Stable Studio and Launcher shims read one supported active selector and forward to the selected generation, preserving the validated binding token. Keep one current-user Installed Apps record and a fixed Start menu entry. Preflight known file slots, reparse boundaries and in-use program files. Journal installation/registration mutations and verify late failure recovery and occupied/conflicting paths on actual Windows.

## Phase 2 — Uninstall cleanup

Discover candidates in F10's private receipt store. Reopen and revalidate the exact current-desktop file and this install's Launcher identity. The ordinary uninstall helper runs from an owned copy outside the installation tree and deletes desktop entries only after program removal commits. Preserve unresolved evidence and retain bounded cleanup status. Program removal and user cleanup have distinct outcomes. See the [interface contract](contracts/installation.md).

## Review fixes before delivery

Separate the installer transaction gate from the stable-entry dispatch gate. An upgrade can stage a new generation while entries dispatch the current generation. Hold the dispatch gate around the final process-lock check and selection change. Hold both gates when first publishing the durable removal intent, then rely on that intent to deny dispatch while uninstall removes files. This preserves removal safety and the final process-lock preflight.

After the selector and registration commit, reuse uninstall's generation receipt and digest checks to reclaim owned generations strictly older than the previous selector. Keep current and previous intact. A conflict or interrupted retirement retains its proof and can be retried by rerunning the installed package. This maintenance does not affect the upgrade commit result.

Track handles' file and directory identities in fresh installer stages so ordinary failure can remove the exact created objects. Preserve unknown content and claimed upgrade stages that have durable retry receipts. An abrupt process exit before durable proof can leave an unverified stage. Its name alone gives no authority to delete it.

Record the old and new stable entry image digests in the upgrade intent. Publish the new generation first. Under the dispatch gate, replace each stable file through verified no-replace retirement and promotion in its pinned parent. Update the ownership record and remove the verified old copies only after both images match. A normal failure attempts to restore the retired name. An abrupt exit during a name transition may require the same Setup to restore entry availability. Retain the installed locations and existing link bytes.

## Phase 3 — Convergence

Run build, static checks, regression, package layout and native lifecycle checks. Verify the real current-user Setup, Start menu, installed Launcher, renderer fallback and registered uninstall in an ordinary non-elevated Windows 11 x64 session. Use isolated published-payload tests for fault/rollback, retained data, reinstall and desktop ownership conflicts. Record the session, runtime and network conditions. Close review and verify every FR, AC and SC. Additional compatibility results can be attached later.
