using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Diagnostics;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Studio;

internal static class F11DiagnosticsUiChecks
{
    internal static void Run(ref int failures)
    {
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f11-diagnostic-view-");
        var results = new List<(string Name, bool Pass)>();
        try { F09WindowChecks.Pump(() => RunCases(fixture.FullName, results)); }
        finally
        {
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, temp, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f11-diagnostic-view-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
        foreach (var result in results) Program.Record("F11 Diagnostics UI " + result.Name, result.Pass, ref failures);
    }

    private static async Task RunCases(string root, List<(string, bool)> results)
    {
        string catalog = Path.Combine(root, "Scenes");
        string desktop = Directory.CreateDirectory(Path.Combine(root, "Desktop")).FullName;
        F09WindowChecks.WriteScene(catalog, "scene-one", "Fixture work");
        string target = Path.Combine(Environment.SystemDirectory, "where.exe");
        var app = new ApplicationProfile("app", "Fixture application", target, new(ApplicationTargetKind.Executable, target, []));
        var store = new StudioStateRepository(Path.Combine(root, "state.json"));
        StudioState initial = StudioState.Empty with { Applications = [app], Bindings = [new("binding", "APP", "scene-one", true)] };
        if (store.Save(initial) != RepositoryError.None) throw new IOException("Profile fixture failed.");
        var logs = new DiagnosticLogStore(Path.Combine(root, "logs"));
        var shortcuts = new ManagedShortcutService(store, catalog, Path.Combine(root, "receipts"), target, () => desktop);
        string? copied = null;
        bool clipboardBusy = false;
        var dialog = new DiagnosticsWindow(store, catalog, logs, shortcuts, "binding", text =>
        {
            if (clipboardBusy) throw new ExternalException("Injected clipboard unavailable.");
            copied = text;
        });
        var repair = (Button)dialog.FindName("RepairEntryButton");
        var copy = (Button)dialog.FindName("CopyReportButton");
        var open = (Button)dialog.FindName("OpenLogsButton");
        var status = (TextBlock)dialog.FindName("DiagnosticStatusText");
        var entry = (TextBlock)dialog.FindName("EntryStatusText");
        try
        {
            await dialog.RefreshAsync();
            results.Add(("empty report and unregistered shortcut are read-only",
                !repair.IsEnabled && !copy.IsEnabled && !open.IsEnabled && !Directory.Exists(logs.DirectoryPath)));

            Guid request = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var item = new DiagnosticEvent(request, now, new(1, 0, 0, 0), 26200, DiagnosticAdapter.Executable,
                DiagnosticStage.Target, DiagnosticResultCode.WindowReady, DiagnosticFallback.None, 40);
            if (logs.Append(item, item with
            {
                Stage = DiagnosticStage.Animation,
                Result = DiagnosticResultCode.RendererFailed,
                Fallback = DiagnosticFallback.RendererUnavailable
            }) != DiagnosticStatus.Stored)
                throw new IOException("Log fixture failed.");
            await dialog.RefreshAsync();
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            string version = ((TextBlock)dialog.FindName("ComponentsText")).Text;
            string recent = ((TextBox)dialog.FindName("RecentEventsText")).Text;
            results.Add(("component and target/renderer states visible with explicit safe copy",
                version.Contains("IPC", StringComparison.Ordinal) && recent.Contains("渲染", StringComparison.Ordinal) &&
                copied?.Contains(request.ToString("N"), StringComparison.Ordinal) == true &&
                !copied.Contains(root, StringComparison.Ordinal) && open.IsEnabled));
            results.Add(("renderer failure offers guidance only in the local view",
                recent.Contains("在组件状态中检查渲染器", StringComparison.Ordinal) &&
                copied?.Contains("在组件状态中检查渲染器", StringComparison.Ordinal) == false));
            clipboardBusy = true;
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            results.Add(("clipboard failure is visible without discarding events",
                status.Text.Contains("剪贴板", StringComparison.Ordinal) && copy.IsEnabled));

            ShortcutResult created = shortcuts.Create("binding");
            if (created.Status != ShortcutStatus.Created) throw new IOException("Shortcut fixture failed.");
            File.Delete(created.Path!);
            await dialog.RefreshAsync();
            results.Add(("missing selected entry offers explicit repair", repair.IsEnabled && entry.Text.Contains("缺失", StringComparison.Ordinal)));
            repair.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                while (!File.Exists(created.Path) || !entry.Text.Contains("有效", StringComparison.Ordinal))
                    await Task.Delay(15, timeout.Token);
            File.AppendAllText(created.Path!, "edited fixture");
            byte[] edited = File.ReadAllBytes(created.Path!);
            await dialog.RefreshAsync();
            results.Add(("edited entry reports conflict and preserves bytes",
                !repair.IsEnabled && entry.Text.Contains("冲突", StringComparison.Ordinal) &&
                File.ReadAllBytes(created.Path!).SequenceEqual(edited)));

            store.Update(state => state with { Bindings = [] });
            await dialog.RefreshAsync();
            results.Add(("removed binding does not offer a stale repair",
                !repair.IsEnabled && File.ReadAllBytes(created.Path!).SequenceEqual(edited)));
        }
        finally { dialog.Close(); }
    }
}
