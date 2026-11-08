using System.Collections.Immutable;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.MotionHost;

namespace DesktopMotionStudio.MotionHost.Checks;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var definition = new ScenePlaybackDefinition(1, "studio-pulse", "1.0.0", "vector-orbit", 1, 3000, "#618EFF", "M",
            ImmutableArray.Create(new SceneKeyframe(0, .55, 0, -45), new SceneKeyframe(1500, 1.15, 1, 180), new SceneKeyframe(3000, .85, 0, 360)));
        int failures = 0;
        void Check(string name, bool passed) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}"); if (!passed) failures++; }
        foreach (bool enabled in new[] { true, false })
        {
            using var preview = new MotionHostWindow(new("studio-pulse", "1.0.0", SceneDisplayMode.Centered, 5000, MotionHostMode.Preview, definition), enabled);
            using var launch = new MotionHostWindow(new("studio-pulse", "1.0.0", SceneDisplayMode.Centered, 5000, MotionHostMode.Launch, definition), enabled);
            SceneSurface a = preview.Controls.OfType<SceneSurface>().Single(), b = launch.Controls.OfType<SceneSurface>().Single();
            int paintFailures = 0;
            a.RenderFailed += (_, _) => paintFailures++;
            b.RenderFailed += (_, _) => paintFailures++;
            a.Size = b.Size = new Size(640, 400);
            int[] times = [0, 250, 750, 1500, 2250, 3000];
            string[] previewPixels = times.Select(time => Fingerprint(a, time)).ToArray();
            Check($"production window paths share exact scene pixels, animations={enabled}", previewPixels.SequenceEqual(times.Select(time => Fingerprint(b, time))));
            Check($"production {(enabled ? "normal scene changes" : "reduced scene remains still")}", enabled ? previewPixels.Distinct().Count() >= 4 : previewPixels.Distinct().Count() == 1);
            Check($"actual frame paint has no suppressed errors, animations={enabled}", paintFailures == 0);
            Check($"preview identifies simulation and offers keyboard Stop, animations={enabled}", preview.Controls.Cast<Control>().SelectMany(Flatten).Any(control => control.Text.Contains("模拟交接", StringComparison.Ordinal)) && preview.Controls.Cast<Control>().SelectMany(Flatten).Any(control => control is Button && control.AccessibleName == "停止预览" && control.TabStop));
        }
        using var first = new SceneSurface(definition, false) { Size = new Size(640, 400) };
        using var second = new SceneSurface(definition with { SceneId = "other-work", Accent = "#EEAA22", Glyph = "S" }, false) { Size = first.Size };
        Check("authored second work changes actual output without runtime source edits", Fingerprint(first, 1500) != Fingerprint(second, 1500));
        first.Size = new Size(1, 1);
        int tinyFailures = 0; first.RenderFailed += (_, _) => tinyFailures++;
        Check("tiny viewport draws safely", Fingerprint(first, 1500).Length == 64 && tinyFailures == 0);
        ScenePlaybackDefinition tech = definition with
        {
            SceneId = "zero-boundary", RuntimeId = "tech-frame", DurationMilliseconds = 2000, Accent = "#70DCFD",
            Keyframes = ImmutableArray.Create(new SceneKeyframe(0, 1, 1, 0), new SceneKeyframe(2000, 1, 1, 0))
        };
        Check("F13 tech-frame is a supported production definition", tech.IsValid);
        if (!tech.IsValid) return 1;
        foreach (Size size in new[] { new Size(1120, 630), new Size(1600, 900), new Size(1280, 400) })
        {
            using var stage = new SceneSurface(tech, false) { Size = size };
            using var still = new SceneSurface(tech, true) { Size = size };
            using var orbit = new SceneSurface(definition, false) { Size = size };
            int faults = 0;
            stage.RenderFailed += (_, _) => faults++;
            still.RenderFailed += (_, _) => faults++;
            string[] frames = new[] { 240, 1100, 1700 }.Select(time => Fingerprint(stage, time)).ToArray();
            Check($"F13 shows three distinct authored beats at {size}", frames.Distinct().Count() == 3 &&
                frames[1] != Fingerprint(orbit, 1100));
            Check($"F13 static path uses the same composition at {size}", Fingerprint(still, 200) == Fingerprint(still, 1600));
            Check($"F13 painting succeeds at {size}", faults == 0);
        }
        foreach (bool english in new[] { false, true })
        {
            ProductText.UseLanguage(english ? ProductText.English : ProductText.DefaultLanguage);
            using var preview = new MotionHostWindow(new("zero-boundary", "1.0.0", SceneDisplayMode.Centered, 5000, MotionHostMode.Preview, tech));
            using var launch = new MotionHostWindow(new("studio-pulse", "1.0.0", SceneDisplayMode.Centered, 5000, MotionHostMode.Launch));
            Check($"F17 localizes window and fallback panel accessibility names, English={english}",
                preview.AccessibleName == (english ? "Desktop Motion Studio scene preview" : "Desktop Motion Studio 场景预览") &&
                launch.AccessibleName == (english ? "Desktop Motion Studio launch animation" : "Desktop Motion Studio 启动动效") &&
                launch.Controls.OfType<Panel>().Single().AccessibleName == (english ? "Animation status" : "动效状态") &&
                preview.Text == "Desktop Motion Studio — Preview" && launch.Text == "Desktop Motion Studio — Launch");
        }
        return failures == 0 ? 0 : 1;
    }

    private static IEnumerable<Control> Flatten(Control control) => new[] { control }.Concat(control.Controls.Cast<Control>().SelectMany(Flatten));

    private static string Fingerprint(SceneSurface surface, int elapsed)
    {
        surface.Present(TimeSpan.FromMilliseconds(elapsed));
        using var bitmap = new Bitmap(surface.Width, surface.Height);
        surface.DrawToBitmap(bitmap, surface.ClientRectangle);
        using var data = new MemoryStream(); bitmap.Save(data, ImageFormat.Bmp);
        return Convert.ToHexString(SHA256.HashData(data.ToArray()));
    }
}
