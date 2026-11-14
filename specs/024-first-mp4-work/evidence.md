# F23 Windows acceptance

Orbit Ignition / 轨道点火 is an original silent six-second launch sequence. The independent review decoded all 180 frames and checked the four storyboard frames for composition. The 0.22.0 payload contains the same clip and cover. On the authoring workstation, a second run reproduced both hashes.

| Asset | SHA-256 |
| --- | --- |
| MP4 | `577F76FA82B2BF7F0D58E71AF997DE8A51177536623E5CC0D682487A329AF518` |
| Cover | `982C96F83CA6CB326882D2312AD86D1D8CE32EB786CCF7E03D7878C43CE8CD02` |

The development encoder was FFmpeg `9.0.2-essentials_build-www.gyan.dev` from the F20 fixture toolchain, SHA-256 `3256173F3F8BFFD7DF12227C68ADF68025EDB1832273A9530688A7BB1ED8EDEC`. The clip is progressive H.264 High, 1920×1080, 30 fps, 8-bit 4:2:0 and silent. The installer contains the final clip and artwork. See the [authoring instructions](quickstart.md) and [asset provenance](../../src/DesktopMotionStudio.Studio/Scenes/orbit-ignition/ATTRIBUTION.md).

The packaged Windows player inspected and imported the exact shipped MP4. Studio showed Chinese and English catalog text, full-screen and compact stages, and accepted focused Esc in both. Test launch and a real desktop shortcut dispatched their targets after playback. Warm launch reused the existing window. Both original code works passed the preview regression.

The review led to compact-window coverage and an export workflow that publishes the cover and clip together after encoding succeeds. The candidate and checksum are under `artifacts/f23/release-0.22.0/setup`. The contact sheet and test traces are under `artifacts/f23`.
