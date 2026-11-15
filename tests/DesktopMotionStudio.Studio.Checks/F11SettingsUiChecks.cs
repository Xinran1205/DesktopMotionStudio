using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Studio;

internal static class F11SettingsUiChecks
{
    internal static void Run(ref int failures)
    {
        var results = new List<(string Name, bool Pass)>();
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f11-window-");
        try
        {
            var app = Application.Current;
            var store = new StudioStateRepository(Path.Combine(fixture.FullName, "state.json"));
            var service = new StudioSettingsService(store);
            var text = new TextBlock();
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            var liveWindow = new Window { Content = text };
            try
            {
                StudioTheme.Apply(app, "light");
                string light = text.Foreground.ToString();
                bool saved = RunDialog(new SettingsWindow(service), "dark", true, save: true);
                string dark = text.Foreground.ToString();
                results.Add(("saving dark and mute updates a live resource and profile", saved &&
                    service.Read().Settings == new StudioSettings("dark", true) && light != dark));
                byte[] original = File.ReadAllBytes(Path.Combine(fixture.FullName, "state.json"));
                bool canceled = !RunDialog(new SettingsWindow(service), "light", false, save: false);
                results.Add(("cancel does not write or apply an unsaved preference", canceled &&
                    File.ReadAllBytes(Path.Combine(fixture.FullName, "state.json")).SequenceEqual(original) &&
                    text.Foreground.ToString() == dark));
                bool followed = RunDialog(new SettingsWindow(service), "system", false, save: true);
                results.Add(("system mode and unmute persist after reopening the repository", followed &&
                    new StudioSettingsService(new StudioStateRepository(Path.Combine(fixture.FullName, "state.json"))).Read().Settings ==
                        new StudioSettings("system", false)));
            }
            finally { liveWindow.Close(); StudioTheme.Apply(app, "light"); }
        }
        finally
        {
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, parent, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f11-window-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
        foreach (var result in results) Program.Record("F11 UI " + result.Name, result.Pass, ref failures);
    }

    private static bool RunDialog(SettingsWindow window, string theme, bool muted, bool save)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(5)) { timer.Stop(); window.Close(); return; }
            if (window.FindName("SaveButton") is not Button { IsEnabled: true } button) return;
            timer.Stop();
            var themes = (ComboBox)window.FindName("ThemeBox");
            themes.SelectedItem = themes.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == theme);
            ((CheckBox)window.FindName("MuteBox")).IsChecked = muted;
            if (save) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else window.Close();
        };
        timer.Start();
        bool? result = window.ShowDialog();
        timer.Stop();
        return result == true;
    }
}
