# Tasks — F22 local MP4 import

## Phase 1 · Setup and first risk checks

- [x] T001 Add isolated import fixtures and exercise interrupted copy, no-replace publication and identity-based cleanup in `tests/DesktopMotionStudio.Core.Checks/F22ImportChecks.cs`. Reuse `src/DesktopMotionStudio.Core/IO/WindowsFileScope.cs` to prove the storage boundary.
- [x] T002 Verify a stage-free first-frame inspection of horizontal, vertical and malformed MP4s using the packaged worker in `src/DesktopMotionStudio.VideoHost/Program.cs` and `tests/DesktopMotionStudio.Studio.Checks/F22ImportChecks.cs`. Keep video wire v1 playback compatibility.

## Phase 2 · Foundations

- [x] T003 Implement strict metadata and typed errors in `src/DesktopMotionStudio.Core/Video/ImportedVideoWork.cs`. Validate the reserved `user-mp4-<32 lowercase hexadecimal digits>` ID, positive revision, trimmed name of 1 to 80 characters without control characters, Boolean sound, and fit of `complete` or `fill`. Bound duration from 1,000 to the smaller of 10,000 and verified source duration.
- [x] T004 Implement profile-derived staging and catalog reading in `src/DesktopMotionStudio.Core/Video/ImportedVideoStore.cs`. Copy only regular local files up to 64 MiB. Inspect the staged copy, publish once without replacing existing work, and retain only `clip.mp4` and `work.json` after successful publish. Recover only identity-verified private artifacts.

## Phase 3 · US1 import and preview

- [x] T005 [US1] Implement the My motions import editor in `src/DesktopMotionStudio.Studio/ImportMotionWindow.xaml` and its code-behind. Default to complete display, sound off and the maximum allowed duration. Localize controls and typed errors in `src/DesktopMotionStudio.Core/Localization`.
- [x] T006 [US1] Wire draft preview, cancellation and save through `src/DesktopMotionStudio.Studio/SceneLibraryWindow.xaml.cs`, `SceneWorkspace.cs` and the existing preview coordinator. Stop the owned preview before publishing or discarding its files. Check source immutability and persistence after moving the original.

## Phase 4 · US2 bind and manage

- [x] T007 [US2] Connect the combined catalog to binding eligibility, shortcut health and launch resolution in `src/DesktopMotionStudio.Core/Shortcuts/ManagedLaunchRequest.cs`, `src/DesktopMotionStudio.Studio/SceneWorkspace.cs` and both app entry points. Verify real Test launch, one Shell Link, warm reuse and missing-import fallback.
- [x] T008 [US2] Implement revision-checked edits and verified removal in `src/DesktopMotionStudio.Core/Video/ImportedVideoStore.cs` and `src/DesktopMotionStudio.Studio/SceneLibraryWindow.xaml.cs`. Test a pending preview or launch, unknown schema and foreign files. Preserve both binding IDs and receipt bytes.

## Phase 5 · Delivery

- [x] T009 Run the [Windows acceptance path](quickstart.md), F21 worker checks and the relevant Core and Studio regressions. Record independent review, evidence and convergence under `specs/023-local-mp4-import`.

T001 and T002 retire the principal risks before UI work. T003 precedes T004. US1 uses T002–T004, and US2 uses the published catalog and existing launch route. Each storage or UI failure keeps the previous committed state.

## Consistency check

US1 has bounded copy, decoded-frame validation, cancellation, the three user choices and save coverage in T001–T006. US2 has stable ID, edit, removal, native fallback and binding coverage in T003, T007 and T008. T009 covers both interface languages, compatibility and the packaged Windows route. Existing app profile and native protocol schemas stay in use.
