# Plan — F22 local MP4 import

Add My motions to Studio and use the same playback and target gate that F21 verified. The user selects a local MP4, works on a private copy, and publishes it from Studio after the player decodes a real frame. The UI, local store and player stay separate.

## Technical context and constitution

Use the existing .NET 10 Core, WPF Studio and separate Windows 11 x64 VideoHost. Keep both built-in works and their v5 protocol. Store imported content under the selected profile's private motion directory. Reuse the handle-verified local file primitives and keep the current profile and shortcut formats. No new runtime dependencies.

The design passes the project constitution gates for local data, least privilege, explicit ownership and launch fallback. Studio owns import and mutation, while Launcher only resolves and reads works. A malformed imported store cannot disable the built-in library. Review that boundary again after integration.

## Implementation order

1. Verify staged copy, atomic publish and restart recovery with isolated profiles. Define the import metadata and typed results in Core.
2. Add a bounded hidden inspection request to VideoHost. Reuse F21 media validation and decode at least one actual frame. Confirm Windows returns a usable duration and the worker exits without creating a stage or audio output.
3. Build the Studio flow from select and inspect to preview and save. Keep drafts separate from the catalog. Localize control labels and errors in English and Chinese.
4. Use one Core catalog resolver for Studio, binding eligibility, shortcut health and Launcher. Publish only imported IDs from the reserved namespace. Add edit and remove with conflict detection. Existing shortcuts continue through the F21 launch gate.
5. Run the narrow storage and protocol checks, then the real isolated Windows scenario in the quickstart. Review and converge before packaging the feature.

See the [design decisions](research.md), [storage model](data-model.md), [interaction contract](contracts/import-flow.md) and [verification path](quickstart.md).
