using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Studio;

internal static class UiText
{
    internal static string Storage(RepositoryError error) => error switch
    {
        RepositoryError.UnsupportedSchema => ProductText.Get("TheConfigurationComesFromANewerVersionOpen"),
        RepositoryError.Busy => ProductText.Get("TheConfigurationIsInUseTryAgainShortly"),
        RepositoryError.InvalidJson or RepositoryError.InvalidState => ProductText.Get("CouldNotReadTheConfigurationKeepTheOriginal"),
        _ => ProductText.Get("CouldNotReadOrWriteTheConfigurationCheck")
    };

    internal static string Result(ApplicationRegistryResult result) => result.Status switch
    {
        ApplicationRegistryStatus.Applied => ProductText.Get("Saved"),
        ApplicationRegistryStatus.Unchanged => ProductText.Get("TheConfigurationIsAlreadyUpToDate"),
        ApplicationRegistryStatus.Duplicate => ProductText.Format("ThisEntryIsAlreadySavedAs", result.Profile?.DisplayName),
        ApplicationRegistryStatus.TargetUnavailable => ProductText.Get("TheLaunchEntryIsUnavailableChooseALocal"),
        ApplicationRegistryStatus.ApplicationMissing => ProductText.Get("ThisAppWasRemovedRefreshTheList"),
        ApplicationRegistryStatus.StorageError => Storage(result.RepositoryError),
        _ => ProductText.Get("CheckTheNameAndLaunchEntrySomeValues")
    };

    internal static string Kind(ApplicationTargetKind? kind) => kind switch
    {
        ApplicationTargetKind.Executable => ProductText.Get("LocalProgram"),
        ApplicationTargetKind.Shortcut => ProductText.Get("Shortcut"),
        ApplicationTargetKind.Packaged => ProductText.Get("PackagedApp"),
        _ => ProductText.Get("TargetToConfirm")
    };
}
