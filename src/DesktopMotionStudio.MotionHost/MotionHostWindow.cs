using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.MotionHost;

internal enum MotionHostExitReason { None, SkipRequested, HandedOff, TimedOut, Shutdown, ParentDisconnected, Failed, Completed }

internal sealed class MotionHostWindow : Form
{
    private const int ToolWindowExtendedStyle = 0x00000080;
    private const int AppWindowExtendedStyle = 0x00040000;
    private static readonly Color TransparentColor = Color.Magenta;
    // Leave IPC, painting and process-exit headroom inside the 300ms handoff acceptance bound.
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(200);
    private readonly MotionHostStartPayload _start;
    private readonly Panel _card;
    private readonly SceneSurface? _surface;
    private readonly bool _animationsEnabled;
    private readonly System.Windows.Forms.Timer _watchdogTimer;
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private Button? _stageExitButton;
    private Point _initialCursor;
    private Font? _headingFont;
    private Font? _detailFont;
    private long _visibleTimestamp;
    private long _fadeTimestamp;
    private int _closing;
    private int _handoffStarted;
    private MotionHostExitReason _fadeExitReason;

    public MotionHostWindow(MotionHostStartPayload start, bool? animationsEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        _start = start;
        if (!start.IsValid)
            throw new ArgumentException("MotionHost display request is invalid.", nameof(start));

        bool techFrame = start.Playback?.IsTechFrame == true;
        bool usePointerScreen = techFrame || start.DisplayMode is SceneDisplayMode.CompactCentered
            or SceneDisplayMode.LargeCentered or SceneDisplayMode.PointerScreenFull;
        Screen screen = usePointerScreen
            ? Screen.FromPoint(Cursor.Position)
            : Screen.PrimaryScreen ?? throw new InvalidOperationException("No primary display is available.");
        _animationsEnabled = animationsEnabled ?? SystemAnimationsEnabled();
        Text = start.Mode == MotionHostMode.Preview ? "Desktop Motion Studio — Preview" : "Desktop Motion Studio — Launch";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        AccessibleName = ProductText.Get(start.Mode == MotionHostMode.Preview ? "HostPreviewWindow" : "HostLaunchWindow");
        BackColor = TransparentColor;
        TransparencyKey = TransparentColor;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        Bounds = start.DisplayMode switch
        {
            SceneDisplayMode.Full or SceneDisplayMode.PointerScreenFull => screen.Bounds,
            SceneDisplayMode.CompactCentered => CenteredBounds(screen.WorkingArea, largeFrame: false, compactFrame: true),
            SceneDisplayMode.LargeCentered => CenteredBounds(screen.WorkingArea, largeFrame: true),
            _ => CenteredBounds(screen.WorkingArea, techFrame)
        };

        _card = new Panel
        {
            BackColor = Color.FromArgb(24, 30, 48),
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = ProductText.Get("HostAnimationStatus")
        };
        if (start.Playback is not null)
        {
            _surface = new SceneSurface(start.Playback, !_animationsEnabled) { Dock = DockStyle.Fill };
            _surface.RenderFailed += (_, _) => RequestImmediateExit(MotionHostExitReason.Failed);
            Controls.Add(_surface);
            if (techFrame) BuildStageExitButton();
            else BuildPlaybackFooter();
        }
        else { Controls.Add(_card); BuildCard(); }

        _watchdogTimer = new System.Windows.Forms.Timer { Interval = _surface is null ? 50 : 16 };
        _watchdogTimer.Tick += OnWatchdogTick;
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _fadeTimer.Tick += OnFadeTick;
        Resize += (_, _) => LayoutCard();
        KeyDown += OnKeyDown;
        FormClosing += OnFormClosing;
        Shown += (_, _) =>
        {
            _initialCursor = Cursor.Position;
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

    public void RequestHandoff() => PostToUiThread(() => BeginHandoff(MotionHostExitReason.HandedOff));

    public void RequestImmediateExit(MotionHostExitReason reason) => PostToUiThread(() => CloseWithReason(reason));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watchdogTimer.Dispose();
            _fadeTimer.Dispose();
            _card.Dispose();
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
            Text = ProductText.Get("HostPreparing"),
            ForeColor = Color.White,
            Font = _headingFont,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(Scaled(34), Scaled(38)),
            Size = new Size(Math.Max(Scaled(100), cardWidth - Scaled(68)), Scaled(48))
        };
        var detail = new Label
        {
            Text = ProductText.Get("HostPreparingDetail"),
            ForeColor = Color.FromArgb(205, 214, 235),
            Font = _detailFont,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(Scaled(36), Scaled(102)),
            Size = new Size(Math.Max(Scaled(100), cardWidth - Scaled(72)), Scaled(54))
        };
        var skip = new Button
        {
            Text = ProductText.Get("HostSkipAnimation"),
            AccessibleName = ProductText.Get("HostSkipAnimation"),
            AccessibleDescription = ProductText.Get("HostSkipDescription"),
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

    private void BuildPlaybackFooter()
    {
        bool preview = _start.Mode == MotionHostMode.Preview;
        int height = Math.Max(56, (int)Math.Round(64 * DeviceDpi / 96d));
        var footer = new Panel { Dock = DockStyle.Bottom, Height = height, BackColor = Color.FromArgb(24, 35, 58) };
        var caption = new Label
        {
            Text = ProductText.Get(preview ? "HostPreviewFooter" :
                _start.Mode == MotionHostMode.PreLaunch ? "HostPreLaunchFooter" : "HostLaunchFooter"),
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AccessibleName = ProductText.Get(preview ? "HostPreviewName" : "HostLaunchName")
        };
        var stop = new Button
        {
            Text = ProductText.Get(preview ? "HostStopEsc" : "HostSkipEsc"),
            AccessibleName = ProductText.Get(preview ? "HostStopPreview" : "HostSkipAnimation"),
            Dock = DockStyle.Right,
            Width = height * 2,
            TabIndex = 0,
            TabStop = true,
            BackColor = Color.FromArgb(67, 86, 132),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        stop.Click += (_, _) => CloseWithReason(MotionHostExitReason.SkipRequested);
        footer.Controls.Add(caption); footer.Controls.Add(stop); Controls.Add(footer);
        ActiveControl = stop;
    }

    private void BuildStageExitButton()
    {
        bool preview = _start.Mode == MotionHostMode.Preview;
        AccessibleDescription = ProductText.Get(preview ? "HostPreviewHelp" : "HostLaunchHelp");
        int Scale(int size) => Math.Max(1, (int)Math.Round(size * DeviceDpi / 96d));
        Size size = new(Scale(88), Scale(34));
        _stageExitButton = new Button
        {
            Text = ProductText.Get(preview ? "HostStop" : "HostSkip"),
            AccessibleName = ProductText.Get(preview ? "HostStopPreview" : "HostSkipAnimation"),
            AccessibleRole = AccessibleRole.PushButton,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = size,
            Location = new Point(Math.Max(0, ClientSize.Width - size.Width - Scale(12)), Scale(12)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(28, 39, 52),
            ForeColor = Color.White,
            TabStop = true,
            Visible = false
        };
        _stageExitButton.Click += (_, _) => CloseWithReason(MotionHostExitReason.SkipRequested);
        Controls.Add(_stageExitButton);
        _stageExitButton.BringToFront();
        _surface!.MouseMove += (_, _) =>
        {
            if (_visibleTimestamp != 0 && Cursor.Position != _initialCursor) _stageExitButton.Show();
        };
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            CloseWithReason(MotionHostExitReason.SkipRequested);
            return true;
        }
        if (keyData is Keys.Tab or (Keys.Shift | Keys.Tab) && _stageExitButton is { Visible: false } button)
        {
            button.Show();
            button.Focus();
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
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
        TimeSpan elapsed = Stopwatch.GetElapsedTime(_visibleTimestamp);
        _surface?.Present(elapsed);
        if (elapsed >= TimeSpan.FromMilliseconds(_start.MaximumVisibleMilliseconds))
            CloseWithReason(MotionHostExitReason.TimedOut);
        else if (_start.Playback is { } playback)
        {
            bool finite = _start.Mode != MotionHostMode.Launch || playback.IsTechFrame;
            int duration = !_animationsEnabled && (_start.Mode == MotionHostMode.PreLaunch ||
                playback.IsTechFrame && _start.Mode == MotionHostMode.Launch)
                ? 350 : playback.DurationMilliseconds;
            if (finite && elapsed.TotalMilliseconds >= duration) BeginHandoff(MotionHostExitReason.Completed);
        }
    }

    private void BeginHandoff(MotionHostExitReason reason)
    {
        if (Volatile.Read(ref _closing) != 0 || IsDisposed || Interlocked.Exchange(ref _handoffStarted, 1) != 0) return;
        _fadeExitReason = reason;
        if (!_animationsEnabled)
        {
            CloseWithReason(reason);
            return;
        }

        try
        {
            _fadeTimestamp = Stopwatch.GetTimestamp();
            _fadeTimer.Start();
        }
        catch (Exception)
        {
            CloseWithReason(reason);
        }
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _closing) != 0) return;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(_fadeTimestamp);
        if (elapsed >= FadeDuration)
        {
            CloseWithReason(_fadeExitReason);
            return;
        }

        try
        {
            double remaining = 1d - (elapsed.TotalMilliseconds / FadeDuration.TotalMilliseconds);
            Opacity = Math.Clamp(remaining, 0d, 1d);
        }
        catch (Exception)
        {
            CloseWithReason(_fadeExitReason);
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

    internal static Rectangle CenteredBounds(Rectangle workArea, bool largeFrame, bool compactFrame = false)
    {
        int width = Math.Min(760, workArea.Width);
        int height = Math.Min(420, workArea.Height);
        if (largeFrame || compactFrame)
        {
            width = Math.Min(1120, (int)(workArea.Width * .82));
            height = Math.Max(1, Math.Min(width * 9 / 16, (int)(workArea.Height * .84)));
            width = Math.Max(1, height * 16 / 9);
            if (compactFrame)
            {
                width = Math.Clamp(width * 3 / 4, 1, 760);
                height = Math.Clamp(height * 3 / 4, 1, 420);
            }
        }
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
