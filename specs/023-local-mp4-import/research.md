# F22 design decisions

## Storage and publication

Use a local sibling directory derived from the exact Studio profile path. Create each draft as a private same-volume directory through Core's WindowsFileScope. Apply the destination's NTFS policy to the managed copy. Use the existing local-path checks on the read-only source so local removable media can be imported. Bounded copying and decoder inspection run outside the UI thread. A single no-replace publish makes the work visible. The scanner ignores all private staging and trash names before applying its catalog limit. Startup recovery verifies the staging marker and file identities before removing anything. A directory of uncertain ownership is ignored.

## Media inspection

Reuse VideoHost and the existing process deadlines. Give inspection an explicit request and terminal response distinct from v1 playback. Probe the staged copy with the Windows decoder in video-only mode, return its usable duration, and keep the stage and audio closed. This shares the working F21 profile checks and avoids doing native decoding in Studio. Preserve the v1 playback and native v5 paths.

## Catalog and launch

Studio and Launcher receive the same profile-derived imported root, including when an isolated fixture supplies a custom profile. Keep built-in and imported catalogs logically separate, use one resolver where a binding or shortcut asks for a work, and reserve the imported ID prefix. Launcher can resolve a namespaced ID without walking unrelated imports. A collision or unreadable imported work is unavailable. A missing import falls through to ordinary app launch using the existing shortcut.

## Edits and removal

Publish name and playback settings together as a single metadata record. Compare the observed revision before writing an edit. Keep the media and ID stable. On removal, acquire the verified work and ensure it is idle before moving it out of the published catalog. Cleanup uses verified expected artifacts. Existing app bindings retain their work ID and display an unavailable state.
