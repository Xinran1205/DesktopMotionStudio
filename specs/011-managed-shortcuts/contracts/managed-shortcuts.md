# F10 Interface Contract

- Shell command. Exact two arguments in order, --binding and the canonical encoded binding ID described in the data model. No path, executable arguments, shell commands or scene IDs in the shortcut argument field. The executable is the bundled Launcher.
- Launcher result. Exit 0 for successful target dispatch or existing instance handling, 2 for malformed CLI, and nonzero for profile, missing binding/target or target launch failure. User-facing failures use bounded status text without target arguments. Render failures are separate from launch success.
- Studio. Selected saved app with eligible binding can Create. Missing or stale owned entries can Repair. Verified or confirmed missing entries can Remove. Shortcut status is independent of binding status. Busy, unsupported folder, unreadable receipt and occupied or modified path have distinct feedback. Updates use the originally selected binding key.
- Platform adapter. Open a checked local desktop, stage a link, read/validate proof, publish with no replace, and delete through the verified open handle. Status reads are nonmutating. Filesystem capability failures return a diagnostic outcome without guessing ownership.
- Release layout. Studio and Scenes at publish root, MotionHost and Launcher in separate child directories. Launcher resolves trusted Scenes and MotionHost relative to its bundle location.
