# F24 implementation plan

Add a localized, owner-modal WPF guide beside Import MP4. Build the brief with resource strings and a pure formatter so it can be checked separately from the clipboard. The modal keeps concept, style and duration in memory. Generate from current fields at the moment the user copies or refreshes the brief. Show an actionable status if the clipboard is unavailable.

Use Orbit Ignition's authored scene as the reproducible sample. Verify both language layouts, clipboard contents, the same source through F22 and a real launch. The playback engine and media rules belong to F21 and F22.
