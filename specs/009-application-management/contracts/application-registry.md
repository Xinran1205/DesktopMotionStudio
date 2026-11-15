# Application registry contract

- `Add(displayName, typedTarget)` validates metadata/F04 target, checks current resolved identity duplicates inside Update, and creates stable random AppId only for a new launch definition. Duplicate returns existing registration without writing.
- `Rename(appId, displayName)` patches only name in the latest state. `Repair(appId, typedTarget)` validates and patches target metadata without replacing IDs/binding or unrelated fields.
- `Remove(appId)` removes app and binding in one transaction; an already absent valid ID is Unchanged. UI owns confirmation.
- `Load()` returns saved profiles only. Search is a read-only UI filter.
- `GetTarget(profile)` converts only explicit Target metadata to F04 types, with a defensive copy of executable arguments; null Target yields null for F07.
- Invalid/duplicate/rejected requests and no-ops preserve state/backup bytes. Repository errors propagate as StorageError; no process launch or shortcut mutation occurs.
