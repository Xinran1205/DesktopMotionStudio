# Plan — F21 production MP4 playback

Start from the F20 Media Engine frame-transfer evidence. Reuse the decoder and stage behavior behind a separate video session. Keep the current code-work manifests, renderer program and v5 request route. Add a video-specific request and published worker at the existing orchestration boundary. Route the bundled video fixture through Studio's Preview and the app gate without granting the worker access to app commands, desktop shortcuts or the profile.

Establish an independent first-frame, clip and cleanup budget. Start with a total renderer budget that leaves headroom inside the existing 60-second Test launch wait, which includes the 30-second app gate and 15-second target discovery budgets. Measure from the actual media and stage events. Define how the media clock is aligned before showing the first frame, how audio is kept muted until visible, and how a slow or missing decoder releases native launch. Keep exact allow-lists and test clean rejection of unsupported files and old worker versions.

Use the window placement preference and monitor sizing already available from F18. Apply fit/fill inside that window. Keep the renderer and playback ownership outside Studio. Check performance and correct audio on 1080p fixtures on the workstation. WIC remains a candidate only while it meets the measured goal. A GPU surface is a follow-up decision if it does not.

Validate with a test-only packaged video fixture. F22 adds the import UI and F23 ships curated content. For release, record actual package evidence and regress both native works against 0.19.0.
