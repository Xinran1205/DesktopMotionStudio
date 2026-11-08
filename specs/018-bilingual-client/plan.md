# Implementation Plan — F17 bilingual client

Use English as the default README and link to the Chinese edition. Load Studio text from two embedded product resources, selected at process startup. Format interpolation arguments separately from the translated template so saved names and target details retain their values.

## Compatibility and data

The prior profile decoder rejects unknown members. Store an optional `<profile>.language` alongside the profile, using the closed tokens `zh-CN` and `en`. Missing, unreadable, or unsupported values fall back to Chinese. Use atomic writes to the preference file. Theme and mute stay in the existing state, with a specific error message if the language write fails.

Keeping both resource sets in Core's main assembly allows existing framework-dependent and self-contained staging to carry both languages. Studio uses the selected preference for its lifetime. English is passed to MotionHost using a fixed private command-line token. The default Chinese path keeps the original command line. The desktop Launcher reads the same preference when it starts, including when Studio is closed. No playback protocol or stored scene format changes.

## Implementation and verification

Localize XAML and code-behind first, then wire the language setting, built-in works and renderer messages. Resolve built-in display data from the packaged identities and original names. Verify the published WPF paths with isolated profile data, resource parity, the unchanged default-language workflows, and preservation of app state.

## Constitution check

Presentation changes preserve application dispatch, scene identities, existing files and renderer isolation. The local preference is the only new user data.
