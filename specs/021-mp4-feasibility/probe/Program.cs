using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;

namespace Mp4MotionProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return 2;
        if (args is ["--verify", string fixtures, string target]) return ProbeVerification.Run(fixtures, target);
        if (args is ["--fixture-stall"]) { Thread.Sleep(TimeSpan.FromSeconds(20)); return 4; }
        if (args is not ["--play", string path, "fit" or "fill"] || !Path.IsPathFullyQualified(path)) return 2;
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        var result = new ProbeResult();
        try
        {
            using var video = new NativeVideo();
            using var window = new VideoStage(video, args[2] == "fill", result);
            var file = new FileInfo(path);
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed ||
                !file.Exists || file.Length is 0 or > 64 * 1024 * 1024 ||
                !string.Equals(file.Extension, ".mp4", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Expected a bounded local MP4 fixture.");
            video.Open(file.FullName);
            window.Run();
        }
        catch (Exception error) { result.Fail(error); }
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Outcome switch { "completed" => 0, "skipped" => 10, "timeout" => 4, _ => 3 };
    }
}

internal sealed class ProbeResult
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    internal long Now => _clock.ElapsedMilliseconds;
    public string Outcome { get; set; } = "failed";
    public string? Error { get; set; }
    public long? FirstFrameMs { get; set; }
    public long? ShownMs { get; set; }
    public long? ClosedMs { get; set; }
    public int Frames { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public float DrawWidth { get; set; }
    public float DrawHeight { get; set; }
    public double DurationSeconds { get; set; }
    public bool HasAudio { get; set; }
    public bool Muted { get; set; }
    public bool FirstFrameHasContrast { get; set; }
    public int FadeInPaints { get; set; }
    public int FullOpacityPaints { get; set; }
    public int FadeOutPaints { get; set; }
    public double? FirstPaintOpacity { get; set; }
    public double MinimumFadeInOpacity { get; set; } = 1;
    public double MinimumFadeOutOpacity { get; set; } = 1;

    internal void Fail(Exception error)
    {
        Outcome = "failed";
        Error = $"{error.GetType().Name} (0x{error.HResult:X8})";
    }
}

internal sealed class VideoStage : Form
{
    internal const string StageTitle = "Desktop Motion Studio MP4 Probe";
    private const double FadeSeconds = .2;
    private static readonly Color Backdrop = Color.FromArgb(12, 15, 21);
    private readonly NativeVideo _video;
    private readonly bool _fill;
    private readonly ProbeResult _result;
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 15 };
    private double _position;

    internal VideoStage(NativeVideo video, bool fill, ProbeResult result)
    {
        _video = video;
        _fill = fill;
        _result = result;
        Text = StageTitle;
        BackColor = Backdrop;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new Size(Math.Min(960, work.Width), Math.Min(540, work.Height));
        Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        _clock.Tick += (_, _) => TickPlayback();
        Shown += (_, _) => _result.ShownMs = _result.Now;
        FormClosed += (_, _) => { _result.ClosedMs = _result.Now; Application.ExitThread(); };
    }

    internal void Run()
    {
        _clock.Start();
        Application.Run();
    }

    private void TickPlayback()
    {
        try
        {
            if (_result.FirstFrameMs is null && _result.Now >= 3000 ||
                _result.ShownMs is long shown && _result.Now - shown >= 10_500)
            {
                Finish("timeout");
                return;
            }
            bool changed = _video.TransferFrame();
            if (changed)
            {
                _result.Frames++;
                if (_result.FirstFrameMs is null)
                {
                    Bitmap frame = _video.Frame!;
                    _result.SourceWidth = frame.Width;
                    _result.SourceHeight = frame.Height;
                    _result.FirstFrameHasContrast = HasContrast(frame);
                    double duration = _video.Duration;
                    if (!double.IsFinite(duration) || duration is <= 0 or > 10)
                        throw new InvalidDataException("Unsupported probe duration.");
                    _result.DurationSeconds = duration;
                    _result.HasAudio = _video.HasAudio;
                    _result.Muted = _video.Muted;
                    _result.FirstFrameMs = _result.Now;
                    Show();
                }
            }
            if (_result.FirstFrameMs is null) return;
            _position = _video.Position;
            if (_video.Ended || _position >= _result.DurationSeconds) Finish("completed");
            else Invalidate();
        }
        catch (Exception error)
        {
            _result.Fail(error);
            Finish("failed");
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_video.Frame is not Bitmap frame) return;
        double visibleSeconds = _result.ShownMs is long shown ? (_result.Now - shown) / 1000d : 0;
        double remaining = _result.DurationSeconds - _position;
        double alpha = Math.Clamp(Math.Min(visibleSeconds, remaining) / FadeSeconds, 0, 1);
        _result.FirstPaintOpacity ??= alpha;
        float horizontal = (float)ClientSize.Width / frame.Width;
        float vertical = (float)ClientSize.Height / frame.Height;
        float scale = _fill ? Math.Max(horizontal, vertical) : Math.Min(horizontal, vertical);
        float width = frame.Width * scale, height = frame.Height * scale;
        var destination = new RectangleF((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
        e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
        e.Graphics.DrawImage(frame, destination);
        if (alpha < 1)
        {
            using var fade = new SolidBrush(Color.FromArgb((int)Math.Round(255 * (1 - alpha)), Backdrop));
            e.Graphics.FillRectangle(fade, ClientRectangle);
        }
        if (visibleSeconds < FadeSeconds)
        {
            _result.FadeInPaints++;
            _result.MinimumFadeInOpacity = Math.Min(_result.MinimumFadeInOpacity, alpha);
        }
        else if (remaining < FadeSeconds)
        {
            _result.FadeOutPaints++;
            _result.MinimumFadeOutOpacity = Math.Min(_result.MinimumFadeOutOpacity, alpha);
        }
        else if (alpha == 1) _result.FullOpacityPaints++;
        _result.CanvasWidth = ClientSize.Width;
        _result.CanvasHeight = ClientSize.Height;
        _result.DrawWidth = width;
        _result.DrawHeight = height;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
        Finish("skipped");
        return true;
    }

    private void Finish(string outcome)
    {
        _clock.Stop();
        _result.Outcome = outcome;
        if (Visible) Close();
        else Application.ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _clock.Dispose();
        base.Dispose(disposing);
    }

    private static bool HasContrast(Bitmap frame)
    {
        Color first = frame.GetPixel(frame.Width / 8, frame.Height / 8);
        return frame.GetPixel(frame.Width * 7 / 8, frame.Height * 7 / 8) != first ||
            frame.GetPixel(frame.Width / 2, frame.Height / 2) != first;
    }
}
