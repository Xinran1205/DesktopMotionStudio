using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Studio;

internal static class WpfRaceChecks
{
    internal static void Run(ref int failures)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DesktopMotionStudio.Studio;component/Styles.xaml", UriKind.Relative) });
        try
        {
            using var saveStore = new GatedStore(delayUpdate: true);
            var editor = new ApplicationEditorWindow(new ApplicationRegistry(saveStore));
            ((TextBox)editor.FindName("DisplayNameBox")).Text = "Delayed fixture";
            ((TextBox)editor.FindName("PathBox")).Text = Environment.ProcessPath!;
            bool attemptedClose = false, remainedOpen = false;
            var clock = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                if (!attemptedClose && saveStore.Started.IsSet)
                {
                    attemptedClose = true;
                    editor.Close();
                    remainedOpen = editor.IsVisible;
                    saveStore.Release.Set();
                }
                if (clock.Elapsed > TimeSpan.FromSeconds(6)) { saveStore.Release.Set(); editor.Close(); }
            };
            editor.Loaded += (_, _) => ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            timer.Start();
            bool? saved = editor.ShowDialog();
            timer.Stop(); saveStore.Release.Set();
            Program.Record("editor close during pending save waits for commit and reports success",
                attemptedClose && remainedOpen && saved == true && saveStore.State.Applications.Count == 1, ref failures);

            using var refreshStore = new GatedStore(delayUpdate: false);
            var main = new MainWindow(refreshStore, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            var add = (Button)main.FindName("AddButton");
            var status = (TextBlock)main.FindName("StatusText");
            Task olderRefresh = main.RefreshAsync();
            int phase = 0;
            bool latestSurvived = false;
            clock.Restart();
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                // Loaded starts the newer refresh while the explicit older one is gated.
                if (phase == 0 && refreshStore.Started.IsSet && add.IsEnabled && status.Text == "已读取此设备上的应用配置。")
                {
                    phase = 1;
                    refreshStore.Release.Set();
                }
                else if (phase == 1 && olderRefresh.IsCompleted)
                {
                    phase = 2;
                    latestSurvived = add.IsEnabled && status.Text == "已读取此设备上的应用配置。";
                    main.Close();
                }
                if (clock.Elapsed > TimeSpan.FromSeconds(6)) { refreshStore.Release.Set(); main.Close(); }
            };
            timer.Start(); main.ShowDialog(); timer.Stop(); refreshStore.Release.Set();
            Program.Record("older refresh failure cannot disable a newer readable home", latestSurvived, ref failures);

            var unreadableStore = new CommandThenUnreadableStore();
            main = new MainWindow(unreadableStore, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            bool commandStarted = false, recoveryShown = false;
            clock.Restart();
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                if (!commandStarted && ((Button)main.FindName("RenameButton")).IsEnabled)
                {
                    commandStarted = true;
                    ((TextBox)main.FindName("NameBox")).Text = "Renamed fixture";
                    ((Button)main.FindName("RenameButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (commandStarted && !((Button)main.FindName("AddButton")).IsEnabled && ((TextBlock)main.FindName("StatusText")).Text.Contains("无法", StringComparison.Ordinal))
                {
                    timer.Stop();
                    main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                    {
                        recoveryShown = unreadableStore.State.Applications.Single().DisplayName == "Renamed fixture" &&
                            ((TextBlock)main.FindName("StatusText")).Text.Contains("无法", StringComparison.Ordinal);
                        main.Close();
                    });
                }
                if (clock.Elapsed > TimeSpan.FromSeconds(6)) main.Close();
            };
            timer.Start(); main.ShowDialog(); timer.Stop();
            Program.Record("successful mutation followed by unreadable reload retains recovery error", recoveryShown, ref failures);
        }
        finally { application.Shutdown(); }
    }

    private sealed class CommandThenUnreadableStore : IStudioStateStore
    {
        internal StudioState State = StudioState.Empty with { Applications = [new("reload-fixture", "Fixture", "legacy")] };
        private bool _unreadable;
        public RepositoryResult Load() => _unreadable ? new(null, RepositoryError.IoError) : new(State, RepositoryError.None);
        public RepositoryResult Update(Func<StudioState, StudioState> transition)
        {
            State = transition(State); _unreadable = true;
            return new(State, RepositoryError.None);
        }
    }

    private sealed class GatedStore(bool delayUpdate) : IStudioStateStore, IDisposable
    {
        private int _reads;
        internal readonly ManualResetEventSlim Started = new();
        internal readonly ManualResetEventSlim Release = new();
        internal readonly ManualResetEventSlim Failed = new();
        internal StudioState State = StudioState.Empty;
        public RepositoryResult Load()
        {
            if (!delayUpdate && Interlocked.Increment(ref _reads) == 1)
            {
                Started.Set(); Release.Wait(TimeSpan.FromSeconds(5)); Failed.Set();
                throw new IOException("Controlled delayed read failure.");
            }
            return new(State, RepositoryError.None);
        }
        public RepositoryResult Update(Func<StudioState, StudioState> transition)
        {
            Started.Set(); Release.Wait(TimeSpan.FromSeconds(5)); State = transition(State);
            return new(State, RepositoryError.None);
        }
        public void Dispose() { Release.Set(); Started.Dispose(); Release.Dispose(); Failed.Dispose(); }
    }
}
