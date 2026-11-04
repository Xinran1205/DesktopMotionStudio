using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Core.Bindings;

public sealed class BindingService
{
    private readonly IStudioStateStore _store;
    private readonly Func<SceneCatalogScanResult> _catalogProvider;
    private readonly Func<ApplicationProfile, LaunchTarget?> _targetProvider;
    private readonly Func<LaunchTarget, LaunchTargetResolution> _validateTarget;

    public BindingService(
        IStudioStateStore store,
        Func<SceneCatalogScanResult> catalogProvider,
        Func<ApplicationProfile, LaunchTarget?> targetProvider,
        Func<LaunchTarget, LaunchTargetResolution>? validateTarget = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _catalogProvider = catalogProvider ?? throw new ArgumentNullException(nameof(catalogProvider));
        _targetProvider = targetProvider ?? throw new ArgumentNullException(nameof(targetProvider));
        _validateTarget = validateTarget ?? new LaunchTargetIdentityResolver().Resolve;
    }

    public BindingMutationResult Bind(string appId, string sceneId)
    {
        if (!ValidAppId(appId) || !SceneManifestReader.IsValidSceneId(sceneId))
            return new(BindingMutationStatus.InvalidRequest);
        return Mutate(appId, (state, app, binding) =>
        {
            if (binding is { Enabled: true } && string.Equals(binding.SceneId, sceneId, StringComparison.OrdinalIgnoreCase))
                return (state, new(BindingMutationStatus.Unchanged, binding));
            if (GetTarget(app) is null) return (state, new(BindingMutationStatus.TargetUnavailable, binding));
            if (GetScene(sceneId) is null) return (state, new(BindingMutationStatus.SceneUnavailable, binding));
            var next = new ApplicationBinding(binding?.BindingId ?? Guid.NewGuid().ToString("N"), app.AppId, sceneId, true);
            ApplicationBinding[] bindings = state.Bindings.Where(item => !SameApp(item.AppId, appId)).Append(next).ToArray();
            return (state with { Bindings = bindings }, new(BindingMutationStatus.Applied, next));
        });
    }

    public BindingMutationResult SetEnabled(string appId, bool enabled)
    {
        if (!ValidAppId(appId)) return new(BindingMutationStatus.InvalidRequest);
        return Mutate(appId, (state, app, binding) =>
        {
            if (binding is null) return (state, new(BindingMutationStatus.BindingMissing));
            if (binding.Enabled == enabled) return (state, new(BindingMutationStatus.Unchanged, binding));
            if (enabled)
            {
                if (GetTarget(app) is null) return (state, new(BindingMutationStatus.TargetUnavailable, binding));
                if (GetScene(binding.SceneId) is null) return (state, new(BindingMutationStatus.SceneUnavailable, binding));
            }
            ApplicationBinding next = binding with { Enabled = enabled };
            ApplicationBinding[] bindings = state.Bindings.Select(item => SameApp(item.AppId, appId) ? next : item).ToArray();
            return (state with { Bindings = bindings }, new(BindingMutationStatus.Applied, next));
        });
    }

    public BindingMutationResult Remove(string appId)
    {
        if (!ValidAppId(appId)) return new(BindingMutationStatus.InvalidRequest);
        return Mutate(appId, (state, _, binding) => binding is null
            ? (state, new(BindingMutationStatus.Unchanged))
            : (state with { Bindings = state.Bindings.Where(item => !SameApp(item.AppId, appId)).ToArray() }, new(BindingMutationStatus.Applied)));
    }

    public BindingResolution Resolve(string appId)
    {
        if (!ValidAppId(appId)) return new(BindingResolutionStatus.InvalidRequest);
        RepositoryResult loaded = _store.Load();
        if (!loaded.IsSuccess) return new(BindingResolutionStatus.StorageError, RepositoryError: loaded.Error);
        StudioState state = loaded.State!;
        ApplicationProfile? app = state.Applications.FirstOrDefault(item => SameApp(item.AppId, appId));
        if (app is null) return new(BindingResolutionStatus.ApplicationMissing);
        ApplicationBinding? binding = state.Bindings.FirstOrDefault(item => SameApp(item.AppId, appId));
        if (binding is null) return new(BindingResolutionStatus.Unbound, app);
        if (!binding.Enabled) return new(BindingResolutionStatus.Disabled, app, binding);
        LaunchTarget? target = GetTarget(app);
        if (target is null) return new(BindingResolutionStatus.TargetUnavailable, app, binding);
        SceneManifest? scene = GetScene(binding.SceneId);
        if (scene is null) return new(BindingResolutionStatus.SceneUnavailable, app, binding);
        return new(BindingResolutionStatus.Ready, app, binding, new(app, target, scene));
    }

    private BindingMutationResult Mutate(
        string appId,
        Func<StudioState, ApplicationProfile, ApplicationBinding?, (StudioState State, BindingMutationResult Result)> transition)
    {
        BindingMutationResult? result = null;
        RepositoryResult stored = _store.Update(state =>
        {
            ApplicationProfile? app = state.Applications.FirstOrDefault(item => SameApp(item.AppId, appId));
            if (app is null)
            {
                result = new(BindingMutationStatus.ApplicationMissing);
                return state;
            }
            ApplicationBinding? binding = state.Bindings.FirstOrDefault(item => SameApp(item.AppId, appId));
            (StudioState next, BindingMutationResult outcome) = transition(state, app, binding);
            result = outcome;
            return next;
        });
        return stored.IsSuccess
            ? result ?? new(BindingMutationStatus.StorageError, RepositoryError: RepositoryError.InvalidState)
            : new(BindingMutationStatus.StorageError, RepositoryError: stored.Error);
    }

    private LaunchTarget? GetTarget(ApplicationProfile app)
    {
        try
        {
            LaunchTarget? target = _targetProvider(app);
            return target is not null && _validateTarget(target).Succeeded ? target : null;
        }
        catch (Exception) { return null; }
    }

    private SceneManifest? GetScene(string sceneId)
    {
        try
        {
            SceneCatalogScanResult catalog = _catalogProvider();
            if (!catalog.RootAvailable) return null;
            SceneCatalogItem[] matches = catalog.Items.Where(item =>
                item.Manifest is not null && string.Equals(item.Manifest.SceneId, sceneId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || !matches[0].IsAvailable) return null;
            SceneManifest scene = matches[0].Manifest!;
            return scene.SchemaVersion == 1 && SceneManifestReader.IsValidSceneVersion(scene.SceneVersion) &&
                   scene.DisplayMode is SceneDisplayMode.Full or SceneDisplayMode.Centered ? scene : null;
        }
        catch (Exception) { return null; }
    }

    private static bool SameApp(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool ValidAppId(string? appId) => !string.IsNullOrWhiteSpace(appId) && appId.Length <= 128;
}
