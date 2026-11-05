using System.Collections.Immutable;
using System.Drawing.Imaging;
using System.Security.Cryptography;
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
