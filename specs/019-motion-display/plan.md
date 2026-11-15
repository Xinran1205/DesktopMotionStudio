# Plan — F18 motion display area

Store an allow-listed token beside the profile, reusing the bounded atomic writer for language. The stable tokens are `recommended`, `compact`, `large`, and `full`. Both Studio and Launcher use the same resolver at each MotionHost start, so a save takes effect without restarting Studio. Preserve the placement behavior of old work manifests.

Add three effective placement values to the renderer request and version the IPC. Work files still author their existing two placement values. Recommended preserves the old per-work defaults. Explicit layouts use the pointer monitor with bounded centered sizes or full bounds. Animation failure continues through the existing launch fallback.

Settings localizes the new controls using the existing two dictionaries. The same published payload must be verified in Windows preview and managed launch. No new dependencies or installer behavior.
