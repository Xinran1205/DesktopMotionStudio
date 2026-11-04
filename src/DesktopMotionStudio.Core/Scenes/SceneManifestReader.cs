using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopMotionStudio.Core.Scenes;

public static class SceneManifestReader
{
    public const int MaxBytes = 1_048_576;
    public const int SupportedSchemaVersion = 1;

    public static bool IsValidSceneId(string? sceneId) =>
        sceneId is not null && sceneId.Length <= 64 && SceneIdPattern.IsMatch(sceneId);

    public static bool IsValidSceneVersion(string? sceneVersion) =>
        sceneVersion is not null && sceneVersion.Length <= 128 && IsValidSemVer(sceneVersion);

    private static readonly Regex SceneIdPattern = new(
        "^[a-z0-9][a-z0-9-]{2,63}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex VersionPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static SceneManifestReadResult Read(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > MaxBytes)
            return Fail(SceneManifestError.TooLarge);

        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root))
                return Fail(SceneManifestError.MalformedJson);

            if (!root.TryGetProperty("schemaVersion", out JsonElement schemaElement))
                return Fail(SceneManifestError.MissingField);
            if (schemaElement.ValueKind != JsonValueKind.Number || !schemaElement.TryGetInt32(out int schemaVersion))
                return Fail(SceneManifestError.InvalidSchema);
            if (schemaVersion > SupportedSchemaVersion)
                return Fail(SceneManifestError.UnsupportedSchema);
            if (schemaVersion != SupportedSchemaVersion)
                return Fail(SceneManifestError.InvalidSchema);

            SceneManifestError fieldError = GetRequiredString(root, "sceneId", out string? sceneId);
            if (fieldError != SceneManifestError.None) return Fail(fieldError);
            fieldError = GetRequiredString(root, "sceneVersion", out string? sceneVersion);
            if (fieldError != SceneManifestError.None) return Fail(fieldError);
            fieldError = GetRequiredString(root, "name", out string? name);
            if (fieldError != SceneManifestError.None) return Fail(fieldError);
            fieldError = GetRequiredString(root, "displayMode", out string? modeText);
            if (fieldError != SceneManifestError.None) return Fail(fieldError);

            if (!IsValidSceneVersion(sceneVersion) || !IsValidSceneId(sceneId) ||
                string.IsNullOrWhiteSpace(name) || name.Length > 120)
                return Fail(SceneManifestError.InvalidField);

            SceneDisplayMode mode = modeText switch
            {
                "full" => SceneDisplayMode.Full,
                "centered" => SceneDisplayMode.Centered,
                _ => (SceneDisplayMode)(-1)
            };
            if (mode is not (SceneDisplayMode.Full or SceneDisplayMode.Centered))
                return Fail(SceneManifestError.InvalidField);

            if (!root.TryGetProperty("assets", out JsonElement assetsElement))
                return Fail(SceneManifestError.MissingField);
            if (assetsElement.ValueKind != JsonValueKind.Array)
                return Fail(SceneManifestError.InvalidField);

            var assets = ImmutableArray.CreateBuilder<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement assetElement in assetsElement.EnumerateArray())
            {
                if (assetElement.ValueKind != JsonValueKind.String)
                    return Fail(SceneManifestError.InvalidField);
                string? asset = assetElement.GetString();
                if (!IsSafeRelativeAssetPath(asset))
                    return Fail(SceneManifestError.UnsafeAssetPath);
                if (!seen.Add(asset!))
                    return Fail(SceneManifestError.DuplicateAsset);
                assets.Add(asset!);
            }

            return new SceneManifestReadResult(
                new SceneManifest(schemaVersion, sceneId!, sceneVersion!, name!, mode, assets.ToImmutable()),
                SceneManifestError.None);
        }
        catch (JsonException)
        {
            return Fail(SceneManifestError.MalformedJson);
        }
    }

    private static SceneManifestError GetRequiredString(JsonElement root, string property, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(property, out JsonElement element))
            return SceneManifestError.MissingField;
        if (element.ValueKind != JsonValueKind.String)
            return SceneManifestError.InvalidField;
        value = element.GetString();
        return value is null ? SceneManifestError.InvalidField : SceneManifestError.None;
    }

    private static bool IsValidSemVer(string version)
    {
        if (!VersionPattern.IsMatch(version)) return false;
        int buildStart = version.IndexOf('+');
        int prereleaseStart = version.IndexOf('-');
        if (prereleaseStart < 0 || (buildStart >= 0 && prereleaseStart > buildStart)) return true;
        string prerelease = version[(prereleaseStart + 1)..(buildStart < 0 ? version.Length : buildStart)];
        foreach (string identifier in prerelease.Split('.'))
        {
            bool numeric = identifier.All(char.IsAsciiDigit);
            if (numeric && identifier.Length > 1 && identifier[0] == '0') return false;
        }
        return true;
    }

    private static bool HasDuplicateProperties(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
            if (!names.Add(property.Name)) return true;
        return false;
    }

    private static bool IsSafeRelativeAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') ||
            path.Contains(':') || path.Contains('\0'))
            return false;
        foreach (string segment in path.Split('/'))
            if (segment is "" or "." or "..") return false;
        return true;
    }

    private static SceneManifestReadResult Fail(SceneManifestError error) => new(null, error);
}
