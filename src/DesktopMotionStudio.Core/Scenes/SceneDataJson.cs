using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopMotionStudio.Core.Scenes;

public enum SceneDataError { None, Missing, TooLarge, InvalidJson, InvalidSchema, UnsupportedSchema, InvalidDefinition, IdentityMismatch, UnsupportedRuntime, UnsafeAssetPath, IoError, CatalogUnavailable }
public sealed record SceneDataReadResult<T>(T? Value, SceneDataError Error) where T : class;

internal static class SceneDataJson
{
    internal const int MaximumBytes = 64 * 1024;
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    internal static SceneDataReadResult<T> Read<T>(ReadOnlySpan<byte> bytes, IReadOnlySet<string> fields,
        string? childArray = null, IReadOnlySet<string>? childFields = null) where T : class
    {
        if (bytes.Length > MaximumBytes) return new(null, SceneDataError.TooLarge);
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateMembers(root)) return new(null, SceneDataError.InvalidJson);
            if (!root.TryGetProperty("schemaVersion", out JsonElement schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version < 1)
                return new(null, SceneDataError.InvalidSchema);
            if (version > 1) return new(null, SceneDataError.UnsupportedSchema);
            if (!HasOnlyMembers(root, fields)) return new(null, SceneDataError.InvalidJson);
            if (childArray is not null && root.TryGetProperty(childArray, out JsonElement children))
            {
                if (children.ValueKind != JsonValueKind.Array || childFields is null || children.EnumerateArray().Any(child => !HasOnlyMembers(child, childFields)))
                    return new(null, SceneDataError.InvalidJson);
            }
            T? value = JsonSerializer.Deserialize<T>(bytes, Options);
            return value is null ? new(null, SceneDataError.InvalidJson) : new(value, SceneDataError.None);
        }
        catch (JsonException) { return new(null, SceneDataError.InvalidJson); }
    }

    internal static bool HasOnlyMembers(JsonElement element, IReadOnlySet<string> fields) =>
        element.ValueKind == JsonValueKind.Object && element.EnumerateObject().All(property => fields.Contains(property.Name));

    internal static bool IsGlyph(string? text) => IsText(text, 8) && text!.EnumerateRunes().Any(rune =>
        Rune.IsLetterOrDigit(rune) || Rune.IsNumber(rune) || Rune.IsPunctuation(rune) || Rune.IsSymbol(rune));

    internal static bool IsText(string? text, int maximumLength)
    {
        if (text is null || text.Length > maximumLength) return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsControl(text[i]) || CharUnicodeInfo.GetUnicodeCategory(text, i) == UnicodeCategory.Format) return false;
            if (char.IsSurrogate(text[i]))
            {
                if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                i++;
            }
        }
        return true;
    }

    private static bool HasDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateMembers(property.Value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray()) if (HasDuplicateMembers(item)) return true;
        return false;
    }
}
