using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Core.Video;

public enum VideoFit { Complete, Fill }

public sealed record VideoClip(
    [property: JsonRequired] string SceneId,
    [property: JsonRequired] string AssetPath,
    [property: JsonRequired] int DurationMilliseconds,
    [property: JsonRequired] VideoFit Fit,
    [property: JsonRequired] bool Sound)
{
    public const int MaximumBytes = 64 * 1024 * 1024;

    [JsonIgnore]
    public bool IsValid => SceneManifestReader.IsValidSceneId(SceneId) &&
        AssetPath is { Length: > 0 and <= 1024 } && Path.IsPathFullyQualified(AssetPath) &&
        !AssetPath.Any(char.IsControl) && string.Equals(Path.GetExtension(AssetPath), ".mp4", StringComparison.OrdinalIgnoreCase) &&
        DurationMilliseconds is >= 1000 and <= 10_000 && Enum.IsDefined(Fit);
}

public sealed record VideoPlaybackRequest(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] VideoClip Clip,
    [property: JsonRequired] SceneDisplayMode DisplayMode,
    [property: JsonRequired] bool Preview)
{
    public const int CurrentVersion = 1;
    public const string WorkerName = "DesktopMotionStudio.VideoHost.exe";
    public const int StartupMilliseconds = 2400;
    public const int FinishMilliseconds = 600;
    public const int CleanupMilliseconds = 750;

    [JsonIgnore]
    public bool IsValid => Version == CurrentVersion && RequestId != Guid.Empty &&
        Clip is { IsValid: true } && Enum.IsDefined(DisplayMode);
}

public enum VideoEventKind { Visible, Completed, Skipped, InvalidMedia, Failed }

public sealed record VideoPlaybackEvent(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] VideoEventKind Kind)
{
    public bool Matches(Guid request) => Version == VideoPlaybackRequest.CurrentVersion &&
        RequestId == request && Enum.IsDefined(Kind);
}

public static class VideoPlaybackWire
{
    private const int MaximumMessageBytes = 8192;
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 4
    };

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (payload.Length > MaximumMessageBytes) throw new InvalidDataException("Video message exceeds the size limit.");
        byte[] size = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(size, payload.Length);
        await stream.WriteAsync(size, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken) where T : class
    {
        byte[] size = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(size, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(size);
        if (length is <= 0 or > MaximumMessageBytes) throw new InvalidDataException("Invalid video message size.");
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        try { return JsonSerializer.Deserialize<T>(payload, Options) ?? throw new InvalidDataException("Empty video message."); }
        catch (JsonException error) { throw new InvalidDataException("Invalid video message.", error); }
    }
}
