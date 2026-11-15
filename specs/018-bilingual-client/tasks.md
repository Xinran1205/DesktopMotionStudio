# Tasks — F17 bilingual client

- [x] T001 Update `README.md` and create `README.zh-CN.md` with mutual links (FR-001, SC-001).
- [x] T002 Add embedded product text and separate language storage in `src/DesktopMotionStudio.Core/Localization/` (FR-002, FR-004).
- [x] T003 Wire Studio's native text and built-in scene names in `src/DesktopMotionStudio.Studio/` and translate playback controls in `src/DesktopMotionStudio.MotionHost/` (FR-003).
- [x] T004 Implement language preference save and startup selection in `SettingsWindow.xaml.cs` and `Program.cs` (FR-002, FR-004).
- [x] T005 Verify the settings, resource fallback, published Windows UI, persisted language and untouched user data. Record evidence and update the user guide (SC-002, SC-003).

## Convergence

- [x] T006 Localize the renderer's screen-reader window and fallback panel names, keeping native window identifiers stable (FR-003).
- [x] T007 Save the selected language independently of applying the active theme and report each failed operation accurately (FR-002, FR-004).
