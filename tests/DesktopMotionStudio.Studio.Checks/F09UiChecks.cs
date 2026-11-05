using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.State;
using static StudioUiChecks;

internal static class F09UiChecks
{
    internal static int Run()
    {
        string? studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH"), target = Environment.GetEnvironmentVariable("DMS_F06_TARGET_PATH");
        if (studio is null || target is null || !File.Exists(studio) || !File.Exists(target)) { Console.WriteLine("FAIL F09 UI requires built Studio and isolated WindowHost fixture paths"); return 1; }
        string hostPath = Path.Combine(Path.GetDirectoryName(studio)!, "MotionHost", "DesktopMotionStudio.MotionHost.exe");
        if (!File.Exists(hostPath)) { Console.WriteLine("FAIL Studio output must contain its bundled MotionHost"); return 1; }
        string root = Path.Combine(Path.GetTempPath(), $"dms-f09-ui-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        string statePath = Path.Combine(root, "state.json"), catalog = Path.Combine(root, "catalog");
        F09WindowChecks.WriteScene(catalog, "scene-one", "First work");
        string secondScene = F09WindowChecks.WriteScene(catalog, "scene-two", "Second work");
        string[] targets = Enumerable.Range(0, 2).Select(index =>
        {
            string directory = Path.Combine(root, $"target-{index}"); Directory.CreateDirectory(directory);
            foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(target)!)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            return Path.Combine(directory, Path.GetFileName(target));
        }).ToArray();
        var store = new StudioStateRepository(statePath);
        store.Save(StudioState.Empty with { Settings = new("dark", false) }); store.Save(StudioState.Empty);
        int failures = 0, baseline = CountTargets(target);
        Process? process = null;
        var observedHosts = new List<Process>();
        void Check(string name, bool passed) => Program.Record("F09 UI " + name, passed, ref failures);
        try
        {
            process = Start(studio, statePath, catalog);
            nint main = Window(process, "Desktop Motion Studio"); var home = AutomationElement.FromHandle(main);
            Require(Program.WaitFor(() => Find(home, "添加应用").Current.IsEnabled), "Home not ready");
            Console.WriteLine($"  F09 Studio PID={process.Id}, HWND=0x{main:X}, DPI={GetDpiForWindow(main)}, fixture={root}");
            bool preferenceRead = SystemParametersInfo(0x1042, 0, out bool animations, 0);
            Console.WriteLine($"  Windows animation preference: readable={preferenceRead}, enabled={animations}");
            if (Environment.GetEnvironmentVariable("DMS_F09_EXPECT_ANIMATIONS") is string expected)
                Check("actual system motion preference matches acceptance branch", preferenceRead && animations == (expected == "1"));
            if (Environment.GetEnvironmentVariable("DMS_F09_WAIT_FOR_FOCUS") == "1")
            {
                Console.WriteLine("  Waiting for foreground activation of the owned F09 acceptance window");
                Require(Program.WaitFor(() => GetForegroundWindow() == main, TimeSpan.FromSeconds(45)), "Owned F09 window was not activated");
            }
            Keys(main, 0x12, 0x4C);
            nint libraryWindow = Window(process, "动效作品库"); var library = AutomationElement.FromHandle(libraryWindow);
            Require(Program.WaitFor(() => Find(library, "预览作品").Current.IsEnabled), "Library not ready");
            Check("two complete works are keyboard reachable with readable selection", Selection(library, "完整动效作品") == "First work" && ListCount(library, "完整动效作品") == 2);
            Pause("library");
            byte[] original = File.ReadAllBytes(statePath), originalBackup = File.ReadAllBytes(statePath + ".bak");
            Find(library, "预览作品").SetFocus(); Keys(libraryWindow, 0x20);
            using (Process host = Host(hostPath))
            {
                nint window = Window(host, "Desktop Motion Studio — Preview");
                Console.WriteLine($"  F09 preview natural PID={host.Id}, HWND=0x{window:X}, DPI={GetDpiForWindow(window)}");
                Check("preview is a real target-free simulated handoff window", Find(AutomationElement.FromHandle(window), "预览模拟交接") is not null);
                Require(host.WaitForExit(5000), "Natural preview did not exit");
            }
            Check("preview with no registered app preserves profile backup and target count", store.Load().State!.Applications.Count == 0 && File.ReadAllBytes(statePath).SequenceEqual(original) && File.ReadAllBytes(statePath + ".bak").SequenceEqual(originalBackup) && CountTargets(target) == baseline);
            Invoke(library, "绑定到应用");
            nint pickerWindow = Window(process, "绑定动效到应用"); var picker = AutomationElement.FromHandle(pickerWindow);
            Require(Program.WaitFor(() => ListCount(picker, "可选择的已保存应用") == 0 && !Find(picker, "确认绑定").Current.IsEnabled), "Empty picker was not safe");
            Keys(pickerWindow, 0x1B);
            Check("empty target picker cancels without state writes", File.ReadAllBytes(statePath).SequenceEqual(original));
            Keys(libraryWindow, 0x12, 0x48); Require(Program.WaitFor(() => FindWindow(process.Id, "动效作品库") == 0), "Library did not return home");
            Stop(process); process = null;
            var apps = targets.Select((path, index) => new ApplicationProfile("app-" + index, index == 0 ? "First app" : "Second app", path, new(ApplicationTargetKind.Executable, path, [], Path.GetDirectoryName(path)))).ToArray();
            Require(store.Update(current => current with { Applications = apps }).IsSuccess, "App fixture save failed");
            process = Start(studio, statePath, catalog); main = Window(process, "Desktop Motion Studio"); home = AutomationElement.FromHandle(main);
            Require(Program.WaitFor(() => Find(home, "替换绑定作品").Current.IsEnabled), "Saved apps not ready");
            Invoke(home, "打开动效作品库"); libraryWindow = Window(process, "动效作品库"); library = AutomationElement.FromHandle(libraryWindow);
            Require(Program.WaitFor(() => Find(library, "绑定到应用").Current.IsEnabled), "Library binding action not ready");
            foreach (string app in new[] { "First app", "Second app" })
            {
                Invoke(library, "绑定到应用"); pickerWindow = Window(process, "绑定动效到应用"); picker = AutomationElement.FromHandle(pickerWindow);
                Require(Program.WaitFor(() => ListCount(picker, "可选择的已保存应用") == 2), "Picker did not load saved apps");
                Select(picker, "可选择的已保存应用", app); Keys(pickerWindow, 0x12, 0x53);
                Require(Program.WaitFor(() => FindWindow(process.Id, "绑定动效到应用") == 0), "Binding confirmation did not close");
            }
            StudioState bound = store.Load().State!; string bindingId = bound.Bindings.Single(binding => binding.AppId == "app-0").BindingId;
            Check("one work binds to two apps with distinct stable binding IDs and no dispatch", bound.Bindings.Count == 2 && bound.Bindings.All(binding => binding.SceneId == "scene-one") && bound.Bindings.Select(binding => binding.BindingId).Distinct().Count() == 2 && CountTargets(target) == baseline);
            Keys(libraryWindow, 0x12, 0x48);
            Require(Program.WaitFor(() => FindWindow(process.Id, "动效作品库") == 0), "Library did not close");
            Select(home, "已添加应用", "First app"); Invoke(home, "替换绑定作品");
            libraryWindow = Window(process, "动效作品库"); library = AutomationElement.FromHandle(libraryWindow);
            Require(Program.WaitFor(() => Selection(library, "完整动效作品") == "First work"), "Replace did not select current work");
            Select(library, "完整动效作品", "Second work"); Invoke(library, "绑定到应用");
            pickerWindow = Window(process, "绑定动效到应用"); picker = AutomationElement.FromHandle(pickerWindow);
            Require(Program.WaitFor(() => Selection(picker, "可选择的已保存应用") == "First app"), "Replace did not select intended app");
            Keys(pickerWindow, 0x12, 0x53); Require(Program.WaitFor(() => FindWindow(process.Id, "绑定动效到应用") == 0), "Replacement did not finish");
            StudioState replaced = store.Load().State!;
            bool retainedBinding = replaced.Bindings.Single(binding => binding.AppId == "app-0") == new ApplicationBinding(bindingId, "app-0", "scene-two", true);
            bool retainedOther = replaced.Bindings.Single(binding => binding.AppId == "app-1") == bound.Bindings.Single(binding => binding.AppId == "app-1");
            bool retainedApps = System.Text.Json.JsonSerializer.Serialize(replaced.Applications) == System.Text.Json.JsonSerializer.Serialize(bound.Applications);
            Console.WriteLine($"  replacement preservation: selectedBinding={retainedBinding}, otherBinding={retainedOther}, applicationData={retainedApps}, settings={replaced.Settings == bound.Settings}");
            Check("replacement retains app and binding IDs and unrelated app", retainedBinding && retainedOther && retainedApps && replaced.Settings == bound.Settings);
            original = File.ReadAllBytes(statePath); originalBackup = File.ReadAllBytes(statePath + ".bak");
            Select(library, "完整动效作品", "First work"); Invoke(library, "绑定到应用"); pickerWindow = Window(process, "绑定动效到应用"); Keys(pickerWindow, 0x1B);
            Keys(libraryWindow, 0x12, 0x48); Require(Program.WaitFor(() => FindWindow(process.Id, "动效作品库") == 0), "Library cancel return failed");
            Check("cancelled replacement preserves exact state and backup", File.ReadAllBytes(statePath).SequenceEqual(original) && File.ReadAllBytes(statePath + ".bak").SequenceEqual(originalBackup));
            Require(Program.WaitFor(() => Find(home, "预览当前绑定作品").Current.IsEnabled), "Home preview not ready");
            Keys(main, 0x12, 0x50);
            using (Process host = Host(hostPath))
            {
                nint window = Window(host, "Desktop Motion Studio — Preview"); var elapsed = Stopwatch.StartNew(); Keys(window, 0x1B);
                Check("home bound preview supports Esc within500ms without state writes", host.WaitForExit(500) && elapsed.ElapsedMilliseconds <= 500 && File.ReadAllBytes(statePath).SequenceEqual(original));
            }
            Invoke(home, "预览当前绑定作品");
            using (Process host = Host(hostPath))
            {
                _ = Window(host, "Desktop Motion Studio — Preview");
                Require(Program.WaitFor(() => Find(home, "停止首页预览").Current.IsEnabled), "Home Stop was not enabled");
                Invoke(home, "停止首页预览");
                Check("home Stop action closes preview and preserves state", host.WaitForExit(2000) && File.ReadAllBytes(statePath).SequenceEqual(original));
            }
            File.Delete(Path.Combine(secondScene, "scene.playback.json")); Invoke(home, "刷新应用");
            Require(Program.WaitFor(() => !Find(home, "预览当前绑定作品").Current.IsEnabled), "Removed scene remained previewable");
            Check("removed playback leaves unavailable binding intact", store.Load().State!.Bindings.Single(binding => binding.AppId == "app-0").BindingId == bindingId && File.ReadAllBytes(statePath).SequenceEqual(original));
            Invoke(home, "替换绑定作品"); libraryWindow = Window(process, "动效作品库"); library = AutomationElement.FromHandle(libraryWindow);
            Require(Program.WaitFor(() => Selection(library, "完整动效作品") == "Second work"), "Unavailable work identity not retained");
            Check("unavailable selected work disables preview and bind", !Find(library, "预览作品").Current.IsEnabled && !Find(library, "绑定到应用").Current.IsEnabled);
            Select(library, "完整动效作品", "First work"); Invoke(library, "预览作品");
            using (Process host = Host(hostPath))
            {
                _ = Window(host, "Desktop Motion Studio — Preview"); Keys(libraryWindow, 0x12, 0x48);
                Check("returning home closes active library preview", host.WaitForExit(2000));
            }
            Require(Program.WaitFor(() => FindWindow(process.Id, "动效作品库") == 0), "Home not restored after preview");
            File.WriteAllText(Path.Combine(secondScene, "scene.playback.json"), F09WindowChecks.PlaybackJson("scene-two")); Invoke(home, "刷新应用");
            Require(Program.WaitFor(() => Find(home, "预览当前绑定作品").Current.IsEnabled), "Restored playback not available");
            Invoke(home, "预览当前绑定作品");
            using (Process host = Host(hostPath))
            {
                _ = Window(host, "Desktop Motion Studio — Preview"); process.CloseMainWindow();
                Check("Studio close waits for owned preview cleanup", process.WaitForExit(3000) && host.WaitForExit(1000));
            }
            process.Dispose(); process = Start(studio, statePath, catalog); main = Window(process, "Desktop Motion Studio"); home = AutomationElement.FromHandle(main);
            Require(Program.WaitFor(() => Find(home, "预览当前绑定作品").Current.IsEnabled), "Restart after close failed");
            Invoke(home, "预览当前绑定作品");
            using (Process host = Host(hostPath))
            {
                _ = Window(host, "Desktop Motion Studio — Preview"); var elapsed = Stopwatch.StartNew(); process.Kill();
                Check("abrupt Studio exit kills only its preview within1s", process.WaitForExit(1000) && host.WaitForExit(1000) && elapsed.ElapsedMilliseconds <= 1000 && CountTargets(target) == baseline);
            }
            Check("all browse preview cancel close and crash paths preserve profile and backup", File.ReadAllBytes(statePath).SequenceEqual(original) && File.ReadAllBytes(statePath + ".bak").SequenceEqual(originalBackup));
        }
        catch (Exception ex) { Console.WriteLine($"FAIL F09 external UI: {ex.GetType().Name}: {ex.Message}"); failures++; }
        finally
        {
            if (process is not null) Stop(process);
            foreach (Process host in observedHosts) host.Dispose();
            Directory.Delete(root, true);
        }
        return failures == 0 ? 0 : 1;

        Process Host(string executable)
        {
            Process? found = null;
            Require(Program.WaitFor(() =>
            {
                foreach (Process candidate in Process.GetProcessesByName("DesktopMotionStudio.MotionHost"))
                {
                    try { if (candidate.MainModule?.FileName == executable) { found = candidate; observedHosts.Add(candidate); return true; } }
                    catch (Exception) { }
                    candidate.Dispose();
                }
                return false;
            }), "Bundled preview host did not start");
            return found!;
        }
    }

    private static void Invoke(AutomationElement root, string name) => ((InvokePattern)Find(root, name).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    private static int ListCount(AutomationElement root, string name) => Find(root, name).FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Count;
    private static string? Selection(AutomationElement root, string name) => ((SelectionPattern)Find(root, name).GetCurrentPattern(SelectionPattern.Pattern)).Current.GetSelection().FirstOrDefault()?.Current.Name;
    private static void Select(AutomationElement root, string list, string name)
    {
        AutomationElement item = Find(root, list).FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty, name)) ?? throw new InvalidOperationException($"List item absent: {name}");
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] out bool value, uint flags);
    private static void Pause(string step) { if (Environment.GetEnvironmentVariable("DMS_F09_PAUSE") == step) { Console.WriteLine($"  PAUSE {step}: owned UI ready for inspection"); Thread.Sleep(45000); } }
}
