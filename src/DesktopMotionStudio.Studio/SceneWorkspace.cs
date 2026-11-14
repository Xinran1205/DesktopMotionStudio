using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.Video;

namespace DesktopMotionStudio.Studio;

internal sealed class SceneWorkspace(string root, ImportedVideoStore? imports = null)
{
    internal string Root { get; } = root;
    internal ImportedVideoStore? Imports { get; } = imports;
    internal SceneLibrarySnapshot Scan()
    {
        var builtIn = new SceneLibrary().Scan(Root);
        if (Imports is null) return builtIn;
        return builtIn with
        {
            Items = builtIn.Items.Where(item => !ImportedVideoWork.IsId(item.Catalog.Manifest?.SceneId))
                .Concat(Imports.ListLibraryItems()).ToArray()
        };
    }
    internal SceneLibraryItem? FindPlayable(string sceneId)
    {
        if (Imports is not null && ImportedVideoWork.IsId(sceneId)) return Imports.Find(sceneId)?.ToLibraryItem();
        return new SceneLibrary().Scan(Root).Items.SingleOrDefault(item =>
            item.IsPlayable && string.Equals(item.Catalog.Manifest?.SceneId, sceneId, StringComparison.OrdinalIgnoreCase));
    }

    internal BindingService CreateBindings(IStudioStateStore store) => new(store,
        () => new BuiltInSceneCatalog().Scan(Root), ApplicationRegistry.GetTarget,
        sceneResolver: sceneId => FindPlayable(sceneId)?.Catalog.Manifest);
}
