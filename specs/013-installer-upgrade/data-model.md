# F12 Data Model

| Entity | Identity and rules |
|---|---|
| Product family | Stable product ID and HKCU registration, current-user scope, known folder plus fixed program path. |
| Product version | Three numeric fields, immutable version directory, explicit package digest and sorted payload inventory. |
| Payload file | Canonical relative path, component identity, size and SHA256. Entries are confined to the install root. |
| Installed state | Absent, installed generation, in transaction or needs recovery. A versioned selector and verified journal constrain recovery. |
| Existing user data | Profile, backup, diagnostics and F10 receipts kept under the existing user-data root. Installer reads only what is necessary to validate cleanup. |
| Cleanup result | Bounded category counts for removed, missing/out of scope, conflict/unsupported and busy/unavailable. The helper writes no original paths, arguments or application identifiers into the summary. |

Program installation and upgrade update installed state only. Ordinary uninstall transitions the product to absent, then verifies and removes eligible desktop entries. Unresolved evidence remains available after reinstall.
