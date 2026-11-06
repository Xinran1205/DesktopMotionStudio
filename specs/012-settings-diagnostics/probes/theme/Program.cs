using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class Probe
{
    [STAThread]
    private static int Main()
    {
        var app = new Application();
        string[] keys =
        [
            "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush",
            "SolidBackgroundFillColorBaseBrush", "CardBackgroundFillColorDefaultBrush",
            "CardStrokeColorDefaultBrush", "ControlFillColorDefaultBrush",
            "ControlStrokeColorDefaultBrush", "AccentTextFillColorPrimaryBrush",
            "AccentFillColorDefaultBrush", "TextOnAccentFillColorPrimaryBrush",
            "SystemFillColorCriticalBrush", "LayerFillColorDefaultBrush"
        ];
        var text = new TextBlock();
        text.SetResourceReference(TextBlock.ForegroundProperty, keys[0]);
        var window = new Window { Content = text };
        foreach (string mode in new[] { ".Light", ".Dark", "", ".HC" })
        {
            var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent{mode}.xaml") };
            if (app.Resources.MergedDictionaries.Count == 0) app.Resources.MergedDictionaries.Add(dict);
            else app.Resources.MergedDictionaries[0] = dict;
            Console.WriteLine($"Mode {mode}, observed live text={text.Foreground}");
            foreach (string key in keys)
                Console.WriteLine($"{key}: {app.TryFindResource(key) ?? "<missing>"}");
            Console.WriteLine($"Button and ComboBox templates: {app.TryFindResource(typeof(Button)) is Style}, {app.TryFindResource(typeof(ComboBox)) is Style}");
        }
        foreach (string key in new[] { "AccentButtonStyle", "DefaultButtonStyle", "DefaultTextBoxStyle", "DefaultComboBoxStyle" }) Console.WriteLine($"{key}={app.TryFindResource(key) is Style}");
        return 0;
    }
}
