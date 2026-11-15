using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Core.Localization;

public sealed class StudioLanguagePreference(string profilePath)
{
    private readonly ProfilePreferenceFile _file = new(profilePath, ".language");

    public string Read()
    {
        string? language = _file.Read();
        return language is ProductText.DefaultLanguage or ProductText.English ? language : ProductText.DefaultLanguage;
    }

    public bool TrySave(string language) => ProductText.IsSupported(language) && _file.TrySave(language);
}
