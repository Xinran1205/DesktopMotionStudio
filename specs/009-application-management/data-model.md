# F08 Data Model

## Versioned persistent state

Configuration schema 2 retains applications/bindings/settings. `ApplicationProfile(AppId, DisplayName, LaunchTarget, Target?)` keeps the original string for legacy preservation and adds optional typed target. A null target is unconfirmed legacy metadata, not an inferred launch command.

| Field | Constraint |
|---|---|
| AppId | Stable nonblank ≤128 characters, case-insensitive unique. |
| DisplayName | Nonblank ≤120 characters. |
| LaunchTarget | Original nonblank text ≤2048; never interpreted by the default F08 provider. |
| Target.Kind | Text enum Executable / Shortcut / Packaged; numeric/unknown values rejected. |
| Target.Identifier | Nonblank ≤2048, no NUL/control characters; executable/shortcut absolute paths with correct extension, or structurally valid AUMID. |
| Target.Arguments | Non-null vector, max 256 strings, total ≤30000 characters including separators; no null/NUL arguments. Only executable may have arguments. |
| Target.WorkingDirectory | Optional absolute directory path for executable; null for other kinds. Existence is runtime validation, not a load prerequisite. |

Schema 1 must not contain typed targets. Validate legacy structure, preserve IDs/text/collections/settings, change only in-memory schema number and keep targets null. Reads/no-op Update do not touch disk. First changed transaction writes schema 2 with exact schema-1 file backup. Future schema numbers are checked before interpreting target members.

## Application registry results

Applied / Unchanged / Duplicate / InvalidRequest / ApplicationMissing / TargetUnavailable / StorageError, with optional affected/existing profile and RepositoryError. Registry list returns saved profiles or storage error. Rename/repair affect one app; remove also removes its binding atomically.

Target duplicate key derives from resolved F04 identity: executable path and working directory compare OrdinalIgnoreCase, arguments compare Ordinal; packaged ID compares Ordinal. Display name is excluded. Unconfirmed legacy targets are not guessed or deduplicated.

## Discovery and presentation

Candidate: display name, explicit target, source label. Result: Complete/Empty/Failed/TimedOut/Cancelled, bounded candidates and stable diagnostic. UI view references saved profile and current F07 binding/target status; no discovered candidate appears on home until registered.
