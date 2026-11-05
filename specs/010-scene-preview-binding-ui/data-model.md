# F09 Data Model

## SceneLibraryItem

Stable F03 catalog item/manifest, optional ScenePresentation, validated ScenePlaybackDefinition, separate catalog/presentation/playback diagnostics. `IsAvailable` retains file integrity; `IsPlayable` additionally requires supported valid playback. Selection uses stable sceneId and current validated version, not list index or display name. Library snapshot is immutable and bounded to 256 directories.

## ScenePresentation schema 1

Required schemaVersion, matching sceneId/sceneVersion, description (0–1000 chars), style/theme (0–120 chars each), optional cover (safe scene-relative PNG/JPEG path). File <=64 KiB, JSON depth <=8, duplicate/unknown members rejected, no control characters in displayed metadata. Missing/corrupt/future/mismatched presentation yields core-name/fallback diagnostic. Optional cover is not a manifest-required asset; bounded read/decode failure falls back.

## ScenePlaybackDefinition schema 1

Required matching scene identity/version, runtimeId=`vector-orbit`, runtimeVersion=1, durationMilliseconds 1000–8000, accent `#RRGGBB`, glyph (1–8 visible chars), 2–8 keyframes. Keyframes have atMilliseconds, scale (0.1–1.5), opacity (0–1), rotationDegrees (-360–360); all numbers finite, times strictly increase from 0 to duration. Serialized validated snapshot <=4 KiB. Unknown schema/runtime is unavailable. Optional future asset-based capabilities require a new supported runtime contract, never arbitrary module loading.

SceneTimeline evaluates a validated immutable definition at clamped elapsed time using fixed interpolation. Reduced-motion evaluation holds scale/rotation constant and emits a stable visible frame until completion. Viewport and mode never change the authored timeline. Preview injects a labelled simulated handoff near the end; real launch may interrupt on actual target Ready.

## PreviewRequest / session

RequestId, immutable playback snapshot, display mode from manifest and Mode=Preview; no appId, profile, executable path, launch arguments, target PID/HWND or dispatcher. Session states: Idle → Starting → Playing → Completed/Skipped/Stopped/Failed → Disposed. Coordinator cancels/closes previous session before acquiring a new slot, generation-checks callbacks, and exposes actual cleanup failures. Owned job belongs only to its host.

## Binding selection

Selected sceneId + existing appId; confirmation calls F07 transaction with current eligibility. One current binding per app, stable BindingId on replace; same enabled selection is unchanged. Cancel/search/preview are read-only. Deleted/legacy-unconfirmed target or unavailable current work fails without altering prior bytes. No new persisted profile schema or UI-selection fields.
