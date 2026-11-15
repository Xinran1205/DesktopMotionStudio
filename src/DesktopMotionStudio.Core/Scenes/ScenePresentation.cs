using System.Text.Json.Serialization;

namespace DesktopMotionStudio.Core.Scenes;

public sealed record ScenePresentation(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string SceneId,
    [property: JsonRequired] string SceneVersion,
    [property: JsonRequired] string Description,
    [property: JsonRequired] string Style,
    [property: JsonRequired] string Theme,
    string? Cover = null);

public static class ScenePresentationReader
{
    public const int MaximumBytes = SceneDataJson.MaximumBytes;
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal) { "schemaVersion", "sceneId", "sceneVersion", "description", "style", "theme", "cover" };
    public static SceneDataReadResult<ScenePresentation> Read(ReadOnlySpan<byte> bytes, SceneManifest expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        SceneDataReadResult<ScenePresentation> read = SceneDataJson.Read<ScenePresentation>(bytes, Fields);
        if (read.Value is not ScenePresentation data) return read;
        if (!SceneManifestReader.IsValidSceneId(data.SceneId) || !SceneManifestReader.IsValidSceneVersion(data.SceneVersion) ||
            !SceneDataJson.IsText(data.Description, 1000) || !SceneDataJson.IsText(data.Style, 120) || !SceneDataJson.IsText(data.Theme, 120)) return new(null, SceneDataError.InvalidDefinition);
        if (data.SceneId != expected.SceneId || data.SceneVersion != expected.SceneVersion) return new(null, SceneDataError.IdentityMismatch);
        if (data.Cover is not null)
        {
            if (!SceneAssetResolver.IsSafeRelativePath(data.Cover)) return new(null, SceneDataError.UnsafeAssetPath);
            if (Path.GetExtension(data.Cover).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg")) return new(null, SceneDataError.InvalidDefinition);
        }
        return read;
    }
}
