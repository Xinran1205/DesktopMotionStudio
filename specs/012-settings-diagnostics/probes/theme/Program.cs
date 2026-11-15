using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopMotionStudio.Studio;

internal static class Probe
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application();
        if (args is ["--watch", var choice] && choice is "system" or "dark" or "light") return Watch(app, choice);
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

    private static int Watch(Application app, string choice)
    {
        const string key = "TextFillColorPrimaryBrush";
        StudioTheme.Apply(app, choice);
        var display = new TextBlock { Text = choice + " palette", Margin = new Thickness(20) };
        display.SetResourceReference(TextBlock.ForegroundProperty, key);
        var watch = new Window { Title = $"F11 Theme Probe — {choice}", Width = 320, Height = 140, Content = display };
        watch.SetResourceReference(Window.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        string? previous = null;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            string sample = $"choice={choice} contrast={SystemParameters.HighContrast} foreground={display.Foreground} background={watch.Background}";
            if (sample != previous)
            {
                Console.WriteLine($"{elapsed.ElapsedMilliseconds}ms {sample}");
                previous = sample;
            }
            if (elapsed.Elapsed > TimeSpan.FromMinutes(6)) app.Shutdown();
        };
        timer.Start();
        return app.Run(watch);
    }
}
