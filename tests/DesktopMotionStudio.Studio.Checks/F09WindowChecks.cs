using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Studio;

internal static class F09WindowChecks
{
    internal static void Run(ref int failures)
    {
        string root = Path.Combine(Path.GetTempPath(), $"dms-f09-window-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        var outcomes = new List<(string, bool)>();
        try
        {
            WriteScene(root, "scene-one", "First work"); string two = WriteScene(root, "scene-two", "Second work");
            Pump(async () =>
            {
                await using var preview = new PreviewCoordinator(new NoPreview());
                var workspace = new SceneWorkspace(root);
                var window = new SceneLibraryWindow(new EmptyStore(), workspace, preview, "scene-two");
                await window.RefreshAsync();
                var list = (ListBox)window.FindName("SceneList");
                outcomes.Add(("library restores intended scene identity and playable actions", list.Items.Count == 2 && ((TextBlock)window.FindName("SceneNameText")).Text == "Second work" && ((Button)window.FindName("PreviewButton")).IsEnabled));
                File.WriteAllText(Path.Combine(two, "scene.playback.json"), "{}");
                await window.RefreshAsync();
                outcomes.Add(("invalid current playback disables preview and binding", !((Button)window.FindName("PreviewButton")).IsEnabled && !((Button)window.FindName("BindButton")).IsEnabled && ((TextBlock)window.FindName("AvailabilityText")).Text.Contains("不可用", StringComparison.Ordinal)));
                Directory.Delete(two, true); await window.RefreshAsync();
                outcomes.Add(("removed intended work never silently selects another", list.SelectedItem is null && !((Button)window.FindName("PreviewButton")).IsEnabled && ((Button)window.FindName("HomeButton")).IsEnabled));
                window.Close();
                var empty = new SceneLibraryWindow(new EmptyStore(), new SceneWorkspace(Path.Combine(root, "missing")), preview);
                await empty.RefreshAsync();
                outcomes.Add(("library root error keeps home and retry available", !((Button)empty.FindName("PreviewButton")).IsEnabled && ((Button)empty.FindName("HomeButton")).IsEnabled && ((Button)empty.FindName("RefreshButton")).IsEnabled && ((TextBlock)empty.FindName("StatusText")).Text.Contains("无法", StringComparison.Ordinal)));
                empty.Close();

                var observed = new CompletedPreviewFactory();
                await using var closingPreview = new PreviewCoordinator(observed);
                using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
                int observedCompletion = 0;
                closingPreview.StateChanged += () => { if (closingPreview.Current.Phase == PreviewPhase.Completed && Interlocked.Increment(ref observedCompletion) == 1) { entered.Set(); release.Wait(TimeSpan.FromSeconds(3)); } };
                var playback = workspace.FindPlayable("scene-one")!.Playback!;
                Task<PreviewResult> old = closingPreview.PlayAsync(new(Guid.NewGuid(), playback, SceneDisplayMode.Centered));
                if (!await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(2)))) throw new TimeoutException("Completed observer barrier was not reached");
                var closingWindow = new SceneLibraryWindow(new EmptyStore(), workspace, closingPreview);
                bool closed = false; closingWindow.Closed += (_, _) => closed = true;
                Task<PreviewResult> queued = closingPreview.PlayAsync(new(Guid.NewGuid(), playback, SceneDisplayMode.Centered));
                closingWindow.Close(); release.Set();
                await old; PreviewResult cancelled = await queued;
                for (int wait = 0; !closed && wait < 100; wait++) await Task.Delay(5);
                outcomes.Add(("library close cancels queued playback before Starting is published", closed && observed.Starts == 1 && cancelled.Outcome == PreviewOutcome.Superseded));
                foreach (string mode in new[] { "busy", "unreadable", "concurrent" })
                    outcomes.Add(await BindingFailureAsync(workspace, preview, mode));
            });
        }
        finally { Directory.Delete(root, true); }
        foreach (var outcome in outcomes) Program.Record(outcome.Item1, outcome.Item2, ref failures);
    }

    internal static string WriteScene(string root, string id, string name)
    {
        string directory = Path.Combine(root, id); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "scene.json"), JsonSerializer.Serialize(new { schemaVersion = 1, sceneId = id, sceneVersion = "1.0.0", name, displayMode = "centered", assets = new[] { "scene.playback.json" } }));
        File.WriteAllText(Path.Combine(directory, "scene.presentation.json"), JsonSerializer.Serialize(new { schemaVersion = 1, sceneId = id, sceneVersion = "1.0.0", description = "Original isolated test work", style = "Orbit", theme = "Neutral" }));
        File.WriteAllText(Path.Combine(directory, "scene.playback.json"), PlaybackJson(id));
        return directory;
    }

    internal static string PlaybackJson(string id) => JsonSerializer.Serialize(new { schemaVersion = 1, sceneId = id, sceneVersion = "1.0.0", runtimeId = "vector-orbit", runtimeVersion = 1, durationMilliseconds = 3000, accent = id == "scene-one" ? "#618EFF" : "#EEAA22", glyph = id == "scene-one" ? "M" : "S", keyframes = new[] { new { atMilliseconds = 0, scale = .6, opacity = .2, rotationDegrees = 0 }, new { atMilliseconds = 1500, scale = 1d, opacity = 1d, rotationDegrees = 180 }, new { atMilliseconds = 3000, scale = .8, opacity = .1, rotationDegrees = 360 } } });

    private static async Task<(string, bool)> BindingFailureAsync(SceneWorkspace workspace, PreviewCoordinator preview, string mode)
    {
        var store = new FailureStore(mode);
        var library = new SceneLibraryWindow(store, workspace, preview, "scene-one");
        var completed = new TaskCompletionSource<bool>();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        int phase = 0;
        bool errorSeen = false;
        string expected = mode == "busy" ? "使用" : mode == "unreadable" ? "无法" : "改变";
        timer.Tick += (_, _) =>
        {
            try
            {
                if (phase == 0 && ((Button)library.FindName("BindButton")).IsEnabled)
                {
                    phase = 1;
                    library.Dispatcher.BeginInvoke(() => ((Button)library.FindName("BindButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                }
                else if (phase == 1 && library.OwnedWindows.OfType<BindingPickerWindow>().FirstOrDefault() is BindingPickerWindow picker && ((ListBox)picker.FindName("BindingAppsList")).Items.Count == 1)
                {
                    ((ListBox)picker.FindName("BindingAppsList")).SelectedIndex = 0;
                    if (((Button)picker.FindName("SaveButton")).IsEnabled) { phase = 2; ((Button)picker.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                }
                else if (phase == 2 && library.OwnedWindows.OfType<BindingPickerWindow>().FirstOrDefault() is BindingPickerWindow failed && ((TextBlock)failed.FindName("StatusText")).Text.Contains(expected, StringComparison.Ordinal) && ((Button)failed.FindName("CancelButton")).IsEnabled)
                { errorSeen = true; phase = 3; failed.Close(); }
                else if (phase == 3 && ((TextBlock)library.FindName("StatusText")).Text.Contains(expected, StringComparison.Ordinal))
                {
                    string status = ((TextBlock)library.FindName("StatusText")).Text;
                    bool stored = mode == "busy" ? store.State.Bindings.Count == 0 : mode == "concurrent" ? store.State.Bindings.Single().SceneId == "other-scene" : store.State.Bindings.Count == 1;
                    completed.TrySetResult(errorSeen && stored && !status.Contains("已保存", StringComparison.Ordinal));
                }
            }
            catch (Exception ex) { completed.TrySetException(ex); }
        };
        library.Show(); timer.Start();
        try { return ($"binding {mode} error survives picker cancellation and parent reload", await completed.Task.WaitAsync(TimeSpan.FromSeconds(6))); }
        finally { timer.Stop(); foreach (Window child in library.OwnedWindows.Cast<Window>().ToArray()) child.Close(); library.Close(); }
    }

    private sealed class FailureStore(string mode) : IStudioStateStore
    {
        internal StudioState State = StudioState.Empty with { Applications = [new("fixture", "Fixture app", Environment.ProcessPath!, new(ApplicationTargetKind.Executable, Environment.ProcessPath!, []))] };
        private bool _committed;
        public RepositoryResult Load() => _committed && mode == "unreadable" ? new(null, RepositoryError.IoError) : new(State, RepositoryError.None);
        public RepositoryResult Update(Func<StudioState, StudioState> transition)
        {
            if (mode == "busy") return new(null, RepositoryError.Busy);
            State = transition(State); _committed = true;
            if (mode == "concurrent") State = State with { Bindings = State.Bindings.Select(binding => binding with { SceneId = "other-scene" }).ToArray() };
            return new(State, RepositoryError.None);
        }
    }

    internal static void Pump(Func<Task> action)
    {
        Exception? failure = null;
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timeout.Tick += (_, _) => { failure = new TimeoutException("View check exceeded its deadline"); frame.Continue = false; };
        Dispatcher.CurrentDispatcher.BeginInvoke(async () => { try { await action(); } catch (Exception ex) { failure = ex; } finally { frame.Continue = false; } });
        timeout.Start(); Dispatcher.PushFrame(frame); timeout.Stop();
        if (failure is not null) throw failure;
    }

    private sealed class EmptyStore : IStudioStateStore
    {
        public RepositoryResult Load() => new(StudioState.Empty, RepositoryError.None);
        public RepositoryResult Update(Func<StudioState, StudioState> transition) => throw new InvalidOperationException("Browsing never writes state");
    }
    private sealed class NoPreview : IPreviewSessionFactory { public IPreviewSession Start(PreviewRequest request) => throw new InvalidOperationException("Browsing never starts playback"); }
    private sealed class CompletedPreviewFactory : IPreviewSessionFactory
    {
        internal int Starts;
        public IPreviewSession Start(PreviewRequest request) { Interlocked.Increment(ref Starts); return new CompletedSession(); }
    }
    private sealed class CompletedSession : IPreviewSession
    {
        public Task<MotionHostOutcome?> Started => Task.FromResult<MotionHostOutcome?>(null);
        public Task<MotionHostOutcome> Completion => Task.FromResult(MotionHostOutcome.Completed);
        public MotionHostError? FailureReason => null;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
