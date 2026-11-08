# F18 — Motion display area

## US1 · Choose the playback area

In Settings, a user can choose **Recommended**, **Compact centered**, **Large centered**, or **Full screen**. Save applies the choice to the next preview, test launch and managed shortcut launch, including with Studio closed. Cancel preserves the saved choice. Recommended uses the existing work-defined area. The explicit choices use the display under the pointer when the renderer opens.

## Requirements

- **FR-001** Provide four localized, keyboard-accessible choices and explain that they apply to the next playback.
- **FR-002** Draw compact and large centered windows within the selected monitor work area. Full screen fills that monitor, preserving the work's composition and escape controls.
- **FR-003** Store the global preference locally and independently of application data. Missing, unsupported or unreadable values use Recommended. Report a failed save. Keep existing app configuration, bindings and shortcuts readable and unchanged by a display-only save.

## Acceptance

- **SC-001** Save and preview each explicit size on Windows. Verify compact and large are distinct on a suitably large monitor and full screen matches the selected monitor bounds. Test Esc and cancel.
- **SC-002** Reopen Studio and check the choice persists. Verify the published launch path resolves the same preference. Verify missing and invalid values, other-profile isolation and unchanged application state bytes.

The setting controls the animation area globally. Target application window size and position belong to the target.
