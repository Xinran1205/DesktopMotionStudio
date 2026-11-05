using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Core.MotionHost;

public static class MotionHostFrameCodec
{
    public const int HeaderSize = sizeof(uint);
    public const int MaximumFrameBytes = 8 * 1024;
    private static readonly HashSet<string> FrameFields = new(StringComparer.Ordinal) { "protocolVersion", "requestId", "kind", "start", "error", "nativeErrorCode" };
    private static readonly HashSet<string> StartFields = new(StringComparer.Ordinal) { "sceneId", "sceneVersion", "displayMode", "maximumVisibleMilliseconds", "mode", "playback" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        Converters =
        {
            new JsonStringEnumConverter<MotionHostMessageKind>(allowIntegerValues: false),
            new JsonStringEnumConverter<MotionHostError>(allowIntegerValues: false),
            new JsonStringEnumConverter<MotionHostMode>(allowIntegerValues: false),
            new JsonStringEnumConverter<SceneDisplayMode>(allowIntegerValues: false)
        }
    };

    public static byte[] Encode(MotionHostFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (MotionHostFrameValidator.ValidateEnvelope(frame, frame.RequestId) != MotionHostError.None)
            throw new InvalidDataException("MotionHost frame is invalid.");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);
        if (payload.Length is 0 or > MaximumFrameBytes)
            throw new InvalidDataException("MotionHost frame size is outside the supported range.");

        byte[] encoded = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(encoded, checked((uint)payload.Length));
        payload.CopyTo(encoded.AsSpan(HeaderSize));
        return encoded;
    }

    public static MotionHostFrame Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < HeaderSize) throw new InvalidDataException("MotionHost frame header is incomplete.");
        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(encoded);
        if (payloadLength is 0 or > MaximumFrameBytes)
            throw new InvalidDataException("MotionHost frame size is outside the supported range.");
        if (encoded.Length != HeaderSize + payloadLength)
            throw new InvalidDataException("MotionHost frame length does not match its payload.");

        ReadOnlySpan<byte> payload = encoded[HeaderSize..];
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || HasDuplicateProperties(document.RootElement))
                throw new InvalidDataException("MotionHost frame JSON must be a unique-property object.");
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("protocolVersion", out JsonElement version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int protocol))
                throw new InvalidDataException("MotionHost protocol version is invalid.");
            if (protocol != MotionHostFrame.CurrentProtocolVersion) throw new MotionHostProtocolException(MotionHostError.UnsupportedProtocol);
            if (!HasClosedShape(root)) throw new InvalidDataException("MotionHost frame has an unknown member.");
            MotionHostFrame? frame = JsonSerializer.Deserialize<MotionHostFrame>(payload, JsonOptions);
            if (frame is null || MotionHostFrameValidator.ValidateEnvelope(frame, frame.RequestId) != MotionHostError.None)
                throw new InvalidDataException("MotionHost frame is invalid.");
            return frame;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("MotionHost frame JSON is malformed.", ex);
        }
    }

    public static async Task WriteAsync(Stream stream, MotionHostFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] encoded = Encode(frame);
        await stream.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<MotionHostFrame?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] header = new byte[HeaderSize];
        int firstByte = await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (firstByte == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken).ConfigureAwait(false);

        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (payloadLength is 0 or > MaximumFrameBytes)
            throw new InvalidDataException("MotionHost frame size is outside the supported range.");

        byte[] encoded = new byte[HeaderSize + checked((int)payloadLength)];
        header.CopyTo(encoded, 0);
        await stream.ReadExactlyAsync(encoded.AsMemory(HeaderSize), cancellationToken).ConfigureAwait(false);
        return Decode(encoded);
    }

    private static bool HasClosedShape(JsonElement root)
    {
        if (!SceneDataJson.HasOnlyMembers(root, FrameFields)) return false;
        if (!root.TryGetProperty("start", out JsonElement start) || start.ValueKind == JsonValueKind.Null) return true;
        if (!SceneDataJson.HasOnlyMembers(start, StartFields)) return false;
        if (!start.TryGetProperty("playback", out JsonElement playback) || playback.ValueKind == JsonValueKind.Null) return true;
        if (playback.ValueKind != JsonValueKind.Object) return false;
        if (playback.TryGetProperty("schemaVersion", out JsonElement schema) && schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out int playbackSchema) && playbackSchema > 1)
            throw new MotionHostProtocolException(MotionHostError.UnsupportedPlayback);
        if (playback.TryGetProperty("runtimeId", out JsonElement runtime) && runtime.ValueKind == JsonValueKind.String && runtime.GetString() != "vector-orbit" ||
            playback.TryGetProperty("runtimeVersion", out JsonElement runtimeVersion) && runtimeVersion.ValueKind == JsonValueKind.Number && runtimeVersion.TryGetInt32(out int runtimeNumber) && runtimeNumber != 1)
            throw new MotionHostProtocolException(MotionHostError.UnsupportedPlayback);
        if (!SceneDataJson.HasOnlyMembers(playback, ScenePlaybackReader.Fields)) return false;
        return !playback.TryGetProperty("keyframes", out JsonElement frames) ||
            frames.ValueKind == JsonValueKind.Array && frames.EnumerateArray().All(frame => SceneDataJson.HasOnlyMembers(frame, ScenePlaybackReader.KeyframeFields));
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateProperties(property.Value)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
                if (HasDuplicateProperties(child)) return true;
        }
        return false;
    }
}
