using System.Text.Json.Serialization;
using DesktopMotionStudio.Core.IO;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Core.Video;

public enum ImportError
{
    None, UnsafePath, Empty, TooLarge, InvalidMedia, DecoderUnavailable, TimedOut,
    InvalidOptions, Missing, Changed, Busy, NoSpace, StorageUnavailable
}

public sealed record ImportedVideoOptions(string Name, int DurationMilliseconds, VideoFit Fit, bool Sound)
{
    internal bool IsValid(long sourceDuration) =>
        Name is { Length: >= 1 and <= 80 } && Name == Name.Trim() && SceneDataJson.IsText(Name, 80) &&
        DurationMilliseconds >= 1000 && DurationMilliseconds <= Math.Min(10_000, sourceDuration) && Enum.IsDefined(Fit);
}

public sealed record ImportedVideoWork(string Id, int Revision, long SourceDurationMilliseconds, bool HasAudio,
    ImportedVideoOptions Options, string AssetPath)
{
    public const string Prefix = "user-mp4-";
    public static bool IsId(string? value) => value is { Length: 41 } &&
        value.StartsWith(Prefix, StringComparison.Ordinal) &&
        value.AsSpan(Prefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;

    public VideoClip Clip => new(Id, AssetPath, Options.DurationMilliseconds, Options.Fit, HasAudio && Options.Sound);

    public SceneLibraryItem ToLibraryItem()
    {
        var manifest = new SceneManifest(1, Id, "1.0.0", Options.Name, SceneDisplayMode.Centered, ["clip.mp4"]);
        var catalog = new SceneCatalogItem(Id, Path.GetDirectoryName(AssetPath)!, manifest, SceneCatalogDiagnostic.None);
        return new(catalog, null, SceneDataError.Missing, null, SceneDataError.None, SceneAssetError.Missing) { Video = Clip };
    }
}

public sealed record ImportResult<T>(T? Value, ImportError Error) where T : class;

internal sealed record ImportFileIdentity(
    [property: JsonRequired] uint Volume,
    [property: JsonRequired] ulong Index,
    [property: JsonRequired] uint CreatedHigh,
    [property: JsonRequired] uint CreatedLow)
{
    internal static ImportFileIdentity Of(WindowsFileScope.FileInfo info) =>
        new(info.VolumeSerial, info.FileIndex, unchecked((uint)info.Creation.dwHighDateTime),
            unchecked((uint)info.Creation.dwLowDateTime));
}

internal sealed record ImportedVideoMetadata(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string Id,
    [property: JsonRequired] int Revision,
    [property: JsonRequired] string Name,
    [property: JsonRequired] long SourceDurationMilliseconds,
    [property: JsonRequired] int DurationMilliseconds,
    [property: JsonRequired] string Fit,
    [property: JsonRequired] bool Sound,
    [property: JsonRequired] bool HasAudio,
    [property: JsonRequired] ImportFileIdentity Media)
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "schemaVersion", "id", "revision", "name", "sourceDurationMilliseconds", "durationMilliseconds",
        "fit", "sound", "hasAudio", "media"
    };

    internal ImportedVideoOptions Options =>
        new(Name, DurationMilliseconds, Fit == "complete" ? VideoFit.Complete : VideoFit.Fill, Sound);

    internal static ImportedVideoMetadata? Read(byte[] bytes, string id)
    {
        var data = SceneDataJson.Read<ImportedVideoMetadata>(bytes, Fields).Value;
        return data is { Revision: > 0, SourceDurationMilliseconds: >= 1000, Fit: "complete" or "fill", Media.Index: > 0 } &&
            data.Id == id && data.Options.IsValid(data.SourceDurationMilliseconds) && (!data.Sound || data.HasAudio) ? data : null;
    }

    internal ImportedVideoMetadata WithOptions(ImportedVideoOptions options, int revision) => this with
    {
        Name = options.Name,
        DurationMilliseconds = options.DurationMilliseconds,
        Fit = options.Fit == VideoFit.Complete ? "complete" : "fill",
        Sound = HasAudio && options.Sound,
        Revision = revision
    };
}
