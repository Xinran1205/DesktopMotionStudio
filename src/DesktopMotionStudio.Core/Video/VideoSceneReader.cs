using System.Text.Json.Serialization;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Core.Video;

public static class VideoSceneReader
{
    private const string Metadata = "scene.video.json";
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
        { "schemaVersion", "file", "durationMilliseconds", "fit", "sound" };

    public static bool UsesVideo(SceneManifest? scene) =>
        scene is not null && scene.Assets.Contains(Metadata, StringComparer.Ordinal);

    public static SceneDataReadResult<VideoClip> Read(SceneCatalogItem item)
    {
        if (item.Manifest is not { } scene || !item.IsAvailable || !UsesVideo(scene))
            return new(null, SceneDataError.CatalogUnavailable);
        var bytes = SceneAssetResolver.ReadFile(item.DirectoryPath, Metadata, SceneDataJson.MaximumBytes);
        if (bytes.Error != SceneAssetError.None) return new(null, Map(bytes.Error));
        var data = SceneDataJson.Read<MetadataV1>(bytes.Bytes!, Fields);
        if (data.Value is not { } descriptor) return new(null, data.Error);
        if (descriptor.File is not { Length: > 0 and <= 128 } file ||
            file != Path.GetFileName(file) || !scene.Assets.Contains(file, StringComparer.Ordinal) ||
            descriptor.Fit is not ("complete" or "fill"))
            return new(null, SceneDataError.InvalidDefinition);

        var asset = SceneAssetResolver.Resolve(item.DirectoryPath, file);
        if (asset.FullPath is not { } path) return new(null, Map(asset.Error));
        try
        {
            long size = new FileInfo(path).Length;
            if (size == 0) return new(null, SceneDataError.InvalidDefinition);
            if (size > VideoClip.MaximumBytes) return new(null, SceneDataError.TooLarge);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return new(null, SceneDataError.Missing); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(null, SceneDataError.IoError); }

        var clip = new VideoClip(scene.SceneId, path, descriptor.DurationMilliseconds,
            descriptor.Fit == "complete" ? VideoFit.Complete : VideoFit.Fill, descriptor.Sound);
        return clip.IsValid ? new(clip, SceneDataError.None) : new(null, SceneDataError.InvalidDefinition);
    }

    private static SceneDataError Map(SceneAssetError error) => error switch
    {
        SceneAssetError.Missing or SceneAssetError.NotAFile => SceneDataError.Missing,
        SceneAssetError.TooLarge => SceneDataError.TooLarge,
        SceneAssetError.InvalidPath or SceneAssetError.ReparsePoint => SceneDataError.UnsafeAssetPath,
        _ => SceneDataError.IoError
    };

    private sealed record MetadataV1(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string File,
        [property: JsonRequired] int DurationMilliseconds,
        [property: JsonRequired] string Fit,
        bool Sound = false);
}
