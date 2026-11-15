namespace DesktopMotionStudio.Core.Scenes;

public enum SceneCatalogDiagnostic
{
    None,
    MissingManifest,
    InvalidManifest,
    MissingAsset,
    UnsafeAssetPath,
    ReparsePoint,
    DuplicateSceneId,
    IoError
}

public sealed record SceneCatalogItem(string DirectoryName, string DirectoryPath,
    SceneManifest? Manifest, SceneCatalogDiagnostic Diagnostic)
{
    public bool IsAvailable => Manifest is not null && Diagnostic == SceneCatalogDiagnostic.None;
}

public enum SceneCatalogRootError { None, Unavailable, TooManyScenes }
public sealed record SceneCatalogScanResult(IReadOnlyList<SceneCatalogItem> Items, bool RootAvailable, SceneCatalogRootError Error = SceneCatalogRootError.None);

public sealed class BuiltInSceneCatalog
{
    public const int MaximumDirectories = 256;

    public SceneCatalogScanResult Scan(string rootPath)
    {
        string root;
        try
        {
            if (SceneAssetResolver.ValidateDirectory(rootPath) != SceneAssetError.None)
                return new SceneCatalogScanResult([], false, SceneCatalogRootError.Unavailable);
            root = Path.GetFullPath(rootPath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new SceneCatalogScanResult([], false, SceneCatalogRootError.Unavailable);
        }

        List<SceneCatalogItem> items;
        try
        {
            var directories = new List<string>();
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                directories.Add(directory);
                if (directories.Count > MaximumDirectories)
                    return new SceneCatalogScanResult([], false, SceneCatalogRootError.TooManyScenes);
            }
            items = directories
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(Path.GetFileName, StringComparer.Ordinal)
                .Select(ScanDirectory)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new SceneCatalogScanResult([], false, SceneCatalogRootError.Unavailable);
        }

        var duplicateIds = items
            .Where(item => item.Manifest is not null)
            .GroupBy(item => item.Manifest!.SceneId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < items.Count; i++)
            if (items[i].Manifest is { } manifest && duplicateIds.Contains(manifest.SceneId))
                items[i] = items[i] with { Diagnostic = SceneCatalogDiagnostic.DuplicateSceneId };

        return new SceneCatalogScanResult(items.AsReadOnly(), true);
    }

    private static SceneCatalogItem ScanDirectory(string directory)
    {
        string name = Path.GetFileName(directory);
        try
        {
            SceneAssetReadResult source = SceneAssetResolver.ReadFile(directory, "scene.json", SceneManifestReader.MaxBytes);
            if (source.Error != SceneAssetError.None)
                return new SceneCatalogItem(name, directory, null, MapAssetError(source.Error, manifest: true));
            SceneManifestReadResult parsed = SceneManifestReader.Read(source.Bytes!);
            if (!parsed.IsSuccess || parsed.Manifest is null)
                return new SceneCatalogItem(name, directory, null, SceneCatalogDiagnostic.InvalidManifest);

            foreach (string asset in parsed.Manifest.Assets)
            {
                SceneCatalogDiagnostic assetDiagnostic = MapAssetError(SceneAssetResolver.Resolve(directory, asset).Error);
                if (assetDiagnostic != SceneCatalogDiagnostic.None)
                    return new SceneCatalogItem(name, directory, parsed.Manifest, assetDiagnostic);
            }

            return new SceneCatalogItem(name, directory, parsed.Manifest, SceneCatalogDiagnostic.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new SceneCatalogItem(name, directory, null, SceneCatalogDiagnostic.IoError);
        }
    }

    private static SceneCatalogDiagnostic MapAssetError(SceneAssetError error, bool manifest = false) => error switch
    {
        SceneAssetError.None => SceneCatalogDiagnostic.None,
        SceneAssetError.ReparsePoint => SceneCatalogDiagnostic.ReparsePoint,
        SceneAssetError.InvalidPath => SceneCatalogDiagnostic.UnsafeAssetPath,
        SceneAssetError.Missing or SceneAssetError.NotAFile => manifest ? SceneCatalogDiagnostic.MissingManifest : SceneCatalogDiagnostic.MissingAsset,
        SceneAssetError.TooLarge => SceneCatalogDiagnostic.InvalidManifest,
        _ => SceneCatalogDiagnostic.IoError
    };
}
