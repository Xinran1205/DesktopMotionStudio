using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Core.Applications;

public enum ApplicationRegistryStatus { Applied, Unchanged, Duplicate, InvalidRequest, ApplicationMissing, TargetUnavailable, StorageError }

public sealed record ApplicationRegistryResult(
    ApplicationRegistryStatus Status, ApplicationProfile? Profile = null, RepositoryError RepositoryError = RepositoryError.None)
{
    public bool Succeeded => Status is ApplicationRegistryStatus.Applied or ApplicationRegistryStatus.Unchanged;
}

public sealed class ApplicationRegistry
{
    private readonly IStudioStateStore _store;
    private readonly Func<LaunchTarget, LaunchTargetResolution> _validate;

    public ApplicationRegistry(IStudioStateStore store, Func<LaunchTarget, LaunchTargetResolution>? validate = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validate = validate ?? new LaunchTargetIdentityResolver().Resolve;
    }

    public RepositoryResult Load() => _store.Load();
    public static LaunchTarget? GetTarget(ApplicationProfile profile) => profile.Target?.ToLaunchTarget();

    public ApplicationRegistryResult Add(string displayName, ApplicationTargetDefinition target)
    {
        if (!ValidName(displayName) || target is null || !target.IsValid) return new(ApplicationRegistryStatus.InvalidRequest);
        ApplicationTargetDefinition snapshot = target.Snapshot();
        ResolvedLaunchTarget? resolved = Resolve(snapshot);
        if (resolved is null) return new(ApplicationRegistryStatus.TargetUnavailable);
        return Mutate(state =>
        {
            ApplicationProfile? duplicate = FindDuplicate(state, resolved);
            if (duplicate is not null) return (state, new(ApplicationRegistryStatus.Duplicate, duplicate));
            if (state.Applications.Count >= 1000) return (state, new(ApplicationRegistryStatus.InvalidRequest));
            var profile = new ApplicationProfile(Guid.NewGuid().ToString("N"), displayName, snapshot.Identifier, snapshot);
            return (state with { Applications = state.Applications.Append(profile).ToArray() }, new(ApplicationRegistryStatus.Applied, profile));
        });
    }

    public ApplicationRegistryResult Rename(string appId, string displayName)
    {
        if (!ValidId(appId) || !ValidName(displayName)) return new(ApplicationRegistryStatus.InvalidRequest);
        return Mutate(state =>
        {
            ApplicationProfile? profile = Find(state, appId);
            if (profile is null) return (state, new(ApplicationRegistryStatus.ApplicationMissing));
            if (profile.DisplayName == displayName) return (state, new(ApplicationRegistryStatus.Unchanged, profile));
            ApplicationProfile next = profile with { DisplayName = displayName };
            return (Replace(state, next), new(ApplicationRegistryStatus.Applied, next));
        });
    }

    public ApplicationRegistryResult Repair(string appId, ApplicationTargetDefinition target)
    {
        if (!ValidId(appId) || target is null || !target.IsValid) return new(ApplicationRegistryStatus.InvalidRequest);
        ApplicationTargetDefinition snapshot = target.Snapshot();
        ResolvedLaunchTarget? resolved = Resolve(snapshot);
        if (resolved is null) return new(ApplicationRegistryStatus.TargetUnavailable);
        return Mutate(state =>
        {
            ApplicationProfile? profile = Find(state, appId);
            if (profile is null) return (state, new(ApplicationRegistryStatus.ApplicationMissing));
            ApplicationProfile? duplicate = FindDuplicate(state, resolved, profile.AppId);
            if (duplicate is not null) return (state, new(ApplicationRegistryStatus.Duplicate, duplicate));
            if (SameStored(profile.Target, snapshot)) return (state, new(ApplicationRegistryStatus.Unchanged, profile));
            ApplicationProfile next = profile with { Target = snapshot };
            return (Replace(state, next), new(ApplicationRegistryStatus.Applied, next));
        });
    }

    public ApplicationRegistryResult Remove(string appId)
    {
        if (!ValidId(appId)) return new(ApplicationRegistryStatus.InvalidRequest);
        return Mutate(state => Find(state, appId) is null
            ? (state, new(ApplicationRegistryStatus.Unchanged))
            : (state with
            {
                Applications = state.Applications.Where(app => !SameId(app.AppId, appId)).ToArray(),
                Bindings = state.Bindings.Where(binding => !SameId(binding.AppId, appId)).ToArray()
            }, new(ApplicationRegistryStatus.Applied)));
    }

    private ApplicationRegistryResult Mutate(Func<StudioState, (StudioState State, ApplicationRegistryResult Result)> transition)
    {
        ApplicationRegistryResult? result = null;
        RepositoryResult stored = _store.Update(state =>
        {
            (StudioState next, ApplicationRegistryResult outcome) = transition(state);
            result = outcome;
            return next;
        });
        return stored.IsSuccess
            ? result ?? new(ApplicationRegistryStatus.StorageError, RepositoryError: RepositoryError.InvalidState)
            : new(ApplicationRegistryStatus.StorageError, RepositoryError: stored.Error);
    }

    private ResolvedLaunchTarget? Resolve(ApplicationTargetDefinition definition)
    {
        try
        {
            LaunchTarget? target = definition.ToLaunchTarget();
            LaunchTargetResolution? result = target is null ? null : _validate(target);
            return result is { Succeeded: true } ? result.Target : null;
        }
        catch (Exception) { return null; }
    }

    private ApplicationProfile? FindDuplicate(StudioState state, ResolvedLaunchTarget target, string? exceptId = null) =>
        state.Applications.FirstOrDefault(app => !SameId(app.AppId, exceptId) && app.Target is not null &&
            Resolve(app.Target) is ResolvedLaunchTarget existing && SameResolved(existing, target));

    private static bool SameResolved(ResolvedLaunchTarget left, ResolvedLaunchTarget right) => (left, right) switch
    {
        (ResolvedExecutableLaunch a, ResolvedExecutableLaunch b) => SamePath(a.ExecutableIdentity.Path, b.ExecutableIdentity.Path) &&
            SamePath(a.WorkingDirectory, b.WorkingDirectory) && a.Arguments.SequenceEqual(b.Arguments, StringComparer.Ordinal),
        (ResolvedPackagedLaunch a, ResolvedPackagedLaunch b) => a.PackagedIdentity.Aumid == b.PackagedIdentity.Aumid,
        _ => false
    };

    private static bool SameStored(ApplicationTargetDefinition? a, ApplicationTargetDefinition b) =>
        a is not null && a.Kind == b.Kind &&
        (a.Kind == ApplicationTargetKind.Packaged ? a.Identifier == b.Identifier : SamePath(a.Identifier, b.Identifier)) &&
        a.Arguments.SequenceEqual(b.Arguments, StringComparer.Ordinal) && SamePath(a.WorkingDirectory, b.WorkingDirectory);

    private static StudioState Replace(StudioState state, ApplicationProfile profile) =>
        state with { Applications = state.Applications.Select(app => SameId(app.AppId, profile.AppId) ? profile : app).ToArray() };
    private static ApplicationProfile? Find(StudioState state, string id) => state.Applications.FirstOrDefault(app => SameId(app.AppId, id));
    private static bool SameId(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128;
    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 120 && !name.Any(char.IsControl);
}
