# Feature Specification — AI Motion Director V1.0

**Feature branch** `codex/ai-motion-director`

**Created** 2026-11-15

**Baseline** [F24 AI motion guide](../025-ai-motion-guide/spec.md)

## User Scenarios & Testing

### US1 · Generate a director instruction (P1)

Open AI Motion Director from Motion Library. Enter an application name and optionally a creative idea. Generate a standalone instruction for a web AI. A Docker-only entry delegates the concept to that AI. Adding a mechanical whale brief preserves the details as creative input.

**Independent test** Generate with Docker, Docker and a multiline idea, 微信 and 原神 in both interface languages. A blank app name, a disallowed control character or a field above its limit produces a localized error. After editing, generate again to replace the result.

### US2 · Take the exact instruction to another tool (P1)

Select text, copy the complete instruction, or download it through the native Markdown save dialog. The result being shown is the source for both actions.

**Independent test** Compare the displayed result with clipboard text and UTF-8 file bytes. Cancel the dialog and verify the destination remains empty. An occupied clipboard or failed file write reports how to retry. Existing files require the dialog's normal overwrite confirmation.

### Edge cases

- Preserve meaningful Unicode, quotes, backticks, braces and idea line breaks. Trim outer whitespace. Reject ill-formed text and report oversized input without truncation.
- Treat the application name as one line and the idea as optional multiline text. An empty idea uses an explicit creative-autonomy value.
- After an input edit, require generation before copy or download so an earlier result cannot be exported by mistake.

## Requirements

- **FR-001** Studio offers two inputs only. Application name is required and limited to 100 UTF-16 code units after trimming. Creative idea is optional and limited to 1,200 after trimming.
- **FR-002** Generate locally on demand from a versioned professional template. The generated instruction language follows Studio. A follows the Studio language. B is a brief synopsis and material handoff in Simplified Chinese. The video prompt in C is always English. D and E follow the instruction language.
- **FR-003** The template asks for app-specific brand research and uncertainty handling, a Hook → Development → Surprise → Brand Reveal story, concrete art direction and camera/action timing, continuity, a stable closing state and negative constraints. User text is delimited as data and cannot change the document structure.
- **FR-004** The template separates the video model's visual direction from technical export. Target 8 seconds, with 10 seconds maximum, 16:9 at 1920×1080, center-safe, SDR and no embedded long fades. Recommend H.264, 8-bit SDR, square pixels, 30 fps, at most 64 MiB, and optional AAC.
- **FR-005** Copy and UTF-8 Markdown download use exactly the current result. The Windows picker defaults to a safe `.md` name, offers standard overwrite confirmation and creates nothing on cancellation.
- **FR-006** Studio composes the instruction offline. The user explicitly chooses where to paste the text and how to produce the media.
- **FR-007 · Editorial revision 1.1** Every brief directs the external AI to verify the app and find a matching visual asset from an official source. B names the product, identifies the source and where to obtain the asset, specifies its visual anchors and identifies which actual file to supply to which input or editing layer. If official material cannot be verified, B states the gap and labels the result as a draft. User-provided material may be used with its provenance stated.
- **FR-008 · Editorial revision 1.1** The main action reflects the product or an app-specific interpretation of the user's idea. B recommends one production route based on the visual objective and known tool capabilities. C aligns with that route and contains only English model directions. When the platform is unknown, the recommendation is conditional and describes the required input. When only text generation is available, C produces the background or other generative elements and B explains the separate placement of exact visuals. A precise mark uses the original asset in editing. E identifies the three frames for a visual identity check.

### Key entities

- **Director inputs** validated application name and user idea. The composing operation owns neither application state nor creative assets.
- **Director instruction** one localized template plus one machine-readable input block. Clipboard and file exports derive from its exact text.

## Success criteria

- **SC-001** A user generates a complete two-field instruction in Chinese or English without supplying additional parameters. The template contains the five required outputs and an English model-ready prompt contract.
- **SC-002** All specified input, clipboard, save and cancellation scenarios pass. Unicode creative input survives round trips as data.
- **SC-003** The MP4 import, preview, binding and actual launch checks continue to pass against the same Studio source.
- **SC-004** The built and installed Chinese and English instructions include app and asset verification, an explicit routing and upload handoff, unverified-source behavior, and checks for subject continuity and an exact final mark. Review Docker with and without an idea, a different app, and a text-only or unavailable-source case. Local checks cover the delivered instructions. Rendered quality requires a separate trial in the selected external video tool.

## Assumptions

Interface language controls the template and D–E. B uses Simplified Chinese in both versions. A follows the interface language. Studio delivers the instructions for asset research and production. Response and render quality depend on the selected external tools and remain a separate acceptance run.
