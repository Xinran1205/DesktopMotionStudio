using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Automation;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Diagnostics;
using DesktopMotionStudio.Core.State;
using static StudioUiChecks;

internal static class F10UiChecks
{
    internal static int Run(bool diagnostics = false)
    {
        string studio = RequiredPath("DMS_F08_STUDIO_PATH");
        string launcher = RequiredPath("DMS_F10_FIXTURE_LAUNCHER_PATH");
        string target = RequiredPath("DMS_F06_TARGET_PATH");
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f10-ui-");
        string root = fixture.FullName;
        string bundle = Path.Combine(root, "Bundle");
        string desktop = Directory.CreateDirectory(Path.Combine(root, "Desktop")).FullName;
        string state = Path.Combine(root, "state.json");
        string targetCopy = Path.Combine(root, "Target", Path.GetFileName(target));
        int failures = 0;
        Process? studioProcess = null;
        string? previous = Environment.GetEnvironmentVariable("DMS_F10_FIXTURE_STATE");
        string? previousLogMode = Environment.GetEnvironmentVariable("DMS_F11_FIXTURE_LOG_MODE");
        var logs = new DiagnosticLogStore(Path.Combine(root, "logs"));
        void Check(string description, bool passed) => Program.Record("F10 UI " + description, passed, ref failures);

        try
        {
            CopyTree(Path.GetDirectoryName(studio)!, bundle);
            string bundledLauncher = Path.Combine(bundle, "Launcher", Path.GetFileName(launcher));
            foreach (string name in new[]
            {
                "DesktopMotionStudio.Launcher.exe", "DesktopMotionStudio.Launcher.dll", "DesktopMotionStudio.Launcher.deps.json",
                "DesktopMotionStudio.Launcher.runtimeconfig.json", "DesktopMotionStudio.Core.dll"
            })
                File.Copy(Path.Combine(Path.GetDirectoryName(launcher)!, name), Path.Combine(bundle, "Launcher", name), overwrite: true);
            Require(File.Exists(bundledLauncher), "Fixture Launcher was not staged.");
            CopyTree(Path.GetDirectoryName(target)!, Path.GetDirectoryName(targetCopy)!);
            string catalog = Path.Combine(bundle, "Scenes");
            F09WindowChecks.WriteScene(catalog, "scene-one", "First work");
            F09WindowChecks.WriteScene(catalog, "scene-two", "Second work");
            var store = new StudioStateRepository(state);
            var saved = new ApplicationProfile("fixture", "Fixture app", targetCopy,
                new(ApplicationTargetKind.Executable, targetCopy, [], Path.GetDirectoryName(targetCopy)));
            var binding = new ApplicationBinding("旧版 fixture", saved.AppId, "scene-one", true);
            StudioState initial = StudioState.Empty with { Applications = [saved], Bindings = [binding] };
            Require(store.Save(initial) == RepositoryError.None && store.Save(initial) == RepositoryError.None, "Fixture state save failed.");
            byte[] initialBytes = File.ReadAllBytes(state), initialBackup = File.ReadAllBytes(state + ".bak");
            Environment.SetEnvironmentVariable("DMS_F10_FIXTURE_STATE", state, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("DMS_F11_FIXTURE_LOG_MODE", diagnostics ? "record" : null, EnvironmentVariableTarget.Process);
            string unrelated = Path.Combine(desktop, "Existing.lnk");
            File.WriteAllText(unrelated, "foreign original");
            string studioCopy = Path.Combine(bundle, Path.GetFileName(studio));
            string realDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);
            Check("fixture switches reject real profile and real desktop",
                Rejects(studioCopy, "--state-file", StudioStateRepository.CurrentUserPath(), "--desktop-root", desktop) &&
                (string.IsNullOrEmpty(realDesktop) || Rejects(studioCopy, "--state-file", state, "--desktop-root", realDesktop)));

            studioProcess = StartStudio(studioCopy, state, catalog, desktop);
            nint hwnd = Window(studioProcess, "Desktop Motion Studio");
            var home = AutomationElement.FromHandle(hwnd);
            Require(Program.WaitFor(() => Enabled(home, "创建桌面入口")), "Create action did not load.");
            Console.WriteLine($"  Studio PID={studioProcess.Id} HWND=0x{hwnd:X} DPI={GetDpiForWindow(hwnd)}");
            if (Environment.GetEnvironmentVariable("DMS_F10_WAIT_FOR_FOCUS") == "1")
            {
                Console.WriteLine("  Waiting for activation of the owned F10 Studio window.");
                Require(Program.WaitFor(() => GetForegroundWindow() == hwnd, TimeSpan.FromSeconds(45)), "Owned Studio window not activated.");
            }
            Check("reading home makes no shortcut", Directory.GetFiles(desktop, "*.lnk").SequenceEqual([unrelated]));
            Keys(hwnd, 0x12, 0x43); // Alt+C
            Require(Program.WaitFor(() => Enabled(home, "移除桌面入口")), "Shortcut creation was not verified.");
            string link = Directory.GetFiles(desktop, "*.lnk").Single(path => path != unrelated);
            Check("keyboard create is explicit and preserves original link and state",
                File.ReadAllText(unrelated) == "foreign original" && SameState(state, initialBytes, initialBackup));
            File.Delete(link);
            Invoke(home, "刷新应用");
            Require(Program.WaitFor(() => Enabled(home, "修复桌面入口")), "Missing entry did not become repairable.");
            Keys(hwnd, 0x12, 0x45); // Alt+E
            Require(Program.WaitFor(() => Enabled(home, "移除桌面入口") && File.Exists(link)), "Repair did not restore entry.");
            Check("keyboard repair restores only registered path", Directory.GetFiles(desktop, "*.lnk").Length == 2);
            studioProcess.CloseMainWindow();
            Require(studioProcess.WaitForExit(5000), "Studio did not exit.");
            Check("Studio has exited before Shell activation", studioProcess.HasExited);
            studioProcess.Dispose();
            studioProcess = null;

            string hostExe = Path.Combine(bundle, "MotionHost", "DesktopMotionStudio.MotionHost.exe");
            byte[] linkBytes = File.ReadAllBytes(link);
            var first = Activate(link, state, targetCopy);
            Check("actual shortcut opens target and hands off first selected work",
                first is { Scene: "scene-one", Instance: "WindowReady", Host: "HandedOff", Window: not 0 });
            string logPrimary = Path.Combine(logs.DirectoryPath, "records", "events.json");
            if (diagnostics)
            {
                DiagnosticSnapshot firstLog = logs.Read();
                Check("F11 diagnostic target and handoff share one request",
                    firstLog is { Status: DiagnosticStatus.Ready, Events.Count: 2 } &&
                    firstLog.Events[0] is { Stage: DiagnosticStage.Target, Result: DiagnosticResultCode.WindowReady } &&
                    firstLog.Events[1] is { Stage: DiagnosticStage.Animation, Result: DiagnosticResultCode.HandedOff } &&
                    firstLog.Events[0].RequestId == firstLog.Events[1].RequestId);
            }
            Require(store.Update(current => current with { Bindings = [binding with { SceneId = "scene-two" }] }).IsSuccess, "Replacement failed.");
            LaunchTrace replaced;
            if (diagnostics)
            {
                byte[] beforeBusy = File.ReadAllBytes(logPrimary);
                using var busy = File.Open(Path.Combine(logs.DirectoryPath, "locks", "events.lock"),
                    FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                replaced = Activate(link, state, targetCopy);
                Check("F11 busy diagnostic log preserves bytes and handoff",
                    File.ReadAllBytes(logPrimary).SequenceEqual(beforeBusy) && replaced.Host == "HandedOff");
            }
            else replaced = Activate(link, state, targetCopy);
            Check("same shortcut uses replacement work", replaced is { Scene: "scene-two", Host: "HandedOff", Window: not 0 });
            Require(store.Update(current => current with { Bindings = [binding with { Enabled = false }] }).IsSuccess, "Disable failed.");
            var disabled = Activate(link, state, targetCopy);
            Check("disabled motion opens native target without a renderer",
                disabled is { Scene: null, Host: null, Instance: "WindowReady", Window: not 0 });
            if (diagnostics)
                Check("F11 disabled motion is distinct from a target failure",
                    logs.Read().Events.LastOrDefault() is { Stage: DiagnosticStage.Animation, Fallback: DiagnosticFallback.AnimationDisabled });
            Require(store.Update(current => current with { Bindings = [binding] }).IsSuccess, "Re-enable failed.");
            File.Move(hostExe, hostExe + ".missing");
            try
            {
                var failed = Activate(link, state, targetCopy);
                Check("missing renderer fails open to one target",
                    failed is { Scene: "scene-one", Host: "Failed", HostError: "StartFailed", Instance: "WindowReady", Window: not 0 });
            }
            finally { File.Move(hostExe + ".missing", hostExe); }
            if (diagnostics)
            {
                Check("F11 renderer fallback has its own typed result",
                    logs.Read().Events.LastOrDefault() is { Result: DiagnosticResultCode.RendererFailed, Fallback: DiagnosticFallback.RendererUnavailable });
                string originalLog = File.ReadAllText(logPrimary);
                File.WriteAllText(logPrimary, "{\"schemaVersion\":5,\"future\":true}");
                LaunchTrace future = Activate(link, state, targetCopy);
                Check("F11 future log remains byte-exact while target still hands off", future.Host == "HandedOff" &&
                    File.ReadAllText(logPrimary) == "{\"schemaVersion\":5,\"future\":true}");
                File.WriteAllText(logPrimary, originalLog);
                Environment.SetEnvironmentVariable("DMS_F11_FIXTURE_LOG_MODE", "throw", EnvironmentVariableTarget.Process);
                LaunchTrace throwing = Activate(link, state, targetCopy);
                Check("F11 throwing diagnostic sink cannot change a successful dispatch",
                    throwing is { Instance: "WindowReady", Host: "HandedOff" } && File.ReadAllText(logPrimary) == originalLog);
                Environment.SetEnvironmentVariable("DMS_F11_FIXTURE_LOG_MODE", "record", EnvironmentVariableTarget.Process);
                Require(store.Update(current => current with { Bindings = [] }).IsSuccess, "Missing-binding fixture failed.");
                Check("F11 missing binding is diagnosed before dispatch",
                    ShellExit(link) == 3 && OwnedTargets(targetCopy) == 0 &&
                    logs.Read().Events.LastOrDefault() is { Stage: DiagnosticStage.Request, Result: DiagnosticResultCode.BindingMissing });
                Require(store.Update(current => current with { Bindings = [binding] }).IsSuccess, "Binding fixture restore failed.");
                string raw = File.ReadAllText(logPrimary);
                string summary = DiagnosticReport.Format(logs.Read().Events);
                string[] privateValues = [root, binding.BindingId, "scene-one", "Fixture app", "launch-trace"];
                Check("F11 raw diagnostics and share text omit application and fixture data", privateValues.All(value =>
                    !raw.Contains(JsonSerializer.Serialize(value)[1..^1], StringComparison.Ordinal) &&
                    !summary.Contains(value, StringComparison.Ordinal)));
            }
            Check("normal Shell activation leaves shortcut bytes unchanged", File.ReadAllBytes(link).SequenceEqual(linkBytes));

            byte[] beforeRemove = File.ReadAllBytes(state), beforeRemoveBackup = File.ReadAllBytes(state + ".bak");
            studioProcess = StartStudio(studioCopy, state, catalog, desktop);
            hwnd = Window(studioProcess, "Desktop Motion Studio");
            home = AutomationElement.FromHandle(hwnd);
            Require(Program.WaitFor(() => Enabled(home, "移除桌面入口")), "Owned entry was not verified on reopen.");
            Keys(hwnd, 0x12, 0x4D); // Alt+M
            Require(Program.WaitFor(() => Enabled(home, "创建桌面入口") && !File.Exists(link)), "Remove did not clear the owned link.");
            Check("keyboard remove preserves binding, backup and foreign entry",
                SameState(state, beforeRemove, beforeRemoveBackup) && File.ReadAllText(unrelated) == "foreign original");
            File.WriteAllText(link, "foreign collision");
            Invoke(home, "刷新应用");
            Require(Program.WaitFor(() => ShortcutStatus(home).Contains("冲突", StringComparison.Ordinal)), "Collision not shown.");
            Check("same-name collision offers no unsafe mutation",
                !Enabled(home, "创建桌面入口") && !Enabled(home, "修复桌面入口") && !Enabled(home, "移除桌面入口") &&
                File.ReadAllText(link) == "foreign collision");
        }
        catch (Exception ex) { Console.WriteLine($"FAIL F10 UI {ex.GetType().Name}: {ex.Message}"); failures++; }
        finally
        {
            Environment.SetEnvironmentVariable("DMS_F10_FIXTURE_STATE", previous, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("DMS_F11_FIXTURE_LOG_MODE", previousLogMode, EnvironmentVariableTarget.Process);
            if (studioProcess is not null) Stop(studioProcess);
            StopOwnedTargets(targetCopy);
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, parent, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f10-ui-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
        return failures;
    }

    private static int ShellExit(string link)
    {
        using Process process = Process.Start(new ProcessStartInfo(link) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }) ??
            throw new InvalidOperationException("Shell did not start the fixture.");
        try { return process.WaitForExit(8000) ? process.ExitCode : -1; }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
    }

    private static LaunchTrace Activate(string link, string state, string target)
    {
        string tracePath = state + ".launch-trace";
        File.Delete(tracePath);
        byte[] bytes = File.ReadAllBytes(state), backup = File.ReadAllBytes(state + ".bak");
        var start = new ProcessStartInfo(link) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
        using Process launcher = Process.Start(start) ?? throw new InvalidOperationException("Shell did not return the Launcher process.");
        try
        {
            Require(launcher.WaitForExit(10000) && launcher.ExitCode == 0, "Fixture Launcher failed or timed out.");
            var result = JsonSerializer.Deserialize<LaunchTrace>(File.ReadAllText(tracePath)) ??
                throw new IOException("No launch trace.");
            Console.WriteLine($"  Launcher PID={launcher.Id}, target PID={result.TargetPid}, HWND=0x{result.Window:X}, scene={result.Scene}, target={result.Instance}, host={result.Host}/{result.HostError}");
            Require(result.TargetPid is int pid && IsTarget(pid, target) && OwnedTargets(target) == 1,
                "Expected exactly one verified target process.");
            Require(SameState(state, bytes, backup), "Launcher changed state or backup.");
            return result;
        }
        finally
        {
            if (!launcher.HasExited) { launcher.Kill(); launcher.WaitForExit(5000); }
            StopOwnedTargets(target);
        }
    }

    private static Process StartStudio(string executable, string state, string catalog, string desktop)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (string arg in new[] { "--state-file", state, "--catalog-root", catalog, "--desktop-root", desktop })
            start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Studio did not start.");
    }

    private static bool Rejects(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Studio probe did not start.");
        try { return process.WaitForExit(3000) && process.ExitCode == 2; }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string child in Directory.GetDirectories(source)) CopyTree(child, Path.Combine(destination, Path.GetFileName(child)));
    }

    private static string RequiredPath(string variable) => Environment.GetEnvironmentVariable(variable) is string path && File.Exists(path)
        ? Path.GetFullPath(path) : throw new InvalidOperationException($"Set {variable} to the built fixture path.");

    private static bool SameState(string state, byte[] bytes, byte[] backup) =>
        File.ReadAllBytes(state).SequenceEqual(bytes) && File.ReadAllBytes(state + ".bak").SequenceEqual(backup);

    private static bool Enabled(AutomationElement root, string name) => Find(root, name).Current.IsEnabled;
    private static string ShortcutStatus(AutomationElement root) =>
        root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "ShortcutHealth"))?.Current.Name ?? "";
    private static void Invoke(AutomationElement root, string name) =>
        ((InvokePattern)Find(root, name).GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    private static int OwnedTargets(string path)
    {
        int count = 0;
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
            using (process) if (IsTarget(process, path)) count++;
        return count;
    }

    private static bool IsTarget(int pid, string path)
    {
        try { using var process = Process.GetProcessById(pid); return IsTarget(process, path); }
        catch (ArgumentException) { return false; }
    }

    private static bool IsTarget(Process process, string path)
    {
        try
        {
            _ = process.SafeHandle;
            return !process.HasExited && string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static void StopOwnedTargets(string path)
    {
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
            if (IsTarget(process, path)) Stop(process);
            else process.Dispose();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record LaunchTrace(string? Scene, string Instance, int? TargetPid, long Window, string? Host, string? HostError);
}
