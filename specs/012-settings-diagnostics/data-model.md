# F11 Data Model

## Profile preferences

Use existing profile2 Settings. Theme is exactly system, light or dark. Muted is a Boolean audio preference. Saving uses the expected original preferences and desired preferences. If desired equals current, return Unchanged without writing. Otherwise a stale expected value produces Conflict. A successful save preserves all application and binding fields. Unsupported state is reported and preserved.

## Diagnostic event schema1

A closed event contains schema version, UTC timestamp, randomly generated request GUID, bounded four-part numeric component version, Windows build integer, adapter category, stage, result, fallback reason and integer elapsed milliseconds. No user text, paths, titles, arguments, app/binding/scene identifiers, HWND, screenshot or raw exception. All enums reject undefined values. Each elapsed value is 0–120000 ms.

The private store retains at most 128 current records. Use at most 32 KiB per generation and discard oldest events by append order. Use a fixed next name for staging. After validating the primary, previous and stage, retire an older verified previous generation before allocating a new stage when a primary exists. At most two product generations occupy up to 64 KiB throughout handover. A missing primary recovers from the previous generation. Unknown, malformed, oversized or inaccessible files yield a structured diagnostic state and retain original bytes. Co-operating writers take one nonblocking cross-process lock. Diagnostic read and write failures leave application launch results unchanged.

## Recovery summary

Read F10 ShortcutResult for the selected binding. Record the current selection version in the view. Repair is a new explicit action against the same binding, subject to F10 scope, receipt, and eligibility checks.
