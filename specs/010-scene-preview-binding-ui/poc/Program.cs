using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace SharedPlaybackPoc;

internal enum PlaybackPath { Preview, LaunchSimulation }
internal readonly record struct SceneFrame(double Scale, double Opacity, double Rotation, bool Completed);
internal readonly record struct Keyframe(int Time, double Scale, double Opacity, double Rotation);

internal static class Pulse
{
    internal const int Duration = 8000;
    private static readonly Keyframe[] Frames =
    [
        new(0, .55, 0, -45), new(1000, 1, 1, 0), new(3500, 1.15, 1, 180),
        new(6500, 1, 1, 315), new(Duration, .85, 0, 360)
    ];

    internal static SceneFrame Evaluate(double elapsed, bool reduced)
    {
        double time = Math.Clamp(elapsed, 0, Duration);
        if (reduced) return new(1, 1, 0, time >= Duration);
        int index = 0;
        while (index < Frames.Length - 2 && time > Frames[index + 1].Time) index++;
        Keyframe a = Frames[index], b = Frames[index + 1];
        double fraction = (time - a.Time) / (b.Time - a.Time);
        double smooth = fraction * fraction * (3 - 2 * fraction);
        double Mix(double start, double end) => start + (end - start) * smooth;
        return new(Mix(a.Scale, b.Scale), Mix(a.Opacity, b.Opacity), Mix(a.Rotation, b.Rotation), time >= Duration);
    }

    // Both paths call this same paint implementation. Path/target selection is not a renderer capability.
    internal static void Paint(Graphics graphics, Rectangle bounds, SceneFrame frame)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(bounds, Color.FromArgb(13, 22, 43), Color.FromArgb(25, 45, 79), 35);
        graphics.FillRectangle(background, bounds);
        float size = Math.Min(bounds.Width, bounds.Height);
        float centerX = bounds.Left + bounds.Width / 2f, centerY = bounds.Top + bounds.Height / 2f;
        float radius = size * .28f * (float)frame.Scale;
        int Alpha(double amount) => Math.Clamp((int)Math.Round(amount * frame.Opacity), 0, 255);
        for (int i = 12; i > 0; i--)
        {
            using var glow = new Pen(Color.FromArgb(Alpha(7), 97, 142, 255), 5);
            float outer = radius + i * 3;
            graphics.DrawEllipse(glow, centerX - outer, centerY - outer, outer * 2, outer * 2);
        }
        using var ring = new Pen(Color.FromArgb(Alpha(230), 133, 169, 255), 3);
        graphics.DrawEllipse(ring, centerX - radius, centerY - radius, radius * 2, radius * 2);
        for (int i = 0; i < 3; i++)
        {
            double angle = (frame.Rotation + i * 120) * Math.PI / 180;
            float x = centerX + radius * (float)Math.Cos(angle), y = centerY + radius * (float)Math.Sin(angle);
            using var dot = new SolidBrush(Color.FromArgb(Alpha(255), 181, 206, 255));
            graphics.FillEllipse(dot, x - 7, y - 7, 14, 14);
        }
        using var font = new Font("Segoe UI", size * .16f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var text = new SolidBrush(Color.FromArgb(Alpha(255), 235, 241, 255));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("M", font, text, new RectangleF(centerX - size / 4, centerY - size / 4, size / 2, size / 2), format);
    }

    internal static string RenderFingerprint(PlaybackPath path, double elapsed, bool reduced)
    {
        if (!Enum.IsDefined(path)) throw new ArgumentOutOfRangeException(nameof(path));
        using var bitmap = new Bitmap(640, 400);
        using (Graphics graphics = Graphics.FromImage(bitmap)) Paint(graphics, new Rectangle(0, 0, bitmap.Width, bitmap.Height), Evaluate(elapsed, reduced));
        using var bytes = new MemoryStream();
        bitmap.Save(bytes, ImageFormat.Bmp);
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
}

internal sealed class SceneCanvas(Func<SceneFrame> evaluate) : Control
{
    internal int PaintedFrames { get; private set; }
    protected override void OnPaint(PaintEventArgs e) { Pulse.Paint(e.Graphics, ClientRectangle, evaluate()); PaintedFrames++; }
    protected override void OnHandleCreated(EventArgs e) { DoubleBuffered = true; base.OnHandleCreated(e); }
}

internal sealed class PlaybackWindow : Form
{
    private readonly Stopwatch _time = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly SceneCanvas _canvas;
    private string _reason = "window-close";

    internal PlaybackWindow(PlaybackPath path, bool reduced)
    {
        Text = $"F09 Shared Playback POC - {path}{(reduced ? " (reduced)" : string.Empty)}";
        ClientSize = new Size(900, 590); MinimumSize = new Size(600, 420);
        StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.FromArgb(24, 35, 58) };
        var label = new Label { Text = path == PlaybackPath.Preview ? "预览 · 原创中性开发作品 · 模拟交接，不启动应用" : "启动渲染模拟 · 同一作品执行器 · 未连接真实目标", ForeColor = Color.White, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        var stop = new Button { Text = "停止 / Esc", Dock = DockStyle.Right, Width = 120, AccessibleName = "停止预览" };
        stop.Click += (_, _) => { _reason = "stop"; Close(); };
        footer.Controls.Add(label); footer.Controls.Add(stop);
        _canvas = new SceneCanvas(() => Pulse.Evaluate(_time.Elapsed.TotalMilliseconds, reduced)) { Dock = DockStyle.Fill };
        Controls.Add(_canvas); Controls.Add(footer);
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { _reason = "escape"; Close(); } };
        _timer.Tick += (_, _) =>
        {
            if (_time.Elapsed.TotalMilliseconds >= Pulse.Duration) { _reason = "completed"; Close(); }
            else _canvas.Invalidate();
        };
        Shown += (_, _) =>
        {
            _time.Start(); _timer.Start();
            Console.WriteLine($"SHOWN_EVENT PID={Environment.ProcessId} HWND=0x{Handle.ToInt64():X} DPI={DeviceDpi} Path={path} Reduced={reduced}");
        };
        FormClosed += (_, _) => { _timer.Stop(); Console.WriteLine($"EXIT Reason={_reason} Frames={_canvas.PaintedFrames} ElapsedMs={_time.ElapsedMilliseconds}"); };
    }

    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--checks"]) return Checks();
        if (args is ["--owned-job-checks"]) return OwnedJobPoc.Checks();
        if (args is ["--job-parent"]) return OwnedJobPoc.Parent();
        if (args is ["--job-parent-atomic"]) return OwnedJobPoc.AtomicParent();
        if (args is ["--job-worker"]) { Thread.Sleep(Timeout.Infinite); return 0; }
        if (!Parse(args, out PlaybackPath path, out bool reduced)) return 2;
        ApplicationConfiguration.Initialize();
        using var window = new PlaybackWindow(path, reduced);
        Application.Run(window);
        return 0;
    }

    private static bool Parse(string[] args, out PlaybackPath path, out bool reduced)
    {
        path = PlaybackPath.Preview; reduced = false;
        if (args.Length is not (2 or 3) || args[0] != "--mode") return false;
        if (args[1] == "launch-simulation") path = PlaybackPath.LaunchSimulation;
        else if (args[1] != "preview") return false;
        if (args.Length == 3) { if (args[2] != "--reduced") return false; reduced = true; }
        return true;
    }

    private static int Checks()
    {
        int failures = 0;
        void Check(string name, bool passed) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}"); if (!passed) failures++; }
        int[] times = [0, 500, 1500, 3500, 6500, Pulse.Duration];
        foreach (bool reduced in new[] { false, true })
            Check($"shared pixels for preview/launch-simulation reduced={reduced}", times.All(time => Pulse.RenderFingerprint(PlaybackPath.Preview, time, reduced) == Pulse.RenderFingerprint(PlaybackPath.LaunchSimulation, time, reduced)));
        Check("authored normal timeline visibly changes", times.Select(time => Pulse.RenderFingerprint(PlaybackPath.Preview, time, false)).Distinct().Count() >= 4);
        Check("reduced-motion geometry remains still", new[] { 500, 3500, 6500 }.Select(time => Pulse.RenderFingerprint(PlaybackPath.Preview, time, true)).Distinct().Count() == 1);
        Check("elapsed time clamps at both endpoints", Pulse.Evaluate(-100, false) == Pulse.Evaluate(0, false) && Pulse.Evaluate(Pulse.Duration + 100, false) == Pulse.Evaluate(Pulse.Duration, false));
        Check("completion depends on elapsed time rather than tick count", !Pulse.Evaluate(Pulse.Duration - 1, false).Completed && Pulse.Evaluate(Pulse.Duration, false).Completed);
        Check("target and arbitrary mode arguments rejected", !Parse(["--mode", "preview", "--target", "app.exe"], out _, out _) && !Parse(["--mode", "launch"], out _, out _));
        return failures == 0 ? 0 : 1;
    }
}
