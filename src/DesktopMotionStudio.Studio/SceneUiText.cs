using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Studio;

internal static class SceneUiText
{
    private static string? BuiltInKey(SceneLibraryItem item) => item.Catalog.Manifest?.SceneId switch
    {
        "zero-boundary" when item.DisplayName == ProductText.Get("ZeroBoundaryName", ProductText.DefaultLanguage) => "ZeroBoundary",
        "studio-pulse" when item.DisplayName == ProductText.Get("StudioPulseName", ProductText.DefaultLanguage) => "StudioPulse",
        _ => null
    };

    internal static string Name(SceneLibraryItem item) => BuiltInKey(item) is string key
        ? ProductText.Get(key + "Name") : item.DisplayName;

    internal static ScenePresentation? Presentation(SceneLibraryItem item)
    {
        ScenePresentation? presentation = item.Presentation;
        if (presentation is null || BuiltInKey(item) is not string key || ProductText.Language == ProductText.DefaultLanguage)
            return presentation;

        string Translate(string field, string value)
        {
            string resource = key + field;
            return value == ProductText.Get(resource, ProductText.DefaultLanguage) ? ProductText.Get(resource) : value;
        }

        return presentation with
        {
            Description = Translate("Description", presentation.Description),
            Style = Translate("Style", presentation.Style),
            Theme = Translate("Theme", presentation.Theme)
        };
    }

    internal static string Availability(SceneLibraryItem item) => item.IsPlayable ? ProductText.Get("ReadyToPreviewAndBindToMultipleApps") : item.PlaybackError switch
    {
        SceneDataError.UnsupportedSchema or SceneDataError.UnsupportedRuntime => ProductText.Get("ThisWorkRequiresASupportedPlaybackVersion"),
        _ => ProductText.Get("WorkFilesAreMissingDamagedOrUnreadable")
    };

    internal static string Binding(BindingMutationResult result) => result.Status switch
    {
        BindingMutationStatus.Applied => ProductText.Get("MotionBindingSaved"),
        BindingMutationStatus.Unchanged => ProductText.Get("ThisAppAlreadyUsesTheSelectedWork"),
        BindingMutationStatus.ApplicationMissing => ProductText.Get("ThisAppWasRemovedChooseAnotherOne"),
        BindingMutationStatus.TargetUnavailable => ProductText.Get("TheLaunchEntryIsUnavailableConfirmItUnder"),
        BindingMutationStatus.SceneUnavailable => ProductText.Get("ThisWorkIsUnavailableTheExistingBindingHas2"),
        BindingMutationStatus.StorageError => UiText.Storage(result.RepositoryError),
        _ => ProductText.Get("TheBindingDidNotFinishRefreshAndChoose")
    };

    internal static string Preview(PreviewSnapshot state) => state.Error == PreviewError.CleanupFailed
        ? ProductText.Get("CouldNotConfirmPreviewCleanupCloseStudioAnd")
        : state.Phase switch
        {
            PreviewPhase.Starting => ProductText.Get("PreparingPreview"),
            PreviewPhase.Playing => ProductText.Get("PreviewingSimulatedHandoffNoAppWillOpen"),
            PreviewPhase.Stopping => ProductText.Get("StoppingPreview"),
            PreviewPhase.Completed => ProductText.Get("PreviewComplete"),
            PreviewPhase.Skipped => ProductText.Get("PreviewSkipped"),
            PreviewPhase.Stopped => ProductText.Get("PreviewStopped"),
            PreviewPhase.Failed => ProductText.Get("PreviewDidNotFinishRefreshTheWorkAnd"),
            _ => ProductText.Get("PreviewPlaysOnlyTheMotion")
        };
}
