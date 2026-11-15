# F11 Interfaces

- Preferences Read returns current settings or repository error. Apply takes the original and desired preference records, returns Applied, Unchanged, Conflict, Invalid or StorageError. Status includes current settings when readable. Existing profile2 bytes are preserved on a failed or unchanged transition.
- Theme resolution accepts the persisted choice, high-contrast state and system app color. High contrast selects system colors first. System color lookup failure falls back to light. Notification and rendering run on the Studio dispatcher.
- Diagnostic Append accepts closed typed fields and returns stored, busy, incompatible, invalid or I/O state. It never receives a raw exception or user payload. Read returns validated records and status. The Launcher keeps its original exit result even if append fails.
- Diagnostic summary shows build, current render capability, newest results and recovery suggestions. Copy and open-directory are explicit local actions. Repair delegates to the selected binding's F10 repair entry.
