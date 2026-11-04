using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.MotionHost;

internal enum MotionHostExitReason { None, SkipRequested, HandedOff, TimedOut, Shutdown, ParentDisconnected, Failed }

internal sealed class MotionHostWindow : Form
{
    private const int ToolWindowExtendedStyle = 0x00000080;
    private const int AppWindowExtendedStyle = 0x00040000;
    private static readonly Color TransparentColor = Color.Magenta;
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(250);
    private readonly MotionHostStartPayload _start;
    private readonly Panel _card;
    private readonly System.Windows.Forms.Timer _watchdogTimer;
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private Font? _headingFont;
    private Font? _detailFont;
    private long _visibleTimestamp;
    private long _fadeTimestamp;
    private int _closing;

    public MotionHostWindow(MotionHostStartPayload start)
    {
        ArgumentNullException.ThrowIfNull(start);
        _start = start;
        if (start.DisplayMode is not (SceneDisplayMode.Full or SceneDisplayMode.Centered) ||
            start.MaximumVisibleMilliseconds is <= 0 or > 15_000)
            throw new ArgumentException("MotionHost display request is invalid.", nameof(start));

        Screen screen = Screen.PrimaryScreen ?? throw new InvalidOperationException("No primary display is available.");
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        AccessibleName = "Desktop Motion Studio animation host";
        BackColor = TransparentColor;
        TransparencyKey = TransparentColor;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        Bounds = start.DisplayMode == SceneDisplayMode.Full
            ? screen.Bounds
            : CenteredBounds(screen.WorkingArea);

        _card = new Panel
        {
            BackColor = Color.FromArgb(24, 30, 48),
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Animation status"
        };
        Controls.Add(_card);
        BuildCard();

        _watchdogTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _watchdogTimer.Tick += OnWatchdogTick;
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _fadeTimer.Tick += OnFadeTick;
        Resize += (_, _) => LayoutCard();
        KeyDown += OnKeyDown;
        FormClosing += OnFormClosing;
        Shown += (_, _) =>
        {
            _visibleTimestamp = Stopwatch.GetTimestamp();
            _watchdogTimer.Start();
        };
    }

    public MotionHostExitReason ExitReason { get; private set; }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= ToolWindowExtendedStyle;
            parameters.ExStyle &= ~AppWindowExtendedStyle;
            return parameters;
        }
    }

    public void RequestHandoff() => PostToUiThread(BeginHandoff);

    public void RequestImmediateExit(MotionHostExitReason reason) => PostToUiThread(() => CloseWithReason(reason));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watchdogTimer.Dispose();
            _fadeTimer.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            _headingFont?.Dispose();
            _detailFont?.Dispose();
        }
    }

    private void BuildCard()
    {
        float scale = Math.Max(1, DeviceDpi) / 96f;
        int Scaled(int logicalPixels) => Math.Max(1, (int)Math.Round(logicalPixels * scale));
        int cardWidth = Math.Min(Scaled(680), ClientSize.Width);
        int cardHeight = Math.Min(Scaled(290), ClientSize.Height);
        _card.Size = new Size(cardWidth, cardHeight);

        _headingFont = new Font("Segoe UI", Scaled(22), FontStyle.Bold, GraphicsUnit.Pixel);
        _detailFont = new Font("Segoe UI", Scaled(12), FontStyle.Regular, GraphicsUnit.Pixel);
        var heading = new Label
        {
            Text = "Preparing your application",
            ForeColor = Color.White,
            Font = _headingFont,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(Scaled(34), Scaled(38)),
            Size = new Size(Math.Max(Scaled(100), cardWidth - Scaled(68)), Scaled(48))
        };
        var detail = new Label
        {
            Text = "Your application will continue opening if this animation is skipped or unavailable.",
            ForeColor = Color.FromArgb(205, 214, 235),
            Font = _detailFont,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(Scaled(36), Scaled(102)),
            Size = new Size(Math.Max(Scaled(100), cardWidth - Scaled(72)), Scaled(54))
        };
        var skip = new Button
        {
            Text = "Skip animation",
            AccessibleName = "Skip animation",
            AccessibleDescription = "Close the animation and continue opening the application.",
            AccessibleRole = AccessibleRole.PushButton,
            TabIndex = 0,
            Size = new Size(Scaled(160), Scaled(42)),
            Location = new Point(Scaled(36), Math.Max(Scaled(170), cardHeight - Scaled(78))),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(67, 86, 132),
            ForeColor = Color.White
        };
        skip.FlatAppearance.BorderColor = Color.FromArgb(140, 165, 220);
        skip.Click += (_, _) => CloseWithReason(MotionHostExitReason.SkipRequested);
        _card.Controls.Add(heading);
        _card.Controls.Add(detail);
        _card.Controls.Add(skip);
        ActiveControl = skip;
        LayoutCard();
    }

    private void LayoutCard() => _card.Location = new Point(
        Math.Max(0, (ClientSize.Width - _card.Width) / 2),
        Math.Max(0, (ClientSize.Height - _card.Height) / 2));

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Escape) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        CloseWithReason(MotionHostExitReason.SkipRequested);
    }

    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (Stopwatch.GetElapsedTime(_visibleTimestamp) >= TimeSpan.FromMilliseconds(_start.MaximumVisibleMilliseconds))
            CloseWithReason(MotionHostExitReason.TimedOut);
    }

    private void BeginHandoff()
    {
        if (Volatile.Read(ref _closing) != 0 || IsDisposed) return;
        if (!SystemAnimationsEnabled())
        {
            CloseWithReason(MotionHostExitReason.HandedOff);
            return;
        }

        try
        {
            _fadeTimestamp = Stopwatch.GetTimestamp();
            _fadeTimer.Start();
        }
        catch (Exception)
        {
            CloseWithReason(MotionHostExitReason.HandedOff);
        }
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _closing) != 0) return;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(_fadeTimestamp);
        if (elapsed >= FadeDuration)
        {
            CloseWithReason(MotionHostExitReason.HandedOff);
            return;
        }

        try
        {
            double remaining = 1d - (elapsed.TotalMilliseconds / FadeDuration.TotalMilliseconds);
            Opacity = Math.Clamp(remaining, 0d, 1d);
        }
        catch (Exception)
        {
            CloseWithReason(MotionHostExitReason.HandedOff);
        }
    }

    private void CloseWithReason(MotionHostExitReason reason)
    {
        if (reason == MotionHostExitReason.None || Interlocked.Exchange(ref _closing, 1) != 0) return;
        ExitReason = reason;
        _watchdogTimer.Stop();
        _fadeTimer.Stop();
        if (!IsDisposed) Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (ExitReason == MotionHostExitReason.None)
            ExitReason = MotionHostExitReason.SkipRequested;
    }

    private void PostToUiThread(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (IsHandleCreated && InvokeRequired)
        {
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
            return;
        }
        action();
    }

    private static Rectangle CenteredBounds(Rectangle workArea)
    {
        int width = Math.Min(760, workArea.Width);
        int height = Math.Min(420, workArea.Height);
        return new Rectangle(
            workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2,
            width, height);
    }

    private static bool SystemAnimationsEnabled()
    {
        const uint spiGetClientAreaAnimation = 0x1042;
        return SystemParametersInfo(spiGetClientAreaAnimation, 0, out bool enabled, 0) && enabled;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value,
        uint flags);
}
