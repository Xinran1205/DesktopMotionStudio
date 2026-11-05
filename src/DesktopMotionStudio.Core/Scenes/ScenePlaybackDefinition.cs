using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopMotionStudio.Core.Scenes;

public sealed record SceneKeyframe(
    [property: JsonRequired] int AtMilliseconds,
    [property: JsonRequired] double Scale,
    [property: JsonRequired] double Opacity,
    [property: JsonRequired] double RotationDegrees);

public sealed record ScenePlaybackDefinition(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string SceneId,
    [property: JsonRequired] string SceneVersion,
    [property: JsonRequired] string RuntimeId,
    [property: JsonRequired] int RuntimeVersion,
    [property: JsonRequired] int DurationMilliseconds,
    [property: JsonRequired] string Accent,
    [property: JsonRequired] string Glyph,
    [property: JsonRequired] ImmutableArray<SceneKeyframe> Keyframes)
{
    [JsonIgnore] public bool IsValid => ScenePlaybackReader.Validate(this) == SceneDataError.None;
}

public static class ScenePlaybackReader
{
    public const int MaximumBytes = SceneDataJson.MaximumBytes;
    public const int MaximumSnapshotBytes = 4 * 1024;
    internal static readonly HashSet<string> Fields = new(StringComparer.Ordinal) { "schemaVersion", "sceneId", "sceneVersion", "runtimeId", "runtimeVersion", "durationMilliseconds", "accent", "glyph", "keyframes" };
    internal static readonly HashSet<string> KeyframeFields = new(StringComparer.Ordinal) { "atMilliseconds", "scale", "opacity", "rotationDegrees" };
    public static SceneDataReadResult<ScenePlaybackDefinition> Read(ReadOnlySpan<byte> bytes, SceneManifest expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        SceneDataReadResult<ScenePlaybackDefinition> read = SceneDataJson.Read<ScenePlaybackDefinition>(bytes, Fields, "keyframes", KeyframeFields);
        if (read.Value is not ScenePlaybackDefinition data) return read;
        SceneDataError error = Validate(data);
        if (error != SceneDataError.None) return new(null, error);
        return data.SceneId == expected.SceneId && data.SceneVersion == expected.SceneVersion ? read : new(null, SceneDataError.IdentityMismatch);
    }

    public static SceneDataError Validate(ScenePlaybackDefinition? data)
    {
        if (data is null || data.SchemaVersion < 1) return SceneDataError.InvalidDefinition;
        if (data.SchemaVersion > 1) return SceneDataError.UnsupportedSchema;
        if (data.RuntimeId != "vector-orbit" || data.RuntimeVersion != 1) return SceneDataError.UnsupportedRuntime;
        if (!SceneManifestReader.IsValidSceneId(data.SceneId) || !SceneManifestReader.IsValidSceneVersion(data.SceneVersion) ||
            data.DurationMilliseconds is < 1000 or > 8000 || data.Accent is null || data.Accent.Length != 7 || data.Accent[0] != '#' ||
            data.Accent.Skip(1).Any(c => !Uri.IsHexDigit(c)) || !SceneDataJson.IsGlyph(data.Glyph) ||
            data.Keyframes.IsDefault || data.Keyframes.Length is < 2 or > 8) return SceneDataError.InvalidDefinition;
        int previous = -1;
        foreach (SceneKeyframe? frame in data.Keyframes)
        {
            if (frame is null || frame.AtMilliseconds <= previous || frame.AtMilliseconds > data.DurationMilliseconds ||
                !double.IsFinite(frame.Scale) || frame.Scale is < .1 or > 1.5 ||
                !double.IsFinite(frame.Opacity) || frame.Opacity is < 0 or > 1 ||
                !double.IsFinite(frame.RotationDegrees) || frame.RotationDegrees is < -360 or > 360) return SceneDataError.InvalidDefinition;
            previous = frame.AtMilliseconds;
        }
        if (data.Keyframes[0].AtMilliseconds != 0 || previous != data.DurationMilliseconds) return SceneDataError.InvalidDefinition;
        return JsonSerializer.SerializeToUtf8Bytes(data, SceneDataJson.Options).Length <= MaximumSnapshotBytes ? SceneDataError.None : SceneDataError.TooLarge;
    }
}
