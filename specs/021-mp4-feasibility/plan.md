# Plan — F20 local MP4 feasibility

Use an independent Windows probe and generated geometry footage. Use a Windows-native decoder with an explicit first-frame signal. Keep the stage hidden until the first frame is usable. The probe owns an independently bounded child, measures overlay close before launching an isolated target, and records repeatable evidence without modifying application state.

Start with Media Foundation's playback clock and video frame transfer. Prove bounded decode startup, correct aspect ratio and fixed fades on one real workstation before choosing the production renderer. Resolve the minimum audio behavior in a follow-up integration. Tooling used solely to generate fixtures stays out of the app payload.

Preserve 0.19.0 shipped scene bytes, IPC v5 and both existing render paths. No elevation, global hooks, actual desktop shortcuts, network playback or installer operations.
