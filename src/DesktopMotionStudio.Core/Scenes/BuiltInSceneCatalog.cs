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

public sealed record SceneCatalogScanResult(IReadOnlyList<SceneCatalogItem> Items, bool RootAvailable);

public sealed class BuiltInSceneCatalog
{
    private const string ManifestFileName = "scene.json";

    public SceneCatalogScanResult Scan(string rootPath)
    {
        string root;
        try
        {
            root = Path.GetFullPath(rootPath);
            if (!Directory.Exists(root) || IsReparsePoint(root))
                return new SceneCatalogScanResult([], false);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new SceneCatalogScanResult([], false);
        }

        List<SceneCatalogItem> items;
        try
        {
            items = Directory.EnumerateDirectories(root)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(Path.GetFileName, StringComparer.Ordinal)
                .Select(ScanDirectory)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new SceneCatalogScanResult([], false);
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

        return new SceneCatalogScanResult(items, true);
    }

    private static SceneCatalogItem ScanDirectory(string directory)
    {
        string name = Path.GetFileName(directory);
        try
        {
            if (IsReparsePoint(directory))
                return new SceneCatalogItem(name, directory, null, SceneCatalogDiagnostic.ReparsePoint);

            string manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
                return new SceneCatalogItem(name, directory, null, SceneCatalogDiagnostic.MissingManifest);
            if (IsReparsePoint(manifestPath))
                return new SceneCatalogItem(name, directory, null, SceneCatalogDiagnostic.ReparsePoint);
            if (new FileInfo(manifestPath).Length > SceneManifestReader.MaxBytes)
                return new SceneCatalogItem(name, directory, null, SceneCatalogDiagnostic.InvalidManifest);

            SceneManifestReadResult parsed = SceneManifestReader.Read(File.ReadAllBytes(manifestPath));
            if (!parsed.IsSuccess || parsed.Manifest is null)
                return new SceneCatalogItem(name, directory, null, SceneCatalogDiagnostic.InvalidManifest);

            foreach (string asset in parsed.Manifest.Assets)
            {
                SceneCatalogDiagnostic assetDiagnostic = ValidateAsset(directory, asset);
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

    private static SceneCatalogDiagnostic ValidateAsset(string sceneDirectory, string assetPath)
    {
        string fullSceneDirectory = Path.GetFullPath(sceneDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullAssetPath = Path.GetFullPath(Path.Combine(sceneDirectory,
            assetPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullAssetPath.StartsWith(fullSceneDirectory, StringComparison.OrdinalIgnoreCase))
            return SceneCatalogDiagnostic.UnsafeAssetPath;

        string current = sceneDirectory;
        string[] segments = assetPath.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            if (!File.Exists(current) && !Directory.Exists(current)) return SceneCatalogDiagnostic.MissingAsset;
            if (IsReparsePoint(current)) return SceneCatalogDiagnostic.ReparsePoint;
            if (i == segments.Length - 1 && !File.Exists(current)) return SceneCatalogDiagnostic.MissingAsset;
        }
        return SceneCatalogDiagnostic.None;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
