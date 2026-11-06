# F10 Data Model

## Binding launch key

A nonblank binding ID of at most 128 UTF-16 code units with valid UTF-8 representation. F07 generates GUIDs and F02 accepts opaque legacy IDs. Canonical unpadded Base64Url of strict UTF-8 is the only positional shell value, capped at 512 ASCII characters. The receipt filename uses a SHA256 key derived from the case-normalized ID. The persisted profile remains authoritative for app, scene and enabled state.

## Ownership receipt schema1

One bounded JSON document per binding under the current profile's private shortcut directory. Explicit version1, matching canonical binding ID, optional current proof and optional pending proof. At least one proof must be present. Unknown or duplicate members, unsupported version, invalid types and size over 16 KiB fail closed.

A proof stores the fully qualified canonical desktop path, verified launcher path, volume/file identity, creation timestamp and uppercase 64-character SHA256 of the complete .lnk. Only an ordinary file with exactly one hard link and size 1–65536 bytes can be owned. Both proofs remain in a repair receipt until publication is verified. Ownership is evaluated at a recorded path inside the currently validated desktop root. A path outside that root cannot be touched.

## Transitions

- No receipt plus no collision → stage candidate, persist pending proof, publish without replace, verify, promote.
- Current proof matches → Healthy, or RepairRequired when the bundled Launcher moved. Duplicate create leaves file and receipt unchanged.
- Repair → stage and persist new proof while keeping old proof. Delete only old matching handle, then publish without replace. Interruption before deletion leaves the old usable file. Interruption after deletion and before publication leaves a repairable missing entry. Interruption after publication leaves a pending proof that can identify the file.
- Recorded path occupied by any unmatched entry → Conflict. Retain user bytes and ownership data.
- Explicit remove → verify current or pending proof at the recorded path, delete that file handle and verify absence, then clear receipt. If no recorded file exists at an accessible recorded location, clear stale receipt on explicit remove. Keep the receipt if absence or ownership cannot be established.
- Invalid, inaccessible, future-version receipt, unsafe root or busy lock → no desktop mutation.
