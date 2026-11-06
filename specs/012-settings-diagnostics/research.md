# F11 Research Decisions

## R1 — Theme

**Decision** Use the WPF Fluent resource dictionaries provided by the pinned Windows Desktop runtime. Select Fluent.xaml for system, Fluent.Light.xaml for light and Fluent.Dark.xaml for dark. Use dynamic semantic resources in all Studio views and system contrast colors where required. Probe the installed WPF runtime and resource changes before modifying windows.

**Rationale** The existing runtime includes PresentationFramework.Fluent. WPF follows app mode and contrast changes in the supported dictionary route. It avoids an experimental ThemeMode property, registry dependence and an added WinRT projection.

**Alternatives considered** Experimental ThemeMode, custom Windows registry polling and UISettings WinRT interop.

**Sources** [WPF Fluent usage](https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md), [Microsoft WPF Fluent documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90), [Windows high contrast](https://learn.microsoft.com/windows/win32/winauto/high-contrast-parameter).

## R2 — Preferences

**Decision** Compare the expected user preference snapshot inside the existing F02 update transaction, preserve schema2 and return the current preferences with conflicts. Keep the existing static list of allowed theme values in one place. Muting does not participate in selection of visual playback.

**Rationale** The schema already contains both fields. Compare-and-save preserves unrelated records and prevents silent overwrite after concurrent settings edits.

**Alternatives considered** An independent configuration file, schema migration, or saving each selection immediately.

## R3 — Diagnostics

**Decision** Append only closed typed events after the target outcome and animation cleanup. Use a nonblocking exclusive gate and private handle-relative no-replace generations. Reuse the F10 directory primitives. Keep a fixed stage name and no more than 128 retained events by append order. Current and previous generations each have a 32 KiB bound. Reject unknown, unreadable or conflicting files without changing launch result. The copy formatter consumes validated typed events only.

**Rationale** A strict contract prevents leaking paths, raw exceptions or command-line text. One bounded snapshot and a previous generation make rollover and interrupted writes observable without a new storage dependency. A fixed stage prevents unbounded orphan files.

**Alternatives considered** Unbounded append, text redaction after logging raw events, a dependency on remote telemetry and a new local database. The F10 receipt entity is not reused.
