# F19 verification — Windows candidate 0.19.0

Verified on Windows 11 x64. The unsigned candidate is in `artifacts/f19/verified-0.19.0/setup`. Setup SHA-256 is `5A81D6D98104B461ECE203CB618E32659B1EAA91EF4A9A3BFF0A37B6C5CA8C71`.

| Acceptance | Result |
|---|---|
| SC-001 · Real playback order | The real Zero Boundary renderer window was visible before the target process existed. The target dispatcher observed the renderer exited, 2,251 ms after first observing the window. Escape sent to the owned window closed it and released exactly one dispatch in the next 45 ms. |
| SC-001 · Product entry points | Both Zero Boundary and Studio Pulse completed on the actual Studio Test launch and Windows Shell Link routes. Each route opened its own fixture target. Warm Test launch reused the existing target. |
| SC-002 · Failure and concurrency | Real renderer crash, missing renderer, and a v3 renderer against the v5 launcher each released one native dispatch. Duplicate requests shared one target and one playback. An independently opened target was recognized after playback. An explicit cancellation during playback closed the renderer and dispatched zero targets. |
| SC-003 · Compatibility | Three display presets, bilingual Settings and all 13 preview checks passed on the final payload. Reduced-motion pre-launch completed early for both renderers. All seven isolated 0.17.0 → 0.19.0 lifecycle checks passed, including rollback and byte-exact preservation of the existing Shell Link and profile. |

The Core suite passed 555 checks and the Studio suite passed 81. MotionHost passed 26 checks. The optional real packaged-app case lacked an AUMID fixture. Independent review led to a second cancellation check after the process rescan and a native timing assertion measured at dispatch. Local logs are under `artifacts/f19`.

The supplemental F11 keyboard-only walkthrough could not obtain foreground focus for its temporary Studio window on this workstation. The settings save and cancel checks in the Studio suite, actual bilingual and placement dialogs, physical preview Escape and Tab+Enter checks, and the Zero Boundary Test launch keyboard entry completed.
