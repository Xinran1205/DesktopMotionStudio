# F22 — Import local MP4 as a work

## User scenario

A user opens My motions, chooses a local MP4, sees the validation result and actual playback duration, then previews, saves and binds it. A completed import retains a local copy under an independent user directory.

## Requirements

- Inspect the actual media and decode a frame before allowing save. Unsupported, malformed, empty or oversized files report the reason. Cancel or an interrupted import leaves no visible partial work. Keep the original file byte-exact.
- Provide complete display or centered fill, duration from the start up to 10 seconds, and original sound on or off. The preview applies the chosen settings. Fixed fades are part of the video runtime.
- Keep a stable work ID for display-name and setting changes. Use managed local files and versioned metadata so the original can move and old settings remain readable. Removing a work makes affected bindings unavailable and preserves native launch.
- User video can have a custom display name. Keep internal storage identity independent of that name and the source file path. Media is read-only during playback. The app profile keeps its existing schema.
- Use a local, bounded staging copy and publish it only after validation. Cleaning up a failed import or a deleted work removes only files owned by that work. The import entry uses local files and does not transmit them.

## Acceptance

- Import a valid file, set all three choices, preview it and bind it to two apps. Move the source, restart Studio and verify both managed launches still work.
- Cancel and interrupt import, try invalid and unsupported files, try a collision and a disallowed path, and verify existing works, bindings, shortcuts and source bytes. Verify no broken work appears after cancellation or failure.
- Remove a saved work and confirm its bindings are reported as unavailable while managed launches fall back to the original target. Verify empty-state and failure messages in both interface languages.

Depends on F21 production playback.
