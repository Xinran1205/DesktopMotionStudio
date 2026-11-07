using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Automation;
using DesktopMotionStudio.Core.Diagnostics;
using DesktopMotionStudio.Core.Distribution;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;
using static StudioUiChecks;

internal static class F15NativeChecks
{
    private const string Binding = "f15-native-test-fixture";
    private const string TargetName = "DmsF15AcceptanceTarget.exe";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    internal static int Run(string fixture, int pid, string studio)
    {
        string profile = StudioStateRepository.CurrentUserPath();
        string target = Path.Combine(fixture, TargetName);
        string bundle = Path.GetDirectoryName(studio)!;
        string versions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "DesktopMotionStudio", "Versions");
        bool productBundle = string.Equals(bundle, Path.Combine(fixture, "candidate", "payload"), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetDirectoryName(bundle), versions, StringComparison.OrdinalIgnoreCase) &&
            Version.TryParse(Path.GetFileName(bundle), out _);
        var repository = new StudioStateRepository(profile);
        StudioState? state = repository.Load().State;
        if (state is not { Applications: [{ AppId: "f15-fixture", Target.Identifier: string saved }], Bindings: [{ BindingId: Binding, Enabled: true }] } ||
            !string.Equals(saved, target, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fixture).StartsWith("f15-", StringComparison.Ordinal) ||
            !productBundle || Path.GetFileName(studio) != "DesktopMotionStudio.Studio.exe" ||
            !LaunchTargetIdentityResolver.IsLocalPath(target) || !LaunchTargetIdentityResolver.IsLocalPath(studio))
            throw new InvalidOperationException("The test requires its own verified profile, executable and local paths.");

        using Process main = Process.GetProcessById(pid);
        if (!string.Equals(main.MainModule?.FileName, studio, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The supplied Studio process is not the fixture executable.");

        byte[] before = File.ReadAllBytes(profile), backup = File.ReadAllBytes(profile + ".bak");
        string launcher = InstalledLayout.LauncherForBundle(bundle);
        string renderer = Path.Combine(bundle, "MotionHost", "DesktopMotionStudio.MotionHost.exe");
        string held = renderer + ".f15-probe";
        string desktop = Directory.CreateTempSubdirectory("dms-f15-desktop-").FullName;
        string receipts = Path.Combine(desktop, "receipts");
        var logs = new DiagnosticLogStore(DiagnosticLogStore.CurrentUserDirectory());
        int failures = 0;
        bool moved = false;
        bool ownsTarget = false;
        Process? launchedTarget = null;
        void Check(string label, bool pass) => Program.Record("F15 native " + label, pass, ref failures);
        try
        {
            using (Process? existing = FindTarget(target))
                Require(existing is null, "The owned target was already running before this test.");
            ownsTarget = true;
            nint hwnd = Window(main, "Desktop Motion Studio");
            var home = AutomationElement.FromHandle(hwnd);
            AutomationElement action = Find(home, "测试启动");
            Require(Program.WaitFor(() => action.Current.IsEnabled), "Test launch did not become available.");
            Guid beforeCold = LastRequest(logs);
            bool keyboard = true;
            try { Keys(hwnd, 0x12, 0x54); }
            catch (InvalidOperationException) when (GetForegroundWindow() != hwnd)
            {
                keyboard = false;
                ((InvokePattern)action.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            }
            var cold = Observe(logs, beforeCold);
            Require(Program.WaitFor(() => ReadStatus(home) == "已打开目标应用。", Wait), "Studio did not report cold launch.");
            launchedTarget = TargetProcess(target);
            int originalPid = launchedTarget.Id;
            Check(keyboard ? "Alt+T opens one target and hands off" : "accessible action opens one target and hands off",
                cold is (DiagnosticResultCode.WindowReady, DiagnosticResultCode.HandedOff, DiagnosticFallback.None));

            Guid beforeWarm = LastRequest(logs);
            ((InvokePattern)action.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            var warm = Observe(logs, beforeWarm);
            Require(Program.WaitFor(() => ReadStatus(home).Contains("目标已运行", StringComparison.Ordinal), Wait), "Studio did not report reuse.");
            using (Process existing = TargetProcess(target))
                Check("warm launch reuses the same process without replay", existing.Id == originalPid &&
                    warm.Target is DiagnosticResultCode.Existing or DiagnosticResultCode.ForegroundDenied &&
                    warm.Animation == DiagnosticResultCode.NotStarted);
            CloseTarget(launchedTarget);
            launchedTarget = null;

            Require(LaunchTargetIdentityResolver.IsLocalPath(renderer) && !File.Exists(held), "The renderer is outside the isolated fault slot.");
            byte[] rendererHash = SHA256.HashData(File.ReadAllBytes(renderer));
            File.Move(renderer, held);
            moved = true;
            Guid beforeFallback = LastRequest(logs);
            ((InvokePattern)action.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            var fallback = Observe(logs, beforeFallback);
            Require(Program.WaitFor(() => ReadStatus(home).Contains("未播放动效", StringComparison.Ordinal), Wait), "Studio did not report fallback.");
            launchedTarget = TargetProcess(target);
            Check("renderer unavailable still opens the target with typed feedback",
                fallback is (DiagnosticResultCode.WindowReady, DiagnosticResultCode.RendererFailed, DiagnosticFallback.RendererUnavailable));
            CloseTarget(launchedTarget);
            launchedTarget = null;
            File.Move(held, renderer);
            moved = false;
            Check("renderer bytes are restored", SHA256.HashData(File.ReadAllBytes(renderer)).SequenceEqual(rendererHash));

            string realReceipts = Path.Combine(Path.GetDirectoryName(profile)!, "managed-shortcuts");
            Check("test launch preserved profile, backup and an empty test Desktop without product receipts",
                before.SequenceEqual(File.ReadAllBytes(profile)) && backup.SequenceEqual(File.ReadAllBytes(profile + ".bak")) &&
                !Directory.Exists(realReceipts) && !Directory.EnumerateFileSystemEntries(desktop).Any());

            var service = new ManagedShortcutService(repository, Path.Combine(bundle, "Scenes"), receipts, launcher, () => desktop);
            ShortcutResult shortcut = service.Create(Binding);
            Require(shortcut is { Status: ShortcutStatus.Created, Path: not null }, "The isolated comparison link could not be created.");
            Guid beforeShell = LastRequest(logs);
            using Process? link = Process.Start(new ProcessStartInfo(shortcut.Path!) { UseShellExecute = true });
            var shell = Observe(logs, beforeShell);
            launchedTarget = TargetProcess(target);
            Check("an actual managed Shell Link has the same dispatch and diagnostics", shell == cold);
            CloseTarget(launchedTarget);
            launchedTarget = null;
            Check("the isolated comparison link can be removed", service.Remove(Binding).Status == ShortcutStatus.Removed);
            return failures;
        }
        finally
        {
            try
            {
                if (moved) File.Move(held, renderer);
                Process? remaining = launchedTarget ?? (ownsTarget ? FindTarget(target) : null);
                if (remaining is not null) CloseTarget(remaining);
            }
            finally
            {
                string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
                if (string.Equals(Path.GetDirectoryName(desktop), parent, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(desktop).StartsWith("dms-f15-desktop-", StringComparison.Ordinal))
                    Directory.Delete(desktop, recursive: true);
            }
        }
    }

    private static Guid LastRequest(DiagnosticLogStore logs) => logs.Read().Events.LastOrDefault()?.RequestId ?? Guid.Empty;

    private static (DiagnosticResultCode Target, DiagnosticResultCode Animation, DiagnosticFallback Fallback) Observe(DiagnosticLogStore logs, Guid previous)
    {
        DiagnosticEvent? target = null, animation = null;
        Require(Program.WaitFor(() =>
        {
            IReadOnlyList<DiagnosticEvent> events = logs.Read().Events;
            animation = events.LastOrDefault(item => item.Stage == DiagnosticStage.Animation);
            target = animation is null ? null : events.LastOrDefault(item =>
                item.RequestId == animation.RequestId && item.Stage == DiagnosticStage.Target);
            return animation is not null && animation.RequestId != previous && target is not null;
        }, Wait), "The installed Launcher did not produce a new result.");
        return (target!.Result, animation!.Result, animation.Fallback);
    }

    private static string ReadStatus(AutomationElement window) => window.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.AutomationIdProperty, "TestLaunchStatus"))?.Current.Name ?? "";

    private static Process TargetProcess(string path)
        => FindTarget(path) ?? throw new InvalidOperationException("The test target did not open.");

    private static Process? FindTarget(string path)
    {
        Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));
        Process? match = processes.SingleOrDefault(process =>
            string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase));
        foreach (Process process in processes) if (process != match) process.Dispose();
        return match;
    }

    private static void CloseTarget(Process process)
    {
        using (process)
            if (!process.HasExited)
                Require(process.CloseMainWindow() && process.WaitForExit(5000), "The owned test target did not exit.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
