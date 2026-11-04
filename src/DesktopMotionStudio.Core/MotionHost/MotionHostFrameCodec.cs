using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Core.MotionHost;

public static class MotionHostFrameCodec
{
    public const int HeaderSize = sizeof(uint);
    public const int MaximumFrameBytes = 8 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<MotionHostMessageKind>(allowIntegerValues: false),
            new JsonStringEnumConverter<MotionHostError>(allowIntegerValues: false),
            new JsonStringEnumConverter<SceneDisplayMode>(allowIntegerValues: false)
        }
    };

    public static byte[] Encode(MotionHostFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
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
            MotionHostFrame? frame = JsonSerializer.Deserialize<MotionHostFrame>(payload, JsonOptions);
            if (frame is null || !Enum.IsDefined(frame.Kind))
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
