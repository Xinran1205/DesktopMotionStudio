# F07 Binding Service Contract

- `Bind(appId, sceneId)` validates current registration, then typed target/scene when a change is needed; adds/replaces and enables. Replacement keeps BindingId. All same-state commands return Unchanged before external validation.
- `SetEnabled(appId, enabled)` needs an existing binding. Disabling needs no external providers; enabling validates both dependencies. Same value is Unchanged.
- `Remove(appId)` needs an existing application registration, removes only its binding; absence is Unchanged.
- `Resolve(appId)` is read-only with the precedence in plan.md and no context unless Ready.
- IDs are not trimmed into a different identity; invalid length/whitespace is rejected. App lookup is OrdinalIgnoreCase; scene ID follows F01.
- Target provider returns an explicit F04 LaunchTarget using the supplied current profile or null; it must not launch. Validation delegate also must not launch. Exceptions return TargetUnavailable. Catalog provider exceptions/missing root/no unique available match return SceneUnavailable.
- Providers are current snapshots and may later become stale. No stored scene version pin or inferred application type is added.
- Mutation results map repository failures to StorageError and retain the RepositoryError enum. No raw exception/path/window title is added to diagnostics.

## Store transaction

`IStudioStateStore.Update(Func<StudioState, StudioState>)` covers load→validation→transition→validation→save under a cross-process write mutex. Callback once; same state reference means no write; programmer exceptions propagate before save. Two-second lock timeout returns Busy. Every binding command uses Update; Save remains whole-snapshot replacement for callers that intentionally own a complete snapshot.
