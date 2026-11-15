using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Core.Bindings;

public enum BindingMutationStatus
{
    Applied, Unchanged, InvalidRequest, ApplicationMissing, BindingMissing,
    TargetUnavailable, SceneUnavailable, StorageError
}

public enum BindingResolutionStatus
{
    Ready, Unbound, Disabled, ApplicationMissing, TargetUnavailable,
    SceneUnavailable, InvalidRequest, StorageError
}

public sealed record BindingMutationResult(
    BindingMutationStatus Status,
    ApplicationBinding? Binding = null,
    RepositoryError RepositoryError = RepositoryError.None)
{
    public bool Succeeded => Status is BindingMutationStatus.Applied or BindingMutationStatus.Unchanged;
}

public sealed record BindingLaunchContext(ApplicationProfile Application, LaunchTarget Target, SceneManifest Scene);

public sealed record BindingResolution(
    BindingResolutionStatus Status,
    ApplicationProfile? Application = null,
    ApplicationBinding? Binding = null,
    BindingLaunchContext? Context = null,
    RepositoryError RepositoryError = RepositoryError.None);
