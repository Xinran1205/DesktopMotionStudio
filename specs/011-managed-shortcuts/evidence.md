# F10 Verification Evidence

## T001 — Native ownership gate, 2026-11-06

Run from the repository root in PowerShell:

```powershell
pwsh -NoProfile -File .\specs\011-managed-shortcuts\probes\native-ownership.ps1
```

Real Windows build 10.0.26200 x64, local NTFS. 22 PASS, 0 FAIL. A fresh isolated temp fixture was generated. No real Desktop or Studio profile was touched.

- Non-replacing handle rename returned native 183 for an occupied destination and retained its bytes. Unoccupied same-volume publication succeeded and retained volume/file index across reopen.
- A same-byte copy differed by file identity. A hard link shared identity and exposed a link count of two. A retained mutation handle denied concurrent write/delete opens with native 32, and an existing write handle blocked verification.
- SHA256 was calculated from the exact handle used for disposition. Verified disposition removed its original pathname and kept unrelated bytes. A deliberately raced hard link created after the one-link observation also retained its pathname and bytes.
- A pinned directory and its pinned ancestor resisted renaming. Absolute destination publication into that pinned directory succeeded. Relative FileRenameInfo with a directory handle returned native 87 and is excluded from the design. A junction opened with OPEN_REPARSE_POINT exposed reparse attributes, but opening the child through it did not expose those attributes. Pin and check each ancestor in order.
- Windows Shell Link COM saved 3,043 bytes to memory. The staged link read back the exact target, argument and icon. Publishing via the retained handle preserved its ID, hash, target and argument.

The proof establishes primitives. Production receipt integration, shell activation and UI acceptance are separate tasks.
