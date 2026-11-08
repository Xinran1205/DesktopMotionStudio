using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Studio;

internal static class F16HomeChecks
{
    internal static int Run(string output)
    {
        string? executable = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH");
        if (!Path.IsPathFullyQualified(output) || !File.Exists(executable)) return 2;
        string catalog = Path.Combine(Path.GetDirectoryName(executable)!, "Scenes");
        Directory.CreateDirectory(output);
        var fixture = Directory.CreateTempSubdirectory("dms-f16-layout-");
        var outcomes = new List<(string Name, bool Pass)>();
        try { F09WindowChecks.Pump(() => CheckAsync(catalog, fixture.FullName, output, outcomes)); }
        catch (Exception ex) { outcomes.Add(($"{ex.GetType().Name}: {ex.Message}", false)); }
        finally { fixture.Delete(recursive: true); }
        int failures = 0;
        foreach (var outcome in outcomes) Program.Record("F16 " + outcome.Name, outcome.Pass, ref failures);
        return failures == 0 ? 0 : 1;
    }

    private static async Task CheckAsync(string catalog, string root, string output, List<(string, bool)> results)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DesktopMotionStudio.Studio;component/Styles.xaml", UriKind.Relative)
        });
        string path = Path.Combine(root, "state.json");
        var store = new StudioStateRepository(path);
        string target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        string missing = Path.Combine(root, "missing.exe");
        var state = StudioState.Empty with
        {
            Applications =
            [
                new("ready", "A 工作台", target, new(ApplicationTargetKind.Executable, target, [])),
                new("choose", "B 待选择", target, new(ApplicationTargetKind.Executable, target, [])),
                new("repair", "C " + new string('长', 105), missing, new(ApplicationTargetKind.Executable, missing, [])),
                new("unavailable", "D 作品不可用", target, new(ApplicationTargetKind.Executable, target, []))
            ],
            Bindings = [new("one", "ready", "zero-boundary", true), new("two", "repair", "studio-pulse", true),
                new("three", "unavailable", "no-such-work", true)]
        };
        if (store.Save(state) != RepositoryError.None) throw new IOException("Could not create isolated F16 fixture.");
        foreach (string theme in new[] { "dark", "light", "system" })
        {
            StudioTheme.Apply(app, theme);
            var home = new MainWindow(store, catalog, testLaunch: new(missing)) { Width = 900, Height = 560 };
            home.Show();
            try
            {
                await home.RefreshAsync("ready");
                var cover = (Image)home.FindName("BoundCover");
                for (int attempt = 0; cover.Source is null && attempt < 20; attempt++) await Task.Delay(15);
                home.UpdateLayout();
                DpiScale dpi = VisualTreeHelper.GetDpi(home);
                string tag = $"{theme}-{dpi.PixelsPerInchX:F0}{(SystemParameters.HighContrast ? "-contrast" : "")}";
                Console.WriteLine($"  F16 {tag}, window={home.ActualWidth:F0}x{home.ActualHeight:F0}");
                if (Environment.GetEnvironmentVariable("DMS_F16_EXPECT_DPI") is string requestedDpi && int.TryParse(requestedDpi, out int value))
                    results.Add(($"actual DPI {requestedDpi}", Math.Abs(dpi.PixelsPerInchX - value) < 1));
                if (Environment.GetEnvironmentVariable("DMS_F16_EXPECT_CONTRAST") is string requestedContrast)
                    results.Add(("actual high contrast preference", SystemParameters.HighContrast == (requestedContrast == "1")));
                var viewport = (ScrollViewer)home.FindName("DetailsScroll");
                var test = (Button)home.FindName("TestLaunchButton");
                var preview = (Button)home.FindName("PreviewBoundButton");
                results.Add(($"{tag} shows the real cover and actions in the first viewport",
                    cover.Source is not null && Inside(cover, viewport) && Inside(test, viewport) && Inside(preview, viewport) &&
                    test.Tag is true && test.IsEnabled));
                results.Add(($"{tag} primary button text retains readable contrast", Contrast(test.Foreground, test.Background) >= 4.5));
                Save(home, output, tag + "-ready");
                var rows = (ListBox)home.FindName("ApplicationList");
                rows.SelectedIndex = 1;
                home.UpdateLayout();
                results.Add(($"{tag} shows Choose Motion on the unbound first screen",
                    Inside((Button)home.FindName("ReplaceButton"), viewport) && ((Button)home.FindName("ReplaceButton")).Tag is true));
                if (theme == "dark") Save(home, output, tag + "-unbound");
                rows.SelectedIndex = 2;
                home.UpdateLayout();
                var repair = (Button)home.FindName("RepairButton");
                results.Add(($"{tag} keeps the repair action visible alongside a long application name",
                    repair.Tag is true && Inside(repair, viewport) && !test.IsEnabled));
                rows.SelectedIndex = 3;
                results.Add(($"{tag} retains native Test Launch for unavailable works", test.IsEnabled && !preview.IsEnabled));
                var search = (TextBox)home.FindName("SearchBox");
                search.Text = "no match";
                var clear = (Button)home.FindName("ClearSearchButton");
                results.Add(($"{tag} search recovery is visible", clear.Tag is true && clear.IsVisible && clear.IsEnabled));
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                results.Add(($"{tag} clear search restores the list", search.Text.Length == 0 && rows.Items.Count == 4));
            }
            finally { home.Close(); }
        }
        var empty = new MainWindow(new StudioStateRepository(Path.Combine(root, "empty.json")), catalog) { Width = 900, Height = 560 };
        try { empty.Show(); await empty.RefreshAsync(); empty.UpdateLayout(); Save(empty, output, "empty"); }
        finally { empty.Close(); }
    }

    private static bool Inside(FrameworkElement element, FrameworkElement viewport)
    {
        if (!element.IsVisible || element.ActualHeight <= 0) return false;
        Rect bounds = element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
        return bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= viewport.ActualWidth && bounds.Bottom <= viewport.ActualHeight;
    }

    private static double Contrast(Brush foreground, Brush background)
    {
        if (foreground is not SolidColorBrush front || background is not SolidColorBrush back) return 0;
        double a = Luminance(front.Color), b = Luminance(back.Color);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            double channel = value / 255d;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    private static void Save(Window window, string directory, string name)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        image.Render(window);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using FileStream stream = File.Create(Path.Combine(directory, name + ".png"));
        png.Save(stream);
    }
}
