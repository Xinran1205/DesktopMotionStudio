# Feature Specification — F17 English and Simplified Chinese

**Status**　Verified in 0.17.0 local candidate · **Branch**　`codex/f17-bilingual-client`

## User scenarios

1. A GitHub visitor opens the English README and switches to Chinese or back using the links at the top.
2. A Studio user chooses English or 简体中文 in Settings, saves, and reopens Studio. The chosen language persists and applies to product labels, messages, accessibility names, and built-in works.
3. The user can still find and launch previously saved applications. Their names, paths, launch arguments, bindings, and existing shortcuts retain their original values.

## Requirements

- **FR-001**　Provide a default English `README.md` and a Simplified Chinese `README.zh-CN.md`. Each links to the other with a GitHub-relative link.
- **FR-002**　Provide English and Simplified Chinese choices in Settings. Use Simplified Chinese when there is no supported stored preference. Show when the Studio language change takes effect.
- **FR-003**　Localize the product's own Studio labels and messages, accessibility text, built-in scene names and descriptions, and playback controls. Keep application metadata from users or Windows verbatim. Scene and binding IDs are stable.
- **FR-004**　Store the preference separately from versioned application data. Saving language alone leaves that data and shortcuts untouched. A missing or invalid preference falls back to Chinese and leaves launch available.

## Acceptance

- **SC-001**　Both README paths resolve and link to each other.
- **SC-002**　On Windows, switch to English and back, reopening the Studio each time. Verify the home, settings, library and an actual preview. A custom Chinese application name stays verbatim in both languages.
- **SC-003**　A saved change preserves profile and backup bytes, existing identifiers, and the rollback readability of the original profile. Verify missing and invalid preference handling.

## Inputs, outputs and edge cases

- Settings accepts one of two supported interface languages and saves a local preference for the next Studio session. The app displays a specific failure message if the preference cannot be written.
- The first run, an unreadable preference, and an unknown language token use Simplified Chinese. Cancel leaves the saved preference intact.
- A name such as `零界` can belong to both a saved app and an official built-in work. Only the built-in work is displayed as `Zero Boundary` in English.
- A managed shortcut can use the language preference when Studio is closed. The shortcut file and existing profile data keep their prior format.

## Dependencies and tests

The existing Studio settings, shipped works, and independent renderer provide the entry points. The Windows check exercises both language directions and a real preview against the published payload, using a temporary profile and a copy of its prior backup for comparison.

## Scope

The installer is a separate distribution surface. This feature does not rename shortcut files or translate user-entered text, target application metadata, technical protocol tokens, or records on disk.
