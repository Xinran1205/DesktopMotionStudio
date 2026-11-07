using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DesktopMotionStudio.MotionHost;

namespace DesktopMotionStudio.ZeroBoundary.Poc;

internal static class Program
{
    private const int Duration = 2000;
    private const int Fade = 180;
    private static readonly Color Accent = Color.FromArgb(112, 220, 253);

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        if (args is ["--target", var delay] && int.TryParse(delay, out int milliseconds) && milliseconds is >= 0 and <= 4000)
        {
            Thread.Sleep(milliseconds);
            using var target = new Form { Text = "Zero Boundary owned target", Size = new Size(620, 360) };
            Application.Run(target);
            return 0;
        }

        Screen screen = Screen.FromPoint(Cursor.Position);
        if (args is ["--demo", "centered" or "full"])
        {
            ShowDemo(screen, args[1] == "full");
            return 0;
        }
        if (args is not ["--check", var output] || !Path.IsPathFullyQualified(output)) return 2;
        Directory.CreateDirectory(output);
        using var dpiProbe = new Form { StartPosition = FormStartPosition.Manual, Bounds = screen.Bounds };
        _ = dpiProbe.Handle;
        Console.WriteLine($"Display={screen.Bounds}; work area={screen.WorkingArea}; DPI={dpiProbe.DeviceDpi}; monitors={Screen.AllScreens.Length}");
        int failures = 0;
        void Check(string description, bool passed)
        {
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {description}");
            if (!passed) failures++;
        }

        foreach (bool full in new[] { false, true })
        {
            string mode = full ? "full" : "centered";
            Rectangle bounds = GetBounds(screen, full);
            using var stage = new Stage(bounds);
            stage.Show();
            Application.DoEvents();
            RectangleF art = TechFramePainter.Composition(stage.ClientRectangle);
            Check($"{mode} shows on its chosen display with a contained 16:9 composition", stage.Visible && stage.Bounds == bounds &&
                stage.ClientRectangle.Contains(Rectangle.Ceiling(art)) && Math.Abs(art.Width / art.Height - 16f / 9) < .001f);
            while (stage.Visible && stage.Elapsed < Duration)
            {
                stage.Advance();
                Pump();
            }
            double end = stage.Elapsed;
            stage.Close();
            Check($"{mode} paints and naturally closes at {end:F1}ms", stage.Frames >= 3 && end is >= 1900 and <= 2500);
            foreach (int time in new[] { 240, 1100, 1700 })
                SaveFrame(Path.Combine(output, $"{mode}-{time}.png"), bounds.Size, time, reduced: false);
        }

        using (Process target = StartTarget(0))
        {
            nint window = WaitForWindow(target, 2000);
            using var pending = new Stage(GetBounds(screen, false));
            if (window == 0) pending.Show();
            Check("target already visible before overlay scheduling suppresses late show", window != 0 && !pending.Visible && pending.Frames == 0);
            StopTarget(target);
        }
        using (Process target = StartTarget(550))
        {
            using var stage = new Stage(GetBounds(screen, false));
            stage.Show();
            nint window = 0;
            while (stage.Elapsed < 1800 && (window = WindowOf(target)) == 0)
            {
                stage.Advance();
                Pump();
            }
            bool beforeRead = GetWindowRect(window, out NativeRectangle before);
            double ready = stage.Elapsed;
            double after = FinishHandoff(stage) - ready;
            Check($"visible target receives handoff in {after:F1}ms, without geometry changes", beforeRead &&
                after <= 300 && !stage.Visible && GetWindowRect(window, out NativeRectangle final) && before == final);
            StopTarget(target);
        }
        using (Process target = StartTarget(2750))
        {
            using var stage = new Stage(GetBounds(screen, false));
            stage.Show();
            while (stage.Elapsed < Duration)
            {
                stage.Advance();
                Pump();
            }
            double end = stage.Elapsed;
            stage.Close();
            Check($"late target remains pending when overlay closes at {end:F1}ms", end <= 2500 && !stage.Visible && !target.HasExited && WindowOf(target) == 0);
            Check("the same late target becomes visible after the overlay exits", WaitForWindow(target, 2200) != 0);
            StopTarget(target);
        }
        using (var stage = new Stage(GetBounds(screen, false)))
        {
            stage.Show();
            stage.Activate();
            nint handle = stage.Handle;
            _ = SetForegroundWindow(handle);
            if (GetForegroundWindow() != handle)
            {
                Point originalCursor = Cursor.Position;
                Point center = stage.PointToScreen(new Point(stage.ClientSize.Width / 2, stage.ClientSize.Height / 2));
                try
                {
                    Cursor.Position = center;
                    if (WindowFromPoint(center) == handle)
                    {
                        Input[] click = [new() { Mouse = new() { Flags = 2 } }, new() { Mouse = new() { Flags = 4 } }];
                        _ = SendInput((uint)click.Length, click, Marshal.SizeOf<Input>());
                        while (stage.Elapsed < 500 && GetForegroundWindow() != handle) Pump();
                    }
                }
                finally { Cursor.Position = originalCursor; }
            }
            bool foreground = GetForegroundWindow() == handle;
            if (foreground)
            {
                SendKeys.SendWait("{ESC}");
                while (stage.Visible && stage.Elapsed < 850) Pump();
            }
            Check($"native Esc closes the active stage (foreground={foreground}, received={stage.Escaped})",
                foreground && stage.Escaped && !stage.Visible);
        }
        using (var stage = new Stage(GetBounds(screen, false), reduced: true))
        {
            stage.Show();
            stage.Advance();
            Application.DoEvents();
            byte[] first = RenderFrame(stage.ClientSize, 200, reduced: true);
            byte[] second = RenderFrame(stage.ClientSize, 1500, reduced: true);
            double before = stage.Elapsed;
            double close = FinishHandoff(stage) - before;
            Check($"injected reduced-motion mode paints a static composition and hands off in {close:F1}ms",
                stage.Frames > 0 && first.AsSpan().SequenceEqual(second) && close <= 100 && !stage.Visible);
        }
        return failures == 0 ? 0 : 1;
    }

    private static void ShowDemo(Screen screen, bool full)
    {
        using var stage = new Stage(GetBounds(screen, full));
        stage.Show();
        while (stage.Visible && stage.Elapsed < Duration)
        {
            stage.Advance();
            Pump();
        }
    }

    private static Rectangle GetBounds(Screen screen, bool full)
    {
        if (full) return screen.Bounds;
        Rectangle work = screen.WorkingArea;
        int width = Math.Min(1120, (int)(work.Width * .82));
        int height = Math.Min((int)(width * 9 / 16f), (int)(work.Height * .84));
        width = (int)(height * 16 / 9f);
        return new Rectangle(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height);
    }

    private static Process StartTarget(int delay)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--target");
        start.ArgumentList.Add(delay.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the owned prototype target.");
    }

    private static nint WindowOf(Process target)
    {
        target.Refresh();
        return target.HasExited ? 0 : target.MainWindowHandle;
    }

    private static nint WaitForWindow(Process target, int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            nint window = WindowOf(target);
            if (window != 0) return window;
            Pump();
        }
        return 0;
    }

    private static void StopTarget(Process target)
    {
        if (!target.HasExited && (!target.CloseMainWindow() || !target.WaitForExit(1000))) target.Kill(entireProcessTree: false);
    }

    private static double FinishHandoff(Stage stage)
    {
        double before = stage.Elapsed;
        while (!stage.Reduced && stage.Elapsed - before < Fade)
        {
            stage.Opacity = Math.Clamp(1 - (stage.Elapsed - before) / Fade, 0, 1);
            Pump();
        }
        stage.Close();
        return stage.Elapsed;
    }

    private static void Pump() { Application.DoEvents(); Thread.Sleep(12); }

    private static void SaveFrame(string path, Size size, int time, bool reduced) => File.WriteAllBytes(path, RenderFrame(size, time, reduced));

    private static byte[] RenderFrame(Size size, int time, bool reduced)
    {
        using var image = new Bitmap(size.Width, size.Height);
        using (Graphics graphics = Graphics.FromImage(image))
            TechFramePainter.Paint(graphics, new Rectangle(Point.Empty, size), time / (double)Duration, Accent, reduced);
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private sealed class Stage : Form
    {
        private readonly Stopwatch _clock = new();
        internal double Elapsed => _clock.Elapsed.TotalMilliseconds;
        internal int Frames { get; private set; }
        internal bool Escaped { get; private set; }
        internal bool Reduced { get; }

        internal Stage(Rectangle bounds, bool reduced = false)
        {
            Reduced = reduced;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = bounds;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            Shown += (_, _) => _clock.Start();
        }

        internal void Advance() => Invalidate();

        protected override void OnPaint(PaintEventArgs args)
        {
            TechFramePainter.Paint(args.Graphics, ClientRectangle, Elapsed / Duration, Accent, Reduced);
            Frames++;
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData != Keys.Escape) return base.ProcessCmdKey(ref message, keyData);
            Escaped = true;
            Close();
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRectangle(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        internal uint Type;
        internal MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        internal int X;
        internal int Y;
        internal uint Data;
        internal uint Flags;
        internal uint Time;
        internal nuint Extra;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRectangle rect);
}
