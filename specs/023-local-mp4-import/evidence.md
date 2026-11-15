# F22 Windows acceptance

**Candidate** 0.21.0 for Windows 11 x64. The checks use generated media, isolated profiles and real Windows launch targets.

| Check | Result |
| --- | --- |
| Private copy and recovery | Forced an isolated import process to exit after creating a partial copy. The live draft remained private. Restart recovery cleared the abandoned files. No-replace publication preserved existing bytes, and a replacement file survived cleanup. |
| Stage-free inspection | The packaged worker decoded both landscape and portrait sources, returned their 6-second duration, and reported audio presence. It rejected malformed and unsupported files and accepted cancellation. |
| Studio import | Selected a file using the Windows picker, verified default duration and sound, changed duration, fit and sound, previewed, pressed Esc and saved. Reopening Studio in English preserved the custom name. Invalid media displayed the English error and kept Save disabled. |
| Edits and removal | Reopened the stored copy after moving the original. Verified stable IDs, stale revision rejection, unknown-schema preservation and busy removal. |
| Two bindings and real launch | Bound the work through Studio's binding service and launched two targets through Test launch and a real Windows shortcut. The cold target was absent while video was visible. Warm launch reused its target. Renaming preserved playback. Removing the work preserved the shortcut bytes and allowed native launch. |
| Regression | The packaged F21 video checks and both original work previews passed. Studio's automated suite passed 81 checks. Release compiled with zero warnings and errors. |
| Installer | Verified the 879-file Windows payload, isolated staging and activation. |

## Review

An independent review examined the import transaction, recovery, background player and Studio integration. The implementation verifies the identity of files discarded on failed save, rereads work metadata before replacement, validates the inspected copy through a read-only handle, resolves existing bindings directly by ID, and resumes interrupted draft cleanup on startup. A same-user process modifying the private import directory directly can still interfere between a metadata recheck and its rename.

Directory rename requires the descendant file handles to close on this workstation. The remove operation checks for active playback first. A playback request that races with the rename either resolves the old work or falls back through the existing native launch path.

The unsigned candidate and checksum are under `artifacts/f22/release-0.21.0/setup`. The test traces are under `artifacts/f22`. See [verification commands](quickstart.md).
