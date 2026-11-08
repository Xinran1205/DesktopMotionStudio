# F22 — Import local MP4 as a work

## User scenarios

- **US1 · Import and preview.** A user opens My motions, chooses a local MP4, sees the validation result and actual playback duration, then previews and saves it. A completed import retains a local copy under an independent user directory.
- **US2 · Bind and manage.** A user binds a saved video to multiple apps, can change its name or settings, and can remove it. A renamed work continues to play from its existing bindings.

## Requirements

- Inspect the actual media and decode a frame before allowing save. Unsupported, malformed, empty or oversized files report the reason. Cancel or an interrupted import leaves no visible partial work. Keep the original file byte-exact.
- Provide complete display or centered fill, duration from the start up to 10 seconds, and original sound on or off. The preview applies the chosen settings. Fixed fades are part of the video runtime.
- Keep a stable work ID for display-name and setting changes. Use managed local files and versioned metadata so the original can move and old settings remain readable. Removing a work makes affected bindings unavailable and preserves native launch.
- User video can have a custom display name. Keep internal storage identity independent of that name and the source file path. Media is read-only during playback. The app profile keeps its existing schema.
- Use a local, bounded staging copy and publish it only after validation. Cleaning up a failed import or a deleted work removes only files owned by that work. The import entry uses local files and does not transmit them.
- Open the import form with complete display, original sound off, and playback duration set to the shorter of the media and ten seconds. The user can select any supported duration from one second up to that maximum. Require a readable regular local MP4 and enforce F21's 64 MiB and media-profile limits.
- A playable draft can be previewed and discarded without publishing. Its preview obeys global mute, Windows reduced motion and display-area preferences. The source can move after its private copy completes.
- Show an actionable localized error for invalid media, unavailable decoder, no space, unsafe path, pending edits, or a work still playing. Leave previously saved work usable if a new edit or removal cannot commit.
- Imported display names preserve their entered language. Use a separate My motions section and keep built-in works available when a single imported work is invalid or the imported library cannot be read.

## Acceptance

- Import a valid file, set all three choices, preview it and bind it to two apps. Move the source, restart Studio and verify both managed launches still work.
- Cancel and interrupt import, try invalid and unsupported files, try a collision and a disallowed path, and verify existing works, bindings, shortcuts and source bytes. Verify no broken work appears after cancellation or failure.
- Remove a saved work and confirm its bindings are reported as unavailable while managed launches fall back to the original target. Verify empty-state and failure messages in both interface languages.
- Verify the default options, the 1-second lower boundary, truncation of a source longer than ten seconds, global mute, and rapid preview/cancel/save. Confirm that changing a name or playback settings preserves the work ID across restart.

Depends on F21 production playback.
