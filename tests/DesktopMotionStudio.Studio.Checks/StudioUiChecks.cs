using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Studio;

internal static class StudioUiChecks
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(8);

    internal static int Run()
    {
        string? studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH");
        string? poc = Environment.GetEnvironmentVariable("DMS_F06_TARGET_PATH");
        if (string.IsNullOrWhiteSpace(studio) || !File.Exists(studio) || string.IsNullOrWhiteSpace(poc) || !File.Exists(poc))
        {
            Console.WriteLine("FAIL set DMS_F08_STUDIO_PATH and DMS_F06_TARGET_PATH for isolated UI acceptance.");
            return 1;
        }
        string root = Path.Combine(Path.GetTempPath(), $"dms-studio-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string state = Path.Combine(root, "state.json");
        string catalog = Path.Combine(root, "catalog");
        Directory.CreateDirectory(catalog);
        string sceneDirectory = Path.Combine(catalog, "one");
        Directory.CreateDirectory(sceneDirectory);
        string manifestPath = Path.Combine(sceneDirectory, "scene.json");
        const string manifest = """{"schemaVersion":1,"sceneId":"scene-one","sceneVersion":"1.0.0","name":"Fixture","displayMode":"centered","assets":[]}""";
        File.WriteAllText(manifestPath, manifest);
        string[] targets = Enumerable.Range(0, 2).Select(index =>
        {
            string directory = Path.Combine(root, $"target-{index}");
            Directory.CreateDirectory(directory);
            foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(poc)!)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            return Path.Combine(directory, Path.GetFileName(poc));
        }).ToArray();
        Process? process = null;
        int failures = 0;
        try
        {
            int targetBaseline = CountTargets(poc);
            var store = new StudioStateRepository(state);
            process = Start(studio, state, catalog);
            nint main = Window(process, "Desktop Motion Studio");
            AutomationElement mainElement = AutomationElement.FromHandle(main);
            Require(Program.WaitFor(() => Find(mainElement, "添加应用").Current.IsEnabled, ReadyTimeout), "Home did not become ready.");
            Console.WriteLine($"  Studio PID {process.Id}, HWND 0x{main.ToInt64():X}, DPI {GetDpiForWindow(main)}");
            if (Environment.GetEnvironmentVariable("DMS_F08_WAIT_FOR_FOCUS") == "1")
            {
                Console.WriteLine("  Waiting for explicit foreground activation of the owned acceptance window.");
                Require(Program.WaitFor(() => GetForegroundWindow() == main, TimeSpan.FromSeconds(30)), "Acceptance window was not activated.");
            }
            Keys(main, 0x12, 0x41);
            nint editor = Window(process, "添加应用");
            AutomationElement dialog = AutomationElement.FromHandle(editor);
            Value(dialog, "显示名称", "F08 UI fixture");
            Value(dialog, "应用路径", targets[0]);
            Keys(editor, 0x12, 0x53);
            Require(Program.WaitFor(() => store.Load().State?.Applications.Count == 1, ReadyTimeout), "Keyboard add did not persist.");
            ApplicationProfile profile = store.Load().State!.Applications.Single();
            Program.Record("keyboard manual add persists typed target without launching it",
                profile.Target?.Identifier == targets[0] && CountTargets(poc) == targetBaseline, ref failures);
            Stop(process);
            Require(store.Update(current => current with { Bindings = [new("ui-binding", profile.AppId, "scene-one", true)] }).IsSuccess, "Binding fixture was not saved.");
            process = Start(studio, state, catalog);
            main = Window(process, "Desktop Motion Studio"); mainElement = AutomationElement.FromHandle(main);
            Require(Program.WaitFor(() => ReadValue(mainElement, "应用显示名称") == "F08 UI fixture", ReadyTimeout), "Saved application did not return after restart.");
            Program.Record("Studio restart restores saved application details", true, ref failures);
            var selected = ((SelectionPattern)Find(mainElement, "已添加应用").GetCurrentPattern(SelectionPattern.Pattern)).Current.GetSelection();
            Program.Record("saved list exposes readable application name to assistive technology", selected.Length == 1 && selected[0].Current.Name == "F08 UI fixture", ref failures);
            Find(mainElement, "切换动效启用状态").SetFocus(); Keys(main, 0x20);
            Require(Program.WaitFor(() => store.Load().State?.Bindings.Single().Enabled == false), "Binding did not disable.");
            File.Delete(manifestPath);
            ((InvokePattern)Find(mainElement, "刷新应用").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Require(Program.WaitFor(() => Find(mainElement, "切换动效启用状态").Current.IsEnabled), "Binding view did not become ready.");
            Find(mainElement, "切换动效启用状态").SetFocus(); Keys(main, 0x20);
            Require(Program.WaitFor(() => Status(mainElement).Contains("作品暂不可用", StringComparison.Ordinal)), "Unavailable binding rejection was absent.");
            Program.Record("keyboard binding disable and unavailable-scene enable rejection preserve identity",
                store.Load().State!.Bindings.Single() is { BindingId: "ui-binding", Enabled: false }, ref failures);
            File.WriteAllText(manifestPath, manifest);
            ((InvokePattern)Find(mainElement, "刷新应用").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Require(Program.WaitFor(() => Find(mainElement, "切换动效启用状态").Current.IsEnabled), "Binding view did not become ready.");
            Find(mainElement, "切换动效启用状态").SetFocus(); Keys(main, 0x20);
            Require(Program.WaitFor(() => store.Load().State?.Bindings.Single().Enabled == true), "Binding did not re-enable.");
            Program.Record("keyboard binding re-enable agrees with persistent F07 state", true, ref failures);
            Value(mainElement, "应用显示名称", "F08 renamed"); Keys(main, 0x12, 0x53);
            Require(Program.WaitFor(() => store.Load().State?.Applications.Single().DisplayName == "F08 renamed"), "Rename did not persist.");
            Keys(main, 0x12, 0x52);
            editor = Window(process, "确认启动入口"); dialog = AutomationElement.FromHandle(editor);
            Value(dialog, "应用路径", targets[1]); Keys(editor, 0x12, 0x53);
            Require(Program.WaitFor(() => store.Load().State?.Applications.Single().Target?.Identifier == targets[1]), "Repair did not persist.");
            Program.Record("keyboard rename and repair retain stable app and binding IDs", store.Load().State!.Applications.Single().AppId == profile.AppId && store.Load().State!.Bindings.Single().BindingId == "ui-binding", ref failures);
            byte[] beforeCancel = File.ReadAllBytes(state);
            Keys(main, 0x12, 0x41); editor = Window(process, "添加应用"); Keys(editor, 0x1B);
            Require(Program.WaitFor(() => FindWindow(process.Id, "添加应用") == 0), "Add dialog did not cancel.");
            Program.Record("keyboard cancel leaves state bytes unchanged", File.ReadAllBytes(state).SequenceEqual(beforeCancel), ref failures);
            Value(mainElement, "搜索已添加应用", "no matching fixture");
            Require(Program.WaitFor(() => ((SelectionPattern)Find(mainElement, "已添加应用").GetCurrentPattern(SelectionPattern.Pattern)).Current.GetSelection().Length == 0), "Search did not filter saved list.");
            Value(mainElement, "搜索已添加应用", string.Empty);
            Require(Program.WaitFor(() => Find(mainElement, "移除应用配置").Current.IsEnabled), "Selection not restored.");
            Keys(main, 0x12, 0x44);
            nint confirmation = Window(process, "移除应用配置");
            InvokeById(AutomationElement.FromHandle(confirmation), "2");
            Program.Record("cancelled removal preserves the registration", store.Load().State!.Applications.Count == 1, ref failures);
            Keys(main, 0x12, 0x44); confirmation = Window(process, "移除应用配置");
            InvokeById(AutomationElement.FromHandle(confirmation), "1");
            Require(Program.WaitFor(() => store.Load().State?.Applications.Count == 0), "Confirmed removal did not persist.");
            Program.Record("keyboard confirmed removal atomically removes application and binding", store.Load().State!.Bindings.Count == 0, ref failures);

            var discovery = new DiscoveryClient(() =>
            {
                var start = new ProcessStartInfo(studio);
                start.ArgumentList.Add("--discovery-worker");
                return start;
            }).ScanAsync(CancellationToken.None).GetAwaiter().GetResult();
            ApplicationCandidate packaged = discovery.Candidates.First(candidate => candidate.Target.Kind == ApplicationTargetKind.Packaged);
            Keys(main, 0x12, 0x41); editor = Window(process, "添加应用"); dialog = AutomationElement.FromHandle(editor);
            ((SelectionItemPattern)Find(dialog, "已安装应用").GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Value(dialog, "搜索已安装应用", packaged.DisplayName);
            Require(Program.WaitFor(() => dialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, packaged.Target.Identifier)) is not null, ReadyTimeout), "Packaged discovery candidate was absent from the UI.");
            AutomationElement item = Find(dialog, packaged.Target.Identifier);
            while (item.Current.ControlType != ControlType.ListItem) item = TreeWalker.ControlViewWalker.GetParent(item) ?? throw new InvalidOperationException("Candidate list item absent.");
            ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Program.Record("discovery list exposes readable candidate name to assistive technology", item.Current.Name == packaged.DisplayName, ref failures);
            Value(dialog, "显示名称", "F08 packaged fixture"); Keys(editor, 0x12, 0x53);
            Require(Program.WaitFor(() => store.Load().State?.Applications.Count == 1), "Packaged add did not persist.");
            Program.Record("discovered packaged UI selection saves explicit AUMID and type",
                store.Load().State!.Applications.Single().Target is { Kind: ApplicationTargetKind.Packaged } saved && saved.Identifier == packaged.Target.Identifier, ref failures);
            Require(Program.WaitFor(() => Find(mainElement, "移除应用配置").Current.IsEnabled), "Packaged details not ready.");
            Keys(main, 0x12, 0x44); confirmation = Window(process, "移除应用配置"); InvokeById(AutomationElement.FromHandle(confirmation), "1");
            Require(Program.WaitFor(() => store.Load().State?.Applications.Count == 0), "Packaged fixture removal did not persist.");
            Program.Record("management acceptance never launches either target", CountTargets(poc) == targetBaseline, ref failures);
            Stop(process);
            string invalid = Path.Combine(root, "directory-state"); Directory.CreateDirectory(invalid);
            process = Start(studio, invalid, catalog); main = Window(process, "Desktop Motion Studio"); mainElement = AutomationElement.FromHandle(main);
            Require(Program.WaitFor(() => !Find(mainElement, "添加应用").Current.IsEnabled), "Invalid configuration did not disable mutation.");
            Require(Program.WaitFor(() => mainElement.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "OperationStatus"))?.Current.Name.Contains("无法", StringComparison.Ordinal) == true), "Storage recovery message was absent.");
            Program.Record("unreadable configuration leaves a readable recovery home", !process.HasExited, ref failures);
            Stop(process);
            string legacyPath = Path.Combine(root, "legacy.json");
            byte[] legacy = Encoding.UTF8.GetBytes("""{"schemaVersion":1,"applications":[{"appId":"legacy-app","displayName":"Legacy fixture","launchTarget":"old launcher --untyped"}],"bindings":[{"bindingId":"legacy-binding","appId":"legacy-app","sceneId":"scene-one","enabled":true}],"settings":{"theme":"system","muted":false}}""");
            File.WriteAllBytes(legacyPath, legacy);
            var legacyStore = new StudioStateRepository(legacyPath);
            process = Start(studio, legacyPath, catalog); main = Window(process, "Desktop Motion Studio"); mainElement = AutomationElement.FromHandle(main);
            Require(Program.WaitFor(() => ReadValue(mainElement, "应用显示名称") == "Legacy fixture"), "Legacy details absent.");
            Program.Record("legacy home read preserves exact original bytes", File.ReadAllBytes(legacyPath).SequenceEqual(legacy), ref failures);
            Keys(main, 0x12, 0x52); editor = Window(process, "确认启动入口"); dialog = AutomationElement.FromHandle(editor);
            Program.Record("legacy repair requires explicit path rather than guessing raw text", ReadValue(dialog, "应用路径") == string.Empty, ref failures);
            Value(dialog, "应用路径", targets[0]); Keys(editor, 0x12, 0x53);
            Require(Program.WaitFor(() => legacyStore.Load().State?.Applications.Single().Target is not null), "Legacy repair did not persist.");
            StudioState repaired = legacyStore.Load().State!;
            Program.Record("legacy UI repair preserves raw text and IDs with exact v1 backup",
                repaired.Applications.Single() is { AppId: "legacy-app", LaunchTarget: "old launcher --untyped" } &&
                repaired.Bindings.Single().BindingId == "legacy-binding" && File.ReadAllBytes(legacyPath + ".bak").SequenceEqual(legacy), ref failures);
        }
        catch (Exception ex) { Console.WriteLine($"FAIL Studio UI acceptance: {ex.GetType().Name}: {ex.Message}"); failures++; }
        finally
        {
            if (process is not null) Stop(process);
            foreach (Process target in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(poc)))
            {
                try { if (target.MainModule?.FileName is string path && path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { target.Kill(); target.WaitForExit(2_000); } }
                catch (Exception) { }
                finally { target.Dispose(); }
            }
            Directory.Delete(root, recursive: true);
        }
        return failures == 0 ? 0 : 1;
    }

    private static Process Start(string executable, string state, string catalog)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (string arg in new[] { "--state-file", state, "--catalog-root", catalog }) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Studio did not start.");
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(2_000)) { process.Kill(); process.WaitForExit(2_000); } }
        }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }

    private static nint Window(Process process, string title)
    {
        nint window = 0;
        Require(Program.WaitFor(() => { window = FindWindow(process.Id, title); return window != 0; }, ReadyTimeout), $"Window not found: {title}");
        return window;
    }

    private static nint FindWindow(int pid, string title)
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            var text = new StringBuilder(256); GetWindowText(window, text, text.Capacity);
            if (owner == (uint)pid && IsWindowVisible(window) && text.ToString() == title) { found = window; return false; }
            return true;
        }, 0);
        return found;
    }

    private static AutomationElement Find(AutomationElement root, string name) =>
        root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name))
        ?? throw new InvalidOperationException($"Control not found: {name}");
    private static void Value(AutomationElement root, string name, string value) => ((ValuePattern)Find(root, name).GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
    private static string ReadValue(AutomationElement root, string name) => ((ValuePattern)Find(root, name).GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
    private static string Status(AutomationElement root) => root.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.AutomationIdProperty, "OperationStatus"))?.Current.Name ?? string.Empty;
    private static void InvokeById(AutomationElement root, string id) => ((InvokePattern)(root.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.AutomationIdProperty, id)) ?? throw new InvalidOperationException("Confirmation button not found.")).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    private static int CountTargets(string path) { Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)); int count = processes.Length; foreach (Process process in processes) process.Dispose(); return count; }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void Keys(nint window, params ushort[] keys)
    {
        SetForegroundWindow(window);
        Require(Program.WaitFor(() => GetForegroundWindow() == window, TimeSpan.FromMilliseconds(500)), "Could not focus the owned test window.");
        var inputs = keys.Select(key => new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key } } })
            .Concat(keys.Reverse().Select(key => new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = 2 } } })).ToArray();
        Require(SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length, "Keyboard input was not injected.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nint Extra; }
    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int size);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
