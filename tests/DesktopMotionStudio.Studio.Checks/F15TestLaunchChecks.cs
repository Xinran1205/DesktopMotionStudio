using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Studio;

internal static class F15TestLaunchChecks
{
    internal static void Run(ref int failures)
    {
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f15-studio-");
        var results = new List<(string Name, bool Passed)>();
        try { F09WindowChecks.Pump(() => RunAsync(fixture.FullName, results)); }
        finally
        {
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, temp, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f15-studio-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
        foreach (var result in results) Program.Record("F15 " + result.Name, result.Passed, ref failures);
    }

    private static async Task RunAsync(string root, List<(string, bool)> results)
    {
        string scenes = Path.Combine(root, "Scenes");
        F09WindowChecks.WriteScene(scenes, "scene-one", "Test work");
        string target = Path.Combine(Environment.SystemDirectory, "where.exe");
        string unavailable = Path.Combine(root, "absent.exe");
        string profile = Path.Combine(root, "state.json");
        var store = new StudioStateRepository(profile);
        var state = StudioState.Empty with
        {
            Applications =
            [
                Saved("a", "A ready", target), Saved("b", "B disabled", target),
                Saved("c", "C unbound", target), Saved("d", "D missing", unavailable)
            ],
            Bindings = [new("测试 binding", "a", "scene-one", true), new("disabled", "b", "scene-one", false),
                new("missing", "d", "scene-one", false)]
        };
        if (store.Save(state) != RepositoryError.None || store.Save(state) != RepositoryError.None)
            throw new IOException("Could not save fixture state and backup.");
        byte[] before = File.ReadAllBytes(profile), backup = File.ReadAllBytes(profile + ".bak");
        string desktop = Directory.CreateDirectory(Path.Combine(root, "Desktop")).FullName;
        string receipts = Path.Combine(root, "managed-shortcuts");
        var shortcuts = new ManagedShortcutService(store, scenes, receipts, target, () => desktop);
        int starts = 0;
        ProcessStartInfo? captured = null;
        CancellationToken observedToken = default;
        TaskCompletionSource<int> reply = NewReply();
        var client = new TestLaunchClient(target, (start, token) =>
        {
            starts++;
            captured = start;
            observedToken = token;
            return reply.Task;
        });

        var window = new MainWindow(store, scenes, shortcuts: shortcuts, testLaunch: client);
        var action = (Button)window.FindName("TestLaunchButton");
        var message = (TextBlock)window.FindName("TestLaunchStatusText");
        var list = (ListBox)window.FindName("ApplicationList");
        var choose = (Button)window.FindName("ReplaceButton");
        var add = (Button)window.FindName("AddButton");
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            await window.RefreshAsync("a");
            results.Add(("exposes the home action, keyboard mnemonic and status",
                action.IsEnabled && Equals(action.Content, "测试启动(_T)")));
            results.Add(("F16 ready home emphasizes Test Launch alone",
                action.Tag is true && choose.Tag is not true && add.Tag is not true));
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            results.Add(("dispatches exactly one canonical request while waiting",
                starts == 1 && !action.IsEnabled && captured is not null && !captured.UseShellExecute &&
                captured.FileName == target && captured.WorkingDirectory == Path.GetDirectoryName(target) &&
                captured.ArgumentList.SequenceEqual([TestLaunchProtocol.Command, ManagedLaunchRequest.EncodeBindingId("测试 binding")!])));

            list.SelectedIndex = 1;
            reply.SetResult((int)TestLaunchOutcome.Opened);
            await UntilAsync(() => action.IsEnabled);
            results.Add(("older completion leaves the new selection's status alone", message.Text.Length == 0));

            reply = NewReply();
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            reply.SetResult((int)TestLaunchOutcome.OpenedWithoutMotion);
            await UntilAsync(() => action.IsEnabled);
            results.Add(("disabled motion still allows an explicitly native test", starts == 2 &&
                message.Text.Contains("未播放动效", StringComparison.Ordinal)));

            list.SelectedIndex = 2;
            bool unbound = !action.IsEnabled && message.Text.Contains("绑定", StringComparison.Ordinal);
            results.Add(("F16 unbound selection emphasizes Choose Motion alone",
                choose.Tag is true && action.Tag is not true && add.Tag is not true));
            list.SelectedIndex = 3;
            results.Add(("unbound or missing targets have actionable status", unbound && !action.IsEnabled &&
                message.Text.Contains("确认启动入口", StringComparison.Ordinal)));

            list.SelectedIndex = 0;
            reply = NewReply();
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Close();
            await UntilAsync(() => closed && observedToken.IsCancellationRequested);
            reply.SetResult((int)TestLaunchOutcome.Opened);
            results.Add(("closing the home cancels the wait for its one dispatched request", starts == 3));
            results.Add(("leaves profile, backup, Desktop and receipts unchanged",
                File.ReadAllBytes(profile).SequenceEqual(before) && File.ReadAllBytes(profile + ".bak").SequenceEqual(backup) &&
                !Directory.EnumerateFileSystemEntries(desktop).Any() && !Directory.Exists(receipts)));
        }
        finally
        {
            if (!closed) window.Close();
            await UntilAsync(() => closed);
        }

        var empty = new MainWindow(new StudioStateRepository(Path.Combine(root, "empty.json")), scenes, testLaunch: client);
        try
        {
            await empty.RefreshAsync();
            results.Add(("F16 empty home emphasizes Add alone",
                ((Button)empty.FindName("AddButton")).Tag is true &&
                ((Button)empty.FindName("ReplaceButton")).Tag is not true &&
                ((Button)empty.FindName("TestLaunchButton")).Tag is not true));
        }
        finally { empty.Close(); }

        var missing = new TestLaunchClient(unavailable);
        results.Add(("reports a missing component", (await missing.LaunchAsync("valid", default)).Status == TestLaunchClientStatus.ComponentUnavailable));
        var failed = new TestLaunchClient(target, (_, _) => throw new Win32Exception(5));
        results.Add(("reports a process creation failure", (await failed.LaunchAsync("valid", default)).Status == TestLaunchClientStatus.ComponentUnavailable));
        var never = NewReply();
        var timed = new TestLaunchClient(target, (_, _) => never.Task, TimeSpan.FromMilliseconds(80));
        results.Add(("bounds the wait without completing or killing the underlying request",
            (await timed.LaunchAsync("valid", default)).Status == TestLaunchClientStatus.TimedOut && !never.Task.IsCompleted));
        never.SetResult((int)TestLaunchOutcome.Opened);
        var unknown = new TestLaunchClient(target, (_, _) => Task.FromResult(250));
        results.Add(("classifies an unexpected launcher exit as failure",
            (await unknown.LaunchAsync("valid", default)).Outcome == TestLaunchOutcome.Failed));
    }

    private static ApplicationProfile Saved(string id, string name, string target) =>
        new(id, name, target, new(ApplicationTargetKind.Executable, target, []));

    private static TaskCompletionSource<int> NewReply() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task UntilAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!ready()) await Task.Delay(15, timeout.Token);
    }
}
