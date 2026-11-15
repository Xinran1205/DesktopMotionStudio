# AI Motion Director V1.0 acceptance

Windows desktop source build from the GitHub main baseline `b3be28b`. Release build succeeded with zero warnings and errors.

| Check | Observed |
| --- | --- |
| Composer | Passed in Chinese and English. The two Docker examples, 微信, 原神, an emoji, internal line breaks, quotes, braces and a Markdown fence round-tripped. Blank, over-limit, invalid control and malformed UTF-16 input produced validation errors. Both templates have ordered A–E sections, a Chinese synopsis, an English video prompt and the compatible MP4 export profile. |
| Windows UI | Both interface languages passed explicit generation, regeneration after edit, real clipboard equality and UTF-8 file equality. The file dialog suggested a Markdown filename, Cancel created nothing, and declining replacement preserved the existing file. The test profile stayed byte-identical. |
| Studio regression | All 81 existing Studio checks passed. The Windows MP4 import UI check passed. The Orbit Ignition sample passed import, bound preview, launch to two targets through Test launch and a real shortcut, warm reuse, rename and native fallback after removal. |
| Review | Independent review checked input framing, real output ownership and both template languages. The final templates specify H.264 profiles, yuv420p, baked orientation and preview of the exported file. Current Studio code composes and exports local text without additional runtime dependencies. |

Local checks validate the instruction, clipboard and saved file. They do not evaluate responses from ChatGPT, DeepSeek, or a video model. The product displays a retry message for clipboard contention or a failed save. This run exercised successful copy and save, cancel and overwrite refusal.

Local logs are under `artifacts/f24-director`. See the [reproduction steps](quickstart.md).

## Revision 1.1 · Installed build 0.24.1

| Check | Observed |
| --- | --- |
| Prompt contract | The Chinese and English composer checks passed. Both embedded resources require official research on familiar apps, concrete REF-01 assets, a single matching production route, a draft when source verification is missing, and a three-frame identity comparison. The localized output and MP4 export contract also passed. |
| Editorial walkthrough | Docker alone requires verification of the product and a container-based action. A moving Docker whale brief requires an appropriate subject reference and a route that preserves the requested motion, with a separate exact-mark treatment if specified. A different app requires its own source and action. A text-only tool receives instructions for the generated elements and a separate original-asset layer. Without source verification, the handoff identifies the missing file and labels the result a draft. These are contract checks, based on reading the templates. |
| Release regression | Core reported 526 passes and five skips for optional configured-host or packaged-app checks. The 81 Studio checks passed. The Windows MP4 import, preview and save check passed. The shipped MP4 passed eight desktop launch checks, including delayed launch, warm reuse and the real shortcut. The initial launcher fixture was framework-dependent. Rerunning with the documented self-contained fixture passed. |
| Installed upgrade | The Windows installer upgraded 0.24.0 to 0.24.1. Nine user-data and Start menu files remained byte-identical, and four installed files matched the payload. The existing Start menu shortcut opened 0.24.1. Against the installed executable, both languages passed generation, clipboard comparison, UTF-8 export, cancel and overwrite refusal. |

External video rendering was paused at the user's request. The installed verification covers the local instructions and production handoff. It does not certify fidelity or creative quality in an external video model.

The installable package and installed verification log are under `artifacts/f24-director/local-install/final-0.24.1`. Core, Studio and media regression logs are in the sibling `0.24.1` directory.
