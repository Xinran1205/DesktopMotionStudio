# F21 — Production MP4 playback

## User scenarios

1. A user can preview and bind a bundled video work using Studio. The work fits the selected stage and plays at the selected duration. Preview only renders the work.
2. The user opens a cold app via Test launch or a desktop shortcut. The app starts once after the video closes. Esc or a media error closes the player and releases native launch.

## Requirements

- Accept locally packaged H.264 MP4 with one video track and optionally one AAC track. Keep distinct video metadata and IPC versioning. Preview uses the same player as launch, with no target authority.
- Preserve aspect ratio with complete display or centered fill. Preserve global display-area choices, fixed 200 ms visual fades, default silence and global mute. Play once for the shorter of the configured duration and source duration. Support a configured duration of 1 to 10 seconds. A full 10 seconds of content fits its own budget, separate from initial decode and cleanup.
- Launch the cold target once after normal exit, skip or failure. A missing decoder, media or renderer still allows native launch. Respect Windows reduced-motion settings and existing-window reuse.
- The initial guaranteed source profile is 8-bit SDR H.264, up to 30 fps, up to 1920×1080 or 1080×1920, with square pixels, baked-in orientation and a maximum file size of 64 MiB. Validate the MP4 signature, track types and first frame. Reject unsupported rotation, color or timing metadata. With Windows animations off, show a silent still for roughly 350 ms before closing.
- Require a local, validated asset plus display, duration and mute settings. Report structured completion, skip, timeout and failure. Avoid sending target commands or profile access to the player.
- Keep the code-work manifest, IPC v5 and native request route intact. Design the video session independently and reuse the existing application launch gate at the orchestration boundary. Prevent the video worker from becoming an allowed target executable.

## Acceptance

- Open the real packaged bundle on Windows. Exercise horizontal and portrait sources, fit and fill, the global stage presets, audio and mute. Confirm no empty stage before a real first frame.
- Play a 10-second fixture with the fixed fades and verify the cold target starts after video cleanup. Measure first-frame time and close time separately. Keep the outer Test launch budget valid for the 30-second gate and the 15-second target wait.
- Verify Esc, reduced motion, unsupported codecs, missing/invalid media, decoder startup timeout, hung-player cleanup and already-open app reuse. Run the existing two works against the 0.19.0 baseline and verify their packages and native route.

Depends on the F20 evidence. The bundled fixture proves the route, while F23 owns the first curated work.
