# F07 Data Model

No persistent fields change; configuration schema remains 1.

| Model | Fields / rules |
|---|---|
| ApplicationBinding | BindingId/AppId: nonblank, max 128; case-insensitive unique; SceneId passes manifest validator; Enabled boolean; one binding per app. |
| BindingMutationStatus | Applied, Unchanged, InvalidRequest, ApplicationMissing, BindingMissing, TargetUnavailable, SceneUnavailable, StorageError. |
| BindingResolutionStatus | Ready, Unbound, Disabled, ApplicationMissing, TargetUnavailable, SceneUnavailable, InvalidRequest, StorageError. |
| BindingLaunchContext | Current ApplicationProfile, original typed LaunchTarget, current validated SceneManifest; exists only for Ready. |
| BindingMutationResult | Status, optional binding, RepositoryError; successful only Applied/Unchanged. |
| BindingResolution | Status, optional profile/binding for diagnosis, optional context, RepositoryError. |
| RepositoryError | Existing values plus Busy at the end; no persisted representation changes. |

Transitions: unbound → enabled assignment; enabled/disabled → replacement (enabled, same ID); enabled ↔ disabled (same ID/scene); any binding → removal. Repeated same state is Unchanged. Missing external references do not mutate stored state.

Update invokes its callback once with the latest validated snapshot; callbacks build new arrays, never mutate loaded lists. Same-reference return is a no-op. Lock failure prevents callback invocation. Callback exception, invalid output, or write error cannot replace the original file.
