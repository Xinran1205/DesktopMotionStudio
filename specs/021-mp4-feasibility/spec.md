# F20 — Local MP4 feasibility

## US1 · Verify the shortest real Windows path

As the maintainer, I can run a repeatable native playback probe against local H.264 MP4 test files. The overlay shows a decoded frame, scales it without distortion, applies fixed fades, and closes before a separate test application starts.

- **FR-001** Verify first decoded frame, finite playback, fit and fill composition, and fixed endpoint fades on Windows 11 x64.
- **FR-002** Show that Esc closes playback and that an invalid file or startup timeout releases the test target exactly once.
- **FR-003** Verify the probe independently of the installed app, its profile, the two shipped scene packages and existing playback source.
- **FR-004** Record the observed Windows media backend, dependencies and remaining production risks. Label the test footage as a fixture.

### Acceptance

- **SC-001** Generate local landscape and portrait test files. Decode and show six- and ten-second H.264 files on the desktop. Verify aspect ratios and fade values, and record actual first frame and close timestamps.
- **SC-002** Run natural end, keyboard skip and invalid-media fallback. A distinct fixture process starts once after renderer exit. A deadline covers renderer startup and playback.
- **SC-003** Verify version-controlled product source, tests and both shipped assets stay intact. Reproduction commands and actual limitations accompany the result.

F20 is an isolated feasibility feature. Production catalog, audio preference, actual shortcut integration and authored content follow their separate specs.
