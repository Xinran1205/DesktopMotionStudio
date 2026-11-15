# Implementation plan — AI Motion Director V1.0

## Summary

Replace the F24 modal's live brief with explicit generation, sample input validation, and two exports of the current instruction. Retain the motion library entry point and existing Studio localization and WPF conventions.

## Technical context

Use .NET 10 and WPF with two embedded Markdown V1 templates, the existing bilingual resource managers, the Windows clipboard and a native save dialog. A pure composer validates the two inputs, writes a JSON data block, and returns the instruction plus its safe suggested filename. Keep template text separate from the view. The modal performs no network, logging or persistence beyond the user-selected export.

The prompt resource explicitly separates authority from user fields, story and model directions from encoding, and provides consistent output constraints across languages. The English instruction still asks for a Chinese synopsis. A required name and optional idea replace the prior style and duration controls. Duration defaults to 8 seconds and can vary only as directed by the creative prompt within 10 seconds.

## Constitution check

This is a local Studio-only feature. It needs no new library, privileges, profile migration, playback or launcher changes. Verify copy/export with an isolated desktop profile and the current media and target fixtures. Preserve F24 as historical evidence and link to this revision.

## Implementation and acceptance

1. Create the pure composer and standalone prompt resources. Verify input framing, limits, brand uncertainty, story and export details in both languages.
2. Replace the modal fields and add generation, clipboard and Markdown actions. Keep text selectable and avoid stale export actions after edits.
3. Exercise real Windows copy, save, overwrite refusal and cancel. Run the existing MP4 import and launch checks. Review the final template and UI independently, then reconcile the results with the spec.

## Editorial revision 1.1

Keep the two-field UI and the bilingual A–E contract. In both embedded templates, replace optional research with a verifiable asset handoff. Select a single route for the concept and target tool, using compositing for any exact mark. Keep platform instructions out of C. Update the window help and user documentation to match B → C → E. Add focused contract checks alongside the existing copy, save and input checks. Build and smoke-test the normal installer against the existing local installation.
