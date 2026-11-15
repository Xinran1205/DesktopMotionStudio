# State v2 compatibility contract

- Current schema becomes 2. Schema 1 loads into a validated current snapshot with null typed targets and original raw LaunchTarget strings. No network, launch, or file write occurs on load.
- Reject duplicate/unknown JSON members, unknown textual/numeric target kinds, invalid shape/ranges and unsupported future versions while preserving source bytes.
- Profile Target is optional only for legacy/unconfirmed entries; the UI asks for explicit repair. New Add/Repair always stores an explicit target after F04 validation.
- Existing schema-1 raw target values are never parsed as shell commands or guessed by suffix/AUMID punctuation.
- Same-reference Update is still a byte-preserving no-op, including on schema-1 input. Changed transactions use existing temporary/flush/atomic replace and exact previous-file backup.
- App/binding IDs and settings survive migration unchanged. Re-reading written schema 2 does not migrate again. Older software sees a future schema and must refuse replacement as established by F02.
