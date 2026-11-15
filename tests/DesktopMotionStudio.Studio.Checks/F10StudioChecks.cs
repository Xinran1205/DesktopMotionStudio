using System.IO;
using System.Windows;
using System.Windows.Controls;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Studio;

internal static class F10StudioChecks
{
    internal static void Run(ref int failures)
    {
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f10-studio-");
        var results = new List<(string Name, bool Pass)>();
        try { F09WindowChecks.Pump(() => RunCases(fixture.FullName, results)); }
        finally
        {
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, parent, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f10-studio-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
        foreach (var result in results) Program.Record("F10 Studio " + result.Name, result.Pass, ref failures);
    }

    private static async Task RunCases(string root, List<(string, bool)> results)
    {
        string desktop = Directory.CreateDirectory(Path.Combine(root, "Desktop")).FullName;
        string scenes = Path.Combine(root, "Scenes");
        F09WindowChecks.WriteScene(scenes, "scene-one", "Work one");
        string target = Path.Combine(Environment.SystemDirectory, "where.exe");
        string profile = Path.Combine(root, "state.json");
        var store = new StudioStateRepository(profile);
        var a = new ApplicationProfile("a", "A fixture", target, new(ApplicationTargetKind.Executable, target, []));
        var b = a with { AppId = "b", DisplayName = "B fixture" };
        StudioState state = StudioState.Empty with
        {
            Applications = [a, b],
            Bindings = [new("one", "a", "scene-one", true), new("two", "b", "scene-one", true)]
        };
        if (store.Save(state) != RepositoryError.None || store.Save(state) != RepositoryError.None)
            throw new IOException("Fixture profile write failed.");
        byte[] profileBytes = File.ReadAllBytes(profile), backup = File.ReadAllBytes(profile + ".bak");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var staleEntered = new ManualResetEventSlim();
        using var staleRelease = new ManualResetEventSlim();
        int gate = 0;
        int staleGate = 0;
        var service = new ManagedShortcutService(store, scenes, Path.Combine(root, "receipts"), target, () =>
        {
            if (Volatile.Read(ref gate) == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Desktop fixture gate timed out.");
            }
            if (Interlocked.Exchange(ref staleGate, 0) == 1)
            {
                staleEntered.Set();
                if (!staleRelease.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Stale status fixture gate timed out.");
            }
            return desktop;
        });
        var main = new MainWindow(store, scenes, shortcuts: service);
        var list = (ListBox)main.FindName("ApplicationList");
        var create = (Button)main.FindName("ShortcutCreateButton");
        var repair = (Button)main.FindName("ShortcutRepairButton");
        var remove = (Button)main.FindName("ShortcutRemoveButton");
        var notice = (TextBlock)main.FindName("ShortcutActionText");
        var health = (TextBlock)main.FindName("ShortcutHealthText");
        try
        {
            Volatile.Write(ref gate, 1);
            await main.RefreshAsync("a");
            await Until(() => entered.IsSet);
            Volatile.Write(ref gate, 0);
            list.SelectedIndex = 1;
            await Until(() => create.IsEnabled);
            release.Set();
            await Task.Delay(120);
            results.Add(("stale status does not replace newer selected app", list.SelectedIndex == 1 &&
                create.IsEnabled && !remove.IsEnabled && !Directory.GetFiles(desktop, "*.lnk").Any()));

            string foreign = service.Inspect("two").Path!;
            File.WriteAllText(foreign, "foreign fixture");
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => notice.Text.Contains("冲突", StringComparison.Ordinal));
            results.Add(("explicit conflict is visible and foreign bytes survive",
                File.ReadAllText(foreign) == "foreign fixture" && !repair.IsEnabled && !remove.IsEnabled));

            list.SelectedIndex = 0;
            await Until(() => create.IsEnabled);
            entered.Reset();
            release.Reset();
            Volatile.Write(ref gate, 1);
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => entered.IsSet);
            Volatile.Write(ref gate, 0);
            list.SelectedIndex = 1;
            await Until(() => health.Text.Contains("冲突", StringComparison.Ordinal));
            Interlocked.Exchange(ref staleGate, 1);
            list.SelectedIndex = 0;
            await Until(() => staleEntered.IsSet);
            release.Set();
            await Until(() => remove.IsEnabled);
            staleRelease.Set();
            await Task.Delay(120);
            ShortcutResult first = service.Inspect("one");
            results.Add(("older A read completing after create cannot replace the verified result",
                first.Status == ShortcutStatus.Healthy && File.Exists(first.Path) && remove.IsEnabled &&
                create.Content.ToString()!.Contains("_C", StringComparison.Ordinal) &&
                repair.Content.ToString()!.Contains("_E", StringComparison.Ordinal) &&
                remove.Content.ToString()!.Contains("_M", StringComparison.Ordinal)));
            File.Delete(first.Path!);
            await main.RefreshAsync("a");
            await Until(() => repair.IsEnabled);
            repair.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !repair.IsEnabled && remove.IsEnabled && File.Exists(first.Path));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => create.IsEnabled);
            results.Add(("repair and remove keep binding and backup bytes intact",
                !File.Exists(first.Path) && health.Text.Contains("未创建", StringComparison.Ordinal) &&
                File.ReadAllBytes(profile).SequenceEqual(profileBytes) &&
                File.ReadAllBytes(profile + ".bak").SequenceEqual(backup) && File.ReadAllText(foreign) == "foreign fixture"));
        }
        finally { release.Set(); staleRelease.Set(); main.Close(); }
    }

    private static async Task Until(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!ready()) await Task.Delay(15, timeout.Token);
    }
}
