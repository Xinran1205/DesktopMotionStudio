using System.Collections.Immutable;

namespace DesktopMotionStudio.Core.Scenes;

public enum SceneDisplayMode { Full, Centered }

public sealed record SceneManifest(
    int SchemaVersion,
    string SceneId,
    string SceneVersion,
    string Name,
    SceneDisplayMode DisplayMode,
    ImmutableArray<string> Assets);

public enum SceneManifestError
{
    None,
    TooLarge,
    MalformedJson,
    InvalidSchema,
    UnsupportedSchema,
    MissingField,
    InvalidField,
    UnsafeAssetPath,
    DuplicateAsset
}

public sealed record SceneManifestReadResult(SceneManifest? Manifest, SceneManifestError Error)
{
    public bool IsSuccess => Manifest is not null && Error == SceneManifestError.None;
}
