using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Core.Checks;

internal static class F06DesktopAcceptance
{
    private const uint MouseInput = 0;
    private const uint KeyboardInput = 1;
    private const uint KeyUp = 0x0002;
    private const uint MouseMove = 0x0001;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint Absolute = 0x8000;
    private const uint VirtualDesktop = 0x4000;
    private const int VirtualScreenLeft = 76;
    private const int VirtualScreenTop = 77;
    private const int VirtualScreenWidth = 78;
    private const int VirtualScreenHeight = 79;
    private const int MonitorCount = 80;
    private const int ExStyleIndex = -20;
    private const long ExToolWindow = 0x00000080;
    private const long ExAppWindow = 0x00040000;
    private const long ExTopMost = 0x00000008;
    private const uint MonitorDefaultToPrimary = 1;
    private const uint SpiGetClientAreaAnimation = 0x1042;

    internal static int Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("FAIL F06 desktop acceptance requires Windows.");
            return 1;
        }

        string? hostPath = Environment.GetEnvironmentVariable("DMS_F06_HOST_PATH");
        string? targetPath = Environment.GetEnvironmentVariable("DMS_F06_TARGET_PATH");
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath) ||
            string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
        {
            Console.WriteLine("FAIL set DMS_F06_HOST_PATH and DMS_F06_TARGET_PATH to the Release MotionHost and WindowHost POC executables.");
            return 1;
        }

        int failures = 0;
        string build = Convert.ToString(Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", "unknown"),
            CultureInfo.InvariantCulture) ?? "unknown";
        _ = SetProcessDpiAwarenessContext(new nint(-4)); // Per-monitor-v2; ignored when the process manifest already set awareness.
        Console.WriteLine($"F06 desktop acceptance · Windows build {build} · OS {Environment.OSVersion.Version} · monitors {GetSystemMetrics(MonitorCount)}");

        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"dms-f06-desktop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(targetPath)!))
            File.Copy(file, Path.Combine(temporaryDirectory, Path.GetFileName(file)));
        string targetCopy = Path.Combine(temporaryDirectory, Path.GetFileName(targetPath));

        Process? target = null;
        nint targetWindow = 0;
        try
        {
            var targetStart = new ProcessStartInfo
            {
                FileName = targetCopy,
                WorkingDirectory = temporaryDirectory,
                UseShellExecute = false
            };
            targetStart.ArgumentList.Add("--mode");
            targetStart.ArgumentList.Add("centered");
            target = Process.Start(targetStart);
            if (target is null)
            {
                Console.WriteLine("FAIL could not start the test-owned target POC.");
                return 1;
            }

            targetWindow = WaitForMainWindow(target, TimeSpan.FromSeconds(3));
            Record("test target POC creates a visible window", targetWindow != 0, ref failures);
            if (targetWindow == 0) return failures;
            Console.WriteLine($"Target PID {target.Id}, HWND 0x{targetWindow.ToInt64():X}");

            RunInteraction(hostPath, target, targetWindow, SceneDisplayMode.Full, Interaction.Escape, ref failures);
            RunInteraction(hostPath, target, targetWindow, SceneDisplayMode.Centered, Interaction.SkipButton, ref failures);
            RunHostCrash(hostPath, target, targetWindow, ref failures);

            bool animationsEnabled = ReadClientAreaAnimation();
            Console.WriteLine($"Windows client-area animation enabled: {animationsEnabled}");
            if (animationsEnabled)
            {
                Console.WriteLine("SKIP reduced-motion handoff on this desktop; the current Windows preference enables animations.");
                RunHandoff(hostPath, target, targetWindow, expectImmediate: false, ref failures);
            }
            else
            {
                Record("Windows desktop is in reduced-motion mode for this acceptance pass", true, ref failures);
                RunHandoff(hostPath, target, targetWindow, expectImmediate: true, ref failures);
                Console.WriteLine("Standard fade timing is covered by the Windows process integration run with animations enabled.");
            }

            Record("target remains alive and its window remains visible after all MotionHost interactions",
                !target.HasExited && IsWindowVisible(targetWindow), ref failures);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL F06 desktop acceptance: {ex.GetType().Name}: {ex.Message}");
            failures++;
        }
        finally
        {
            if (target is not null)
            {
                try
                {
                    if (!target.HasExited && targetWindow != 0) _ = PostMessage(targetWindow, 0x0010, 0, 0);
                    if (!target.HasExited && !target.WaitForExit(2_000)) target.Kill(entireProcessTree: false);
                    target.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAIL close the test-owned target POC: {ex.GetType().Name}: {ex.Message}");
                    failures++;
                }
            }

            try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        Console.WriteLine($"F06 desktop acceptance: {failures} failure(s).");
        return failures == 0 ? 0 : 1;
    }

    private static void RunInteraction(
        string hostPath,
        Process target,
        nint targetWindow,
        SceneDisplayMode mode,
        Interaction interaction,
        ref int failures)
    {
        var request = new MotionHostStartRequest(Guid.NewGuid(), "dragon-portal", "1.0.0", mode);
        var factory = new WindowsMotionHostProcessFactory(hostPath);
        HashSet<int> existingIds = GetProcessIds(Path.GetFileNameWithoutExtension(hostPath));
        IMotionHostSession session = factory.Start(request);
        try
        {
            using Process? host = WaitForNewProcess(hostPath, existingIds, TimeSpan.FromSeconds(2));
            nint window = host is null ? 0 : WaitForMainWindow(host, TimeSpan.FromSeconds(2));
            Record($"{mode} MotionHost creates a visible window", window != 0 && IsWindowVisible(window), ref failures);
            if (host is null || window == 0) return;

            Rect monitor = GetMonitorRectangle(window, workArea: false);
            Rect workArea = GetMonitorRectangle(window, workArea: true);
            Rect actual = GetWindowRectangle(window);
            Rect expected = mode == SceneDisplayMode.Full ? monitor : CenteredRectangle(workArea, 760, 420);
            bool placement = NearlyEqual(actual, expected, 1);
            Record($"{mode} MotionHost placement matches its primary-display contract", placement, ref failures);

            long style = GetWindowLongPtrW(window, ExStyleIndex).ToInt64();
            bool noTaskbarStyle = (style & ExToolWindow) != 0 && (style & ExAppWindow) == 0;
            Record("production MotionHost has no taskbar application-window style", noTaskbarStyle, ref failures);
            bool topMost = (style & ExTopMost) != 0;
            Record("MotionHost stays above the target window", topMost, ref failures);
            uint dpi = GetDpiForWindow(window);
            Console.WriteLine($"  {mode} · PID {host.Id} · HWND 0x{window.ToInt64():X} · DPI {dpi} · bounds {Format(actual)} · monitor {Format(monitor)} · work area {Format(workArea)} · style 0x{style:X}");

            bool activated = ActivateWindow(window);
            Record($"{interaction} test activates the MotionHost window", activated, ref failures);
            if (!activated) return;

            long started = Stopwatch.GetTimestamp();
            bool injected = interaction == Interaction.Escape
                ? SendEscape()
                : SendButtonClick(window);
            bool closed = injected && WaitUntilHidden(window, TimeSpan.FromMilliseconds(500));
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            Console.WriteLine($"  {interaction} visual-close latency: {elapsed.TotalMilliseconds:F1}ms");
            Record($"{interaction} hides the real MotionHost window within 500 ms",
                injected && closed && elapsed <= TimeSpan.FromMilliseconds(500), ref failures);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            MotionHostOutcome outcome = session.HandoffAsync(timeout.Token).GetAwaiter().GetResult();
            if (outcome != MotionHostOutcome.Skipped)
                Console.WriteLine($"  {interaction} outcome detail: {outcome}; failure={session.FailureReason?.ToString() ?? "none"}");
            Record($"{interaction} reports a skipped host lifecycle", outcome == MotionHostOutcome.Skipped, ref failures);
            bool hostExited = host.WaitForExit(1_000);
            Record($"{interaction} exits only its MotionHost child process", hostExited, ref failures);
            Record($"target POC remains alive after {interaction}", !target.HasExited && IsWindowVisible(targetWindow), ref failures);
        }
        finally
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void RunHandoff(
        string hostPath,
        Process target,
        nint targetWindow,
        bool expectImmediate,
        ref int failures)
    {
        var factory = new WindowsMotionHostProcessFactory(hostPath);
        HashSet<int> existingIds = GetProcessIds(Path.GetFileNameWithoutExtension(hostPath));
        IMotionHostSession session = factory.Start(new MotionHostStartRequest(
            Guid.NewGuid(), "dragon-portal", "1.0.0", SceneDisplayMode.Centered));
        try
        {
            using Process? host = WaitForNewProcess(hostPath, existingIds, TimeSpan.FromSeconds(2));
            nint window = host is null ? 0 : WaitForMainWindow(host, TimeSpan.FromSeconds(2));
            Record(expectImmediate ? "reduced-motion host becomes visible" : "standard-fade host becomes visible",
                window != 0 && IsWindowVisible(window), ref failures);
            if (host is null || window == 0) return;
            Console.WriteLine($"  Handoff host · PID {host.Id} · HWND 0x{window.ToInt64():X} · DPI {GetDpiForWindow(window)} · bounds {Format(GetWindowRectangle(window))}");

            long started = Stopwatch.GetTimestamp();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            MotionHostOutcome outcome = session.HandoffAsync(timeout.Token).GetAwaiter().GetResult();
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            Console.WriteLine($"  {(expectImmediate ? "reduced-motion close" : "standard fade")}: {elapsed.TotalMilliseconds:F1}ms, outcome={outcome}");
            bool timely = expectImmediate
                ? elapsed <= TimeSpan.FromMilliseconds(100)
                : elapsed <= TimeSpan.FromMilliseconds(300);
            Record(expectImmediate ? "reduced-motion handoff closes immediately (<=100 ms)" : "standard handoff fade completes within 300 ms",
                outcome == MotionHostOutcome.HandedOff && timely, ref failures);
            Record("target POC remains alive after handoff", !target.HasExited && IsWindowVisible(targetWindow), ref failures);
            Record("handoff MotionHost child exits", host.WaitForExit(1_000), ref failures);
        }
        finally
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void RunHostCrash(string hostPath, Process target, nint targetWindow, ref int failures)
    {
        var factory = new WindowsMotionHostProcessFactory(hostPath);
        HashSet<int> existingIds = GetProcessIds(Path.GetFileNameWithoutExtension(hostPath));
        IMotionHostSession session = factory.Start(new MotionHostStartRequest(
            Guid.NewGuid(), "dragon-portal", "1.0.0", SceneDisplayMode.Centered));
        try
        {
            using Process? host = WaitForNewProcess(hostPath, existingIds, TimeSpan.FromSeconds(2));
            nint window = host is null ? 0 : WaitForMainWindow(host, TimeSpan.FromSeconds(2));
            bool ready = host is not null && window != 0 && IsWindowVisible(window);
            Record("crash-cleanup host creates a visible test-owned window", ready, ref failures);
            if (!ready || host is null) return;

            Console.WriteLine($"  Crash-cleanup host · PID {host.Id} · HWND 0x{window.ToInt64():X} · DPI {GetDpiForWindow(window)}");
            long started = Stopwatch.GetTimestamp();
            host.Kill(entireProcessTree: false);
            bool hostExited = host.WaitForExit(1_000);
            bool windowClosed = WaitUntilHidden(window, TimeSpan.FromSeconds(1));
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            Console.WriteLine($"  crashed-host window cleanup: {elapsed.TotalMilliseconds:F1}ms");
            Record("crashing the owned MotionHost removes its HWND within one second",
                hostExited && windowClosed && elapsed <= TimeSpan.FromSeconds(1), ref failures);
            Record("target POC remains alive after MotionHost process crash",
                !target.HasExited && IsWindowVisible(targetWindow), ref failures);
        }
        finally
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    internal static bool ActivateWindow(nint window)
    {
        _ = ShowWindow(window, 5);
        _ = SetForegroundWindow(window);
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromMilliseconds(500))
        {
            if (GetForegroundWindow() == window) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    internal static bool SendEscape()
    {
        Input[] inputs =
        [
            new() { Type = KeyboardInput, Data = new InputUnion { Keyboard = new KeyboardInputData { VirtualKey = 0x1B } } },
            new() { Type = KeyboardInput, Data = new InputUnion { Keyboard = new KeyboardInputData { VirtualKey = 0x1B, Flags = KeyUp } } }
        ];
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static bool SendButtonClick(nint hostWindow)
    {
        nint button = FindChildButton(hostWindow, "Skip animation");
        if (button == 0) return false;
        Rect buttonRect = GetWindowRectangle(button);
        int x = buttonRect.Left + (buttonRect.Right - buttonRect.Left) / 2;
        int y = buttonRect.Top + (buttonRect.Bottom - buttonRect.Top) / 2;
        int left = GetSystemMetrics(VirtualScreenLeft);
        int top = GetSystemMetrics(VirtualScreenTop);
        int width = GetSystemMetrics(VirtualScreenWidth);
        int height = GetSystemMetrics(VirtualScreenHeight);
        if (width <= 1 || height <= 1) return false;
        int absoluteX = (int)Math.Round((x - left) * 65_535d / (width - 1));
        int absoluteY = (int)Math.Round((y - top) * 65_535d / (height - 1));
        uint flags = MouseMove | Absolute | VirtualDesktop;
        Input[] inputs =
        [
            new() { Type = MouseInput, Data = new InputUnion { Mouse = new MouseInputData { X = absoluteX, Y = absoluteY, Flags = flags } } },
            new() { Type = MouseInput, Data = new InputUnion { Mouse = new MouseInputData { X = absoluteX, Y = absoluteY, Flags = LeftDown } } },
            new() { Type = MouseInput, Data = new InputUnion { Mouse = new MouseInputData { X = absoluteX, Y = absoluteY, Flags = LeftUp } } }
        ];
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static nint FindChildButton(nint parent, string expectedText)
    {
        nint found = 0;
        EnumChildWindows(parent, (window, _) =>
        {
            var text = new System.Text.StringBuilder(128);
            _ = GetWindowText(window, text, text.Capacity);
            if (string.Equals(text.ToString(), expectedText, StringComparison.Ordinal))
            {
                found = window;
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    private static bool WaitUntilHidden(nint window, TimeSpan timeout)
    {
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < timeout)
        {
            if (!IsWindow(window) || !IsWindowVisible(window)) return true;
            Thread.Sleep(2);
        }
        return !IsWindow(window) || !IsWindowVisible(window);
    }

    private static Process? WaitForNewProcess(string executablePath, IReadOnlySet<int> existingIds, TimeSpan timeout)
    {
        string processName = Path.GetFileNameWithoutExtension(executablePath);
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < timeout)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            Process? match = processes.FirstOrDefault(process => !existingIds.Contains(process.Id));
            foreach (Process process in processes)
                if (!ReferenceEquals(process, match)) process.Dispose();
            if (match is not null) return match;
            Thread.Sleep(20);
        }
        return null;
    }

    private static HashSet<int> GetProcessIds(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        try { return processes.Select(process => process.Id).ToHashSet(); }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    private static nint WaitForMainWindow(Process process, TimeSpan timeout)
    {
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < timeout)
        {
            try
            {
                process.Refresh();
                if (process.HasExited) return 0;
                nint window = FindVisibleWindow(process.Id);
                if (window != 0) return window;
            }
            catch (InvalidOperationException) { return 0; }
            Thread.Sleep(20);
        }
        return 0;
    }

    private static nint FindVisibleWindow(int processId)
    {
        nint found = 0;
        EnumWindows((window, parameter) =>
        {
            if (!IsWindowVisible(window)) return true;
            _ = GetWindowThreadProcessId(window, out uint owner);
            if (owner != (uint)processId) return true;
            found = window;
            return false;
        }, 0);
        return found;
    }

    private static Rect GetMonitorRectangle(nint window, bool workArea)
    {
        nint monitor = MonitorFromWindow(window, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>(), Device = string.Empty };
        if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("GetMonitorInfo failed.");
        return workArea ? info.WorkArea : info.MonitorArea;
    }

    private static Rect GetWindowRectangle(nint window)
    {
        if (!GetWindowRect(window, out Rect rectangle)) throw new InvalidOperationException("GetWindowRect failed.");
        return rectangle;
    }

    private static Rect CenteredRectangle(Rect area, int maximumWidth, int maximumHeight)
    {
        int width = Math.Min(maximumWidth, area.Right - area.Left);
        int height = Math.Min(maximumHeight, area.Bottom - area.Top);
        int left = area.Left + ((area.Right - area.Left) - width) / 2;
        int top = area.Top + ((area.Bottom - area.Top) - height) / 2;
        return new Rect { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    private static bool NearlyEqual(Rect actual, Rect expected, int tolerance) =>
        Math.Abs(actual.Left - expected.Left) <= tolerance && Math.Abs(actual.Top - expected.Top) <= tolerance &&
        Math.Abs(actual.Right - expected.Right) <= tolerance && Math.Abs(actual.Bottom - expected.Bottom) <= tolerance;

    private static string Format(Rect rectangle) =>
        $"({rectangle.Left},{rectangle.Top})-({rectangle.Right},{rectangle.Bottom})";

    private static bool ReadClientAreaAnimation()
    {
        if (!SystemParametersInfoGet(SpiGetClientAreaAnimation, 0, out bool enabled, 0))
            throw new InvalidOperationException("SPI_GETCLIENTAREAANIMATION failed.");
        return enabled;
    }

    private static void Record(string name, bool passed, ref int failures)
    {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
        if (!passed) failures++;
    }

    private enum Interaction { Escape, SkipButton }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect MonitorArea;
        public Rect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInputData Mouse;
        [FieldOffset(0)] public KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData { public int X, Y; public uint Data, Flags, Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData { public ushort VirtualKey, ScanCode; public uint Flags, Time; public nint ExtraInfo; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowCallback(nint window, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetProcessDpiAwarenessContext(nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Rect rectangle);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumChildWindows(nint parent, EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, System.Text.StringBuilder text, int maximum);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfoGet(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
