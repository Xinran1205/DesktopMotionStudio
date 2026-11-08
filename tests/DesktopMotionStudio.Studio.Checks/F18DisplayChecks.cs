using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;
using static StudioUiChecks;

internal static class F18DisplayChecks
{
    internal static int Run()
    {
        string studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH") ?? "";
        string targetSource = Environment.GetEnvironmentVariable("DMS_F06_TARGET_PATH") ?? "";
        string launcher = Environment.GetEnvironmentVariable("DMS_F10_FIXTURE_LAUNCHER_PATH") ?? "";
        if (!File.Exists(studio) || !File.Exists(targetSource) || !File.Exists(launcher)) return 2;
        string bundle = Path.GetDirectoryName(studio)!;
        string host = Path.Combine(bundle, "MotionHost", "DesktopMotionStudio.MotionHost.exe");
        string catalog = Path.Combine(bundle, "Scenes");
        var root = Directory.CreateTempSubdirectory("dms-f18-");
        string profile = Path.Combine(root.FullName, "profile.json");
        string targetDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "target")).FullName;
        string target = Path.Combine(targetDirectory, Path.GetFileName(targetSource));
        Process? process = null;
        try
        {
            foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(targetSource)!))
                File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)));
            var display = new MotionDisplayPreference(profile);
            Require(display.Read() == "recommended" && !display.TrySave("unknown"), "The display default or token allow-list is wrong.");
            File.WriteAllText(profile + ".motion-display", new string('x', 25));
            var store = new StudioStateRepository(profile);
            var state = StudioState.Empty with
            {
                Applications = [new("f18-app", "Display fixture", target, new(ApplicationTargetKind.Executable, target, []))],
                Bindings = [new("f18-binding", "f18-app", "studio-pulse", true)]
            };
            Require(store.Save(StudioState.Empty) == RepositoryError.None && store.Save(state) == RepositoryError.None, "Fixture save failed.");
            byte[] original = File.ReadAllBytes(profile);
            byte[] backup = File.ReadAllBytes(profile + ".bak");
            process = Start(studio, profile, catalog);
            var home = AutomationElement.FromHandle(Window(process, "Desktop Motion Studio"));
            SetDisplay(process, home, "当前屏幕全屏", "作品推荐", save: false);
            Require(File.ReadAllText(profile + ".motion-display") == new string('x', 25), "Cancel modified the invalid preference.");

            SetDisplay(process, home, "居中小窗", "作品推荐");
            Rect compact = Preview(home, host);
            SetDisplay(process, home, "居中大画幅", "居中小窗");
            Rect large = Preview(home, host);
            SetDisplay(process, home, "当前屏幕全屏", "居中大画幅");
            Rect full = Preview(home, host, full: true);
            Require(compact.Width < large.Width && large.Width < full.Width, $"Unexpected widths: {compact.Width}, {large.Width}, {full.Width}");
            Require(new MotionDisplayPreference(Path.Combine(root.FullName, "other.json")).Read() == "recommended", "Preference leaked across profiles.");
            Stop(process);
            process = Start(studio, profile, catalog);
            home = AutomationElement.FromHandle(Window(process, "Desktop Motion Studio"));
            SetDisplay(process, home, "居中小窗", "当前屏幕全屏", save: false);
            Require(display.Read() == "full", "Restart or cancel lost the saved placement.");
            Stop(process);
            process = null;

            var start = new ProcessStartInfo(launcher) { UseShellExecute = false };
            start.Environment["DMS_F10_FIXTURE_STATE"] = profile;
            start.ArgumentList.Add("--binding");
            start.ArgumentList.Add(ManagedLaunchRequest.EncodeBindingId("f18-binding")!);
            using var launch = Process.Start(start) ?? throw new InvalidOperationException("Launcher fixture did not start.");
            string launchHost = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(launcher)!, "..", "MotionHost", "DesktopMotionStudio.MotionHost.exe"));
            nint stage = 0;
            Require(Program.WaitFor(() => (stage = FindOwnedMotionWindow(launchHost, "Desktop Motion Studio — Launch")) != 0),
                "Managed launch did not show a real animation window.");
            Require(AutomationElement.FromHandle(stage).Current.BoundingRectangle == full, "Managed launch did not use the saved full screen.");
            Require(launch.WaitForExit(20_000) && launch.ExitCode == 0, "Managed launch did not complete successfully.");
            Require(File.ReadAllBytes(profile).SequenceEqual(original) && File.ReadAllBytes(profile + ".bak").SequenceEqual(backup),
                "Display-only operation changed profile or backup bytes.");
            Console.WriteLine($"PASS F18 compact {compact.Width}×{compact.Height}, large {large.Width}×{large.Height}, full {full.Width}×{full.Height}; cancel, persistence and full-screen managed launch.");
            return 0;
        }
        catch (Exception error) { Console.WriteLine($"FAIL F18 display UI: {error}"); return 1; }
        finally
        {
            if (process is not null) Stop(process);
            foreach (Process candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(target)))
            {
                using (candidate)
                {
                    try { if (string.Equals(candidate.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase)) Stop(candidate); }
                    catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
                }
            }
            root.Delete(recursive: true);
        }
    }

    private static void SetDisplay(Process process, AutomationElement home, string desired, string expected, bool save = true)
    {
        Invoke(home, "打开设置");
        var settings = AutomationElement.FromHandle(Window(process, "设置"));
        var box = settings.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "DisplayBox"))
            ?? throw new InvalidOperationException("Display selector was absent.");
        Require(box.Current.Name == "动效显示区域" && Program.WaitFor(() => box.Current.IsEnabled), "Display selector did not load.");
        var selection = (SelectionPattern)box.GetCurrentPattern(SelectionPattern.Pattern);
        Require(selection.Current.GetSelection().Single().Current.Name == expected, "Saved display choice did not load.");
        var dropDown = (ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        dropDown.Expand();
        ((SelectionItemPattern)Find(box, desired).GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        dropDown.Collapse();
        Invoke(settings, save ? "保存设置" : "取消设置");
        Require(Program.WaitFor(() => FindWindow(process.Id, "设置") == 0), "Settings did not close.");
    }

    private static Rect Preview(AutomationElement home, string host, bool full = false)
    {
        Require(Program.WaitFor(() => Find(home, "预览当前绑定作品").Current.IsEnabled), "Preview is not available.");
        Invoke(home, "预览当前绑定作品");
        nint stage = 0;
        Require(Program.WaitFor(() => (stage = FindOwnedMotionWindow(host, "Desktop Motion Studio — Preview")) != 0), "Preview did not open.");
        Rect bounds = AutomationElement.FromHandle(stage).Current.BoundingRectangle;
        var monitor = new MonitorInformation { Size = Marshal.SizeOf<MonitorInformation>() };
        Require(GetMonitorInfoW(MonitorFromWindow(stage, 2), ref monitor), "Could not read monitor bounds.");
        if (full)
            Require(bounds == monitor.Full.Bounds, "Full screen does not match native monitor bounds.");
        else
            Require(monitor.Work.Bounds.Contains(bounds) &&
                Math.Abs(bounds.Left + bounds.Width / 2 - (monitor.Work.Left + monitor.Work.Right) / 2d) <= 1 &&
                Math.Abs(bounds.Top + bounds.Height / 2 - (monitor.Work.Top + monitor.Work.Bottom) / 2d) <= 1,
                "The animation is not centered inside the monitor work area.");
        Keys(stage, 0x1B);
        Require(Program.WaitFor(() => FindOwnedMotionWindow(host, "Desktop Motion Studio — Preview") == 0), "Esc did not close preview.");
        return bounds;
    }

    private static void Invoke(AutomationElement root, string name) =>
        ((InvokePattern)Find(root, name).GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left, Top, Right, Bottom;
        internal readonly Rect Bounds => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInformation
    {
        internal int Size;
        internal NativeRect Full, Work;
        internal uint Flags;
    }

    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInformation information);
}
