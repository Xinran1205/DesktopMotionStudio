# F11 Data Model

## Profile preferences

Use existing profile2 Settings. Theme is exactly system, light or dark. Muted is a Boolean audio preference. Saving uses the expected original preferences and desired preferences. If desired equals current, return Unchanged without writing. Otherwise a stale expected value produces Conflict. A successful save preserves all application and binding fields. Unsupported state is reported and preserved.

## Diagnostic event schema1

A closed event contains schema version, UTC timestamp, randomly generated request GUID, bounded four-part numeric component version, Windows build integer, adapter category, stage, result, fallback reason and integer elapsed milliseconds. No user text, paths, titles, arguments, app/binding/scene identifiers, HWND, screenshot or raw exception. All enums reject undefined values. Each elapsed value is 0–120000 ms.

The private store retains at most 128 current records. Committed diagnostics across current and recovery generations occupy at most 64 KiB. Use at most 32 KiB per generation and discard oldest events to fit. Unknown, malformed, oversized or inaccessible records yield a structured diagnostic state. Co-operating writers take one nonblocking cross-process lock. Diagnostic read and write failures leave application launch results unchanged.

## Recovery summary

Read F10 ShortcutResult for the selected binding. Record the current selection version in the view. Repair is a new explicit action against the same binding, subject to F10 scope, receipt, and eligibility checks.
