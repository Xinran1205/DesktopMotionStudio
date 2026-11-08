using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.Video;

namespace DesktopMotionStudio.Studio;

internal sealed class SceneWorkspace(string root)
{
    internal string Root { get; } = root;
    internal SceneLibrarySnapshot Scan() => new SceneLibrary().Scan(Root);
    internal SceneLibraryItem? FindPlayable(string sceneId) => Scan().Items.SingleOrDefault(item =>
        item.IsPlayable && string.Equals(item.Catalog.Manifest?.SceneId, sceneId, StringComparison.OrdinalIgnoreCase));

    internal BindingService CreateBindings(IStudioStateStore store) => new(store,
        () => new BuiltInSceneCatalog().Scan(Root), ApplicationRegistry.GetTarget, sceneEligibility: CurrentPlayback);

    private static bool CurrentPlayback(SceneCatalogItem item)
    {
        if (item.Manifest is null) return false;
        if (VideoSceneReader.UsesVideo(item.Manifest)) return VideoSceneReader.Read(item).Error == SceneDataError.None;
        SceneAssetReadResult read = SceneAssetResolver.ReadFile(item.DirectoryPath, "scene.playback.json", ScenePlaybackReader.MaximumBytes);
        return read.Error == SceneAssetError.None && ScenePlaybackReader.Read(read.Bytes!, item.Manifest).Error == SceneDataError.None;
    }
}
