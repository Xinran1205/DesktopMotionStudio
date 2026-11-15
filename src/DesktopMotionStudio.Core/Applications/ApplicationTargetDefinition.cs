using DesktopMotionStudio.Core.Launch;
using System.Text.Json.Serialization;

namespace DesktopMotionStudio.Core.Applications;

public enum ApplicationTargetKind { Executable, Shortcut, Packaged }

public sealed record ApplicationTargetDefinition(
    [property: JsonRequired] ApplicationTargetKind Kind,
    [property: JsonRequired] string Identifier,
    [property: JsonRequired] IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null)
{
    [JsonIgnore]
    public bool IsValid
    {
        get
        {
            if (!Enum.IsDefined(Kind) || string.IsNullOrWhiteSpace(Identifier) || Identifier.Length > 2048 ||
                Identifier.Any(char.IsControl) || Arguments is null || Arguments.Count > 256 ||
                Arguments.Any(arg => arg is null || arg.Contains('\0')) ||
                Arguments.Sum(arg => (long)arg.Length + 1) > 30_000) return false;
            if (Kind == ApplicationTargetKind.Packaged)
                return Arguments.Count == 0 && WorkingDirectory is null && LaunchTargetIdentityResolver.IsValidAumid(Identifier);
            if (!Path.IsPathFullyQualified(Identifier)) return false;
            if (Kind == ApplicationTargetKind.Shortcut)
                return Arguments.Count == 0 && WorkingDirectory is null && Path.GetExtension(Identifier).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
            return Path.GetExtension(Identifier).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                   (WorkingDirectory is null || (WorkingDirectory.Length <= 2048 && !WorkingDirectory.Any(char.IsControl) && Path.IsPathFullyQualified(WorkingDirectory)));
        }
    }

    public LaunchTarget? ToLaunchTarget() => !IsValid ? null : Kind switch
    {
        ApplicationTargetKind.Executable => new ExecutableTarget(Identifier, Array.AsReadOnly(Arguments.ToArray()), WorkingDirectory),
        ApplicationTargetKind.Shortcut => new ShortcutTarget(Identifier),
        ApplicationTargetKind.Packaged => new PackagedAppTarget(Identifier),
        _ => null
    };

    public ApplicationTargetDefinition Snapshot() => this with { Arguments = Array.AsReadOnly(Arguments.ToArray()) };
}
