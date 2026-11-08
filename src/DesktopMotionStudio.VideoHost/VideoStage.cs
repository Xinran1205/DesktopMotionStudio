using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.Video;

namespace DesktopMotionStudio.VideoHost;

internal sealed class VideoStage : Form
{
    private const double FadeSeconds = .2;
    private static readonly Color Backdrop = Color.FromArgb(12, 15, 21);
    private readonly NativeVideo _video;
    private readonly VideoPlaybackRequest _request;
    private readonly Action _visible;
    private readonly bool _reducedMotion;
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 15 };
    private readonly Button _exit;
    private long _shown;
    private double _duration;
    private VideoEventKind _outcome = VideoEventKind.Failed;

    internal VideoStage(NativeVideo video, VideoPlaybackRequest request, Action visible, bool reducedMotion)
    {
        _video = video;
        _request = request;
        _visible = visible;
        _reducedMotion = reducedMotion;
        Text = request.Preview ? "Desktop Motion Studio — Video preview" : "Desktop Motion Studio — Video launch";
        BackColor = Backdrop;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Area(Screen.FromPoint(Cursor.Position), request.DisplayMode);
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        _exit = new Button
        {
            Text = ProductText.Get(request.Preview ? "HostStop" : "HostSkip"),
            AccessibleName = ProductText.Get(request.Preview ? "HostStopPreview" : "HostSkipAnimation"),
            AutoSize = true,
            TabStop = true,
            Visible = false
        };
        Controls.Add(_exit);
        _exit.Click += (_, _) => Finish(VideoEventKind.Skipped);
        MouseMove += (_, _) =>
        {
            if (!_exit.Visible)
            {
                _exit.Location = new Point(Math.Max(0, ClientSize.Width - _exit.PreferredSize.Width - 12), 12);
                _exit.Show();
            }
        };
        FormClosed += (_, _) => Application.ExitThread();
        _clock.Tick += (_, _) => TickPlayback();
    }

    internal VideoEventKind Run()
    {
        _clock.Start();
        Application.Run();
        return _outcome;
    }

    private void TickPlayback()
    {
        if (_shown == 0)
        {
            if (!_video.TransferFrame()) return;
            double sourceDuration = _video.Duration;
            if (!double.IsFinite(sourceDuration) || sourceDuration < 1)
                throw new InvalidDataException("Unsupported MP4 duration.");
            _duration = _reducedMotion ? .35 : Math.Min(sourceDuration, _request.Clip.DurationMilliseconds / 1000d);
            _shown = Stopwatch.GetTimestamp();
            Show();
            Invalidate();
            Update();
            if (!_reducedMotion) _video.Play();
            _visible();
        }
        else if (Stopwatch.GetElapsedTime(_shown).TotalSeconds >= _duration)
        {
            bool reachedEnd = _reducedMotion || _video.ReachedEnd || _video.Position >= _duration - .35;
            Finish(reachedEnd ? VideoEventKind.Completed : VideoEventKind.Failed);
            return;
        }
        else if (!_reducedMotion)
        {
            _video.TransferFrame(Stopwatch.GetElapsedTime(_shown).TotalSeconds);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_video.Frame is not Bitmap frame) return;
        float horizontal = (float)ClientSize.Width / frame.Width;
        float vertical = (float)ClientSize.Height / frame.Height;
        float scale = _request.Clip.Fit == VideoFit.Fill ? Math.Max(horizontal, vertical) : Math.Min(horizontal, vertical);
        float width = frame.Width * scale, height = frame.Height * scale;
        var destination = new RectangleF((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
        e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
        e.Graphics.DrawImage(frame, destination);
        if (_reducedMotion) return;
        double elapsed = _shown == 0 ? 0 : Stopwatch.GetElapsedTime(_shown).TotalSeconds;
        double opacity = Math.Clamp(Math.Min(elapsed, _duration - elapsed) / FadeSeconds, 0, 1);
        if (_shown != 0 && elapsed < FadeSeconds) opacity = Math.Max(1d / 255, opacity);
        if (opacity < 1)
        {
            using var fade = new SolidBrush(Color.FromArgb((int)Math.Round(255 * (1 - opacity)), Backdrop));
            e.Graphics.FillRectangle(fade, ClientRectangle);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Finish(VideoEventKind.Skipped);
            return true;
        }
        if ((keyData is Keys.Tab or (Keys.Shift | Keys.Tab)) && !_exit.Visible)
        {
            _exit.Location = new Point(Math.Max(0, ClientSize.Width - _exit.PreferredSize.Width - 12), 12);
            _exit.Show();
            _exit.Focus();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Finish(VideoEventKind outcome)
    {
        _outcome = outcome;
        _clock.Stop();
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _clock.Dispose();
        base.Dispose(disposing);
    }

    private static Rectangle Area(Screen screen, SceneDisplayMode mode)
    {
        if (mode is SceneDisplayMode.Full or SceneDisplayMode.PointerScreenFull) return screen.Bounds;
        Rectangle work = screen.WorkingArea;
        int width = Math.Min(1120, (int)(work.Width * .82));
        int height = Math.Max(1, Math.Min(width * 9 / 16, (int)(work.Height * .84)));
        width = Math.Max(1, height * 16 / 9);
        if (mode == SceneDisplayMode.CompactCentered)
        {
            width = Math.Clamp(width * 3 / 4, 1, 760);
            height = Math.Clamp(height * 3 / 4, 1, 420);
        }
        return new Rectangle(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height);
    }

    internal static bool SystemAnimationsEnabled() => SystemParametersInfoW(0x1042, 0, out bool enabled, 0) && enabled;

    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
