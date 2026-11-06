# F11 Research Decisions

## R1 — Theme

**Decision** Use WPF Fluent from the pinned Windows Desktop runtime. Keep dynamic semantic resources across Studio views. Centralize WPF's experimental application `ThemeMode` access in `StudioTheme`, with a local WPF0001 suppression. Reapply the saved mode on the UI dispatcher after Windows high-contrast transitions.

**Rationale** The dictionary probe established semantic resource coverage. Actual Windows transitions exposed a missing palette after manually replacing the application System dictionary on contrast exit. The pinned WPF ThemeMode implementation supplies the System color dictionary. The production-linked probe then verified both System and Dark palettes before, during and after actual Windows high contrast. The API remains isolated and theme transition checks must be rerun on desktop runtime updates.

**Alternatives considered** Direct Fluent dictionary replacement, Windows registry polling and UISettings WinRT interop.

**Sources** [WPF Fluent usage](https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md), [pinned WPF ThemeManager](https://github.com/dotnet/wpf/blob/v10.0.14/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/ThemeManager.cs), [Windows high contrast](https://learn.microsoft.com/windows/win32/winauto/high-contrast-parameter).

## R2 — Preferences

**Decision** Compare the expected user preference snapshot inside the existing F02 update transaction, preserve schema2 and return the current preferences with conflicts. Keep the existing static list of allowed theme values in one place. Muting does not participate in selection of visual playback.

**Rationale** The schema already contains both fields. Compare-and-save preserves unrelated records and prevents silent overwrite after concurrent settings edits.

**Alternatives considered** An independent configuration file, schema migration, or saving each selection immediately.

## R3 — Diagnostics

**Decision** Append only closed typed events after the target outcome and animation cleanup. Use a nonblocking exclusive gate and private handle-relative no-replace generations. Reuse the F10 directory primitives. Keep a fixed stage name and no more than 128 retained events by append order. Current and previous generations each have a 32 KiB bound. Reject unknown, unreadable or conflicting files without changing launch result. The copy formatter consumes validated typed events only.

**Rationale** A strict contract prevents leaking paths, raw exceptions or command-line text. One bounded snapshot and a previous generation make rollover and interrupted writes observable without a new storage dependency. A fixed stage prevents unbounded orphan files.

**Alternatives considered** Unbounded append, text redaction after logging raw events, a dependency on remote telemetry and a new local database. The F10 receipt entity is not reused.
