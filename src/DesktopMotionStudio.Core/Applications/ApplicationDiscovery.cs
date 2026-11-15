using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopMotionStudio.Core.Applications;

public enum ApplicationDiscoveryStatus { Complete, Empty, Failed, TimedOut, Cancelled }
public sealed record ApplicationCandidate(
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] ApplicationTargetDefinition Target,
    [property: JsonRequired] string Source);
public sealed record ApplicationDiscoveryResult(
    [property: JsonRequired] ApplicationDiscoveryStatus Status,
    [property: JsonRequired] IReadOnlyList<ApplicationCandidate> Candidates,
    [property: JsonRequired] int ProtocolVersion = 1);

public static class ApplicationDiscoveryProtocol
{
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumCandidates = 2000;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        Converters =
        {
            new JsonStringEnumConverter<ApplicationTargetKind>(allowIntegerValues: false),
            new JsonStringEnumConverter<ApplicationDiscoveryStatus>(allowIntegerValues: false)
        }
    };

    public static byte[] Encode(ApplicationDiscoveryResult result)
    {
        Validate(result);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Discovery result is too large.");
        return bytes;
    }

    public static ApplicationDiscoveryResult Decode(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw new InvalidDataException("Discovery result length is invalid.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicateMembers(document.RootElement);
            ApplicationDiscoveryResult result = JsonSerializer.Deserialize<ApplicationDiscoveryResult>(bytes, Options)
                ?? throw new InvalidDataException("Discovery result is absent.");
            Validate(result);
            return result;
        }
        catch (JsonException ex) { throw new InvalidDataException("Discovery JSON is invalid.", ex); }
    }

    private static void Validate(ApplicationDiscoveryResult result)
    {
        if (result.ProtocolVersion != 1 || !Enum.IsDefined(result.Status) || result.Candidates is null || result.Candidates.Count > MaximumCandidates ||
            (result.Status == ApplicationDiscoveryStatus.Complete && result.Candidates.Count == 0) ||
            (result.Status != ApplicationDiscoveryStatus.Complete && result.Candidates.Count != 0))
            throw new InvalidDataException("Discovery result is invalid.");
        foreach (ApplicationCandidate? candidate in result.Candidates)
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.DisplayName) || candidate.DisplayName.Length > 120 ||
                candidate.DisplayName.Any(char.IsControl) || candidate.Target is null || !candidate.Target.IsValid ||
                candidate.Source is not ("StartMenu" or "Packaged") ||
                (candidate.Source == "StartMenu" && candidate.Target.Kind != ApplicationTargetKind.Shortcut) ||
                (candidate.Source == "Packaged" && candidate.Target.Kind != ApplicationTargetKind.Packaged))
                throw new InvalidDataException("Discovery candidate is invalid.");
    }

    private static void RejectDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate discovery member.");
                RejectDuplicateMembers(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray()) RejectDuplicateMembers(item);
    }
}
