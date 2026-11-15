using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Core.State;

public sealed class MotionDisplayPreference(string profilePath)
{
    public const string Recommended = "recommended";
    private readonly ProfilePreferenceFile _file = new(profilePath, ".motion-display");

    public string Read()
    {
        string? token = _file.Read();
        return token is "compact" or "large" or "full" ? token : Recommended;
    }

    public bool TrySave(string token) => (token is Recommended or "compact" or "large" or "full") && _file.TrySave(token);

    public SceneDisplayMode Resolve(SceneDisplayMode recommended) => Read() switch
    {
        "compact" => SceneDisplayMode.CompactCentered,
        "large" => SceneDisplayMode.LargeCentered,
        "full" => SceneDisplayMode.PointerScreenFull,
        _ => recommended
    };
}
