namespace DesktopMotionStudio.Core.Scenes;

public enum SceneLibraryError { None, Unavailable, TooManyScenes }
public sealed record SceneLibrarySnapshot(IReadOnlyList<SceneLibraryItem> Items, SceneLibraryError Error);
public sealed record SceneLibraryItem(
    SceneCatalogItem Catalog,
    ScenePresentation? Presentation, SceneDataError PresentationError,
    ScenePlaybackDefinition? Playback, SceneDataError PlaybackError,
    SceneAssetError CoverError)
{
    public string DisplayName => Catalog.Manifest?.Name ?? Catalog.DirectoryName;
    public bool IsPlayable => Catalog.IsAvailable && Playback is { IsValid: true } definition && PlaybackError == SceneDataError.None &&
        definition.SceneId == Catalog.Manifest!.SceneId && definition.SceneVersion == Catalog.Manifest.SceneVersion;
}

public sealed class SceneLibrary
{
    public const int MaximumCoverBytes = 4 * 1024 * 1024;
    private readonly Func<string, string, int, SceneAssetReadResult> _readFile;
    public SceneLibrary(Func<string, string, int, SceneAssetReadResult>? readFile = null) => _readFile = readFile ?? SceneAssetResolver.ReadFile;

    public SceneLibrarySnapshot Scan(string root)
    {
        SceneCatalogScanResult catalog = new BuiltInSceneCatalog().Scan(root);
        if (!catalog.RootAvailable)
            return new([], catalog.Error == SceneCatalogRootError.TooManyScenes ? SceneLibraryError.TooManyScenes : SceneLibraryError.Unavailable);
        SceneLibraryItem[] items = catalog.Items.Select(ReadItem).ToArray();
        return new(Array.AsReadOnly(items), SceneLibraryError.None);
    }

    private SceneLibraryItem ReadItem(SceneCatalogItem item)
    {
        if (!item.IsAvailable || item.Manifest is not SceneManifest manifest)
            return new(item, null, SceneDataError.CatalogUnavailable, null, SceneDataError.CatalogUnavailable, SceneAssetError.Missing);
        SceneAssetReadResult presentationBytes = ReadFile(item.DirectoryPath, "scene.presentation.json", ScenePresentationReader.MaximumBytes);
        SceneDataReadResult<ScenePresentation> presentation = presentationBytes.Error == SceneAssetError.None
            ? ScenePresentationReader.Read(presentationBytes.Bytes!, manifest) : new(null, Map(presentationBytes.Error));
        SceneAssetReadResult playbackBytes = ReadFile(item.DirectoryPath, "scene.playback.json", ScenePlaybackReader.MaximumBytes);
        SceneDataReadResult<ScenePlaybackDefinition> playback = playbackBytes.Error == SceneAssetError.None
            ? ScenePlaybackReader.Read(playbackBytes.Bytes!, manifest) : new(null, Map(playbackBytes.Error));
        SceneAssetError coverError = presentation.Value?.Cover is string cover
            ? SceneAssetResolver.ProbeFile(item.DirectoryPath, cover, MaximumCoverBytes) : SceneAssetError.Missing;
        return new(item, presentation.Value, presentation.Error, playback.Value, playback.Error, coverError);
    }

    private SceneAssetReadResult ReadFile(string directory, string relative, int limit)
    {
        try { return _readFile(directory, relative, limit); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(null, SceneAssetError.IoError); }
    }

    private static SceneDataError Map(SceneAssetError error) => error switch
    {
        SceneAssetError.Missing or SceneAssetError.NotAFile => SceneDataError.Missing,
        SceneAssetError.TooLarge => SceneDataError.TooLarge,
        SceneAssetError.InvalidPath or SceneAssetError.ReparsePoint => SceneDataError.UnsafeAssetPath,
        _ => SceneDataError.IoError
    };
}
