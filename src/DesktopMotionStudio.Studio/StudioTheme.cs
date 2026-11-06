using System.Windows;

namespace DesktopMotionStudio.Studio;

internal static class StudioTheme
{
    private const string Prefix = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent";

    internal static void Apply(Application application, string choice)
    {
        string suffix = choice switch { "light" => ".Light", "dark" => ".Dark", _ => "" };
        string source = Prefix + suffix + ".xaml";
        var merged = application.Resources.MergedDictionaries;
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source?.OriginalString.StartsWith(Prefix, StringComparison.Ordinal) != true) continue;
            if (merged[i].Source.OriginalString != source)
                merged[i] = new ResourceDictionary { Source = new Uri(source) };
            return;
        }
        merged.Insert(0, new ResourceDictionary { Source = new Uri(source) });
    }
}
