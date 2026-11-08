using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.Video;
using static F09UiChecks;
using static StudioUiChecks;

internal static class F21VideoChecks
{
    internal const string SceneId = "f21-video-fixture";
    internal const string LaunchTitle = "Desktop Motion Studio — Video launch";
    private const string PreviewTitle = "Desktop Motion Studio — Video preview";

    internal static void AddScene(string catalog, string fixtures, bool sixSecond = false)
    {
        string work = Directory.CreateDirectory(Path.Combine(catalog, SceneId)).FullName;
        File.Copy(Path.Combine(fixtures, sixSecond ? "landscape.mp4" : "landscape-10s.mp4"), Path.Combine(work, "clip.mp4"));
        File.WriteAllText(Path.Combine(work, "scene.json"), """
            {"schemaVersion":1,"sceneId":"f21-video-fixture","sceneVersion":"1.0.0","name":"F21 Video Fixture",
            "displayMode":"centered","assets":["scene.video.json","clip.mp4"]}
            """);
        File.WriteAllText(Path.Combine(work, "scene.video.json"), $$"""
            {"schemaVersion":1,"file":"clip.mp4","durationMilliseconds":{{(sixSecond ? 6000 : 10000)}},"fit":"complete","sound":true}
            """);
    }

    internal static int Run(string fixtures)
    {
        string studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH") ?? "";
        if (!File.Exists(studio)) return 2;
        string worker = Path.Combine(Path.GetDirectoryName(studio)!, "MotionHost", VideoPlaybackRequest.WorkerName);
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(38));
        var root = Directory.CreateTempSubdirectory("dms-f21-");
        try
        {
            Require(File.Exists(worker), "The self-contained bundle must include VideoHost.");
            var factory = new VideoPlaybackSessionFactory(worker);
            var landscape = new VideoClip(SceneId, Path.GetFullPath(Path.Combine(fixtures, "landscape-10s.mp4")), 10_000, VideoFit.Complete, true);
            var portrait = landscape with { AssetPath = Path.GetFullPath(Path.Combine(fixtures, "portrait.mp4")),
                DurationMilliseconds = 6_000, Fit = VideoFit.Fill, Sound = false };
            CheckPlayback(factory, landscape, worker, SceneDisplayMode.Centered, skip: false, watchdog.Token);
            var muted = landscape with { AssetPath = Path.GetFullPath(Path.Combine(fixtures, "landscape.mp4")),
                DurationMilliseconds = 6_000, Sound = false };
            CheckPlayback(factory, muted, worker, SceneDisplayMode.Centered, skip: false, watchdog.Token);
            string lowRate = Path.GetFullPath(Path.Combine(fixtures, "low-rate.mp4"));
            if (File.Exists(lowRate))
                CheckPlayback(factory, muted with { AssetPath = lowRate, DurationMilliseconds = 2000 }, worker,
                    SceneDisplayMode.Centered, skip: false, watchdog.Token);
            CheckPlayback(factory, portrait, worker, SceneDisplayMode.Full, skip: true, watchdog.Token);
            foreach (string invalid in new[] { "malformed.mp4", "missing.mp4", "unsupported.mp4" })
            {
                string media = Path.GetFullPath(Path.Combine(fixtures, invalid));
                if (invalid == "unsupported.mp4" && !File.Exists(media)) continue;
                CheckRejected(factory, landscape with { AssetPath = media }, invalid, watchdog.Token);
            }
            CheckPlacement(worker, portrait, root.FullName, watchdog.Token);
            CheckStudio(studio, worker, root.FullName, fixtures);
            return 0;
        }
        catch (Exception error) { Console.WriteLine($"FAIL F21 video: {error}"); return 1; }
        finally { root.Delete(recursive: true); }
    }

    private static void CheckPlacement(string worker, VideoClip clip, string root, CancellationToken token)
    {
        var preference = new MotionDisplayPreference(Path.Combine(root, "placement-profile.json"));
        var factory = new VideoPlaybackSessionFactory(worker, preference);
        var widths = new List<double>();
        foreach (string mode in new[] { "compact", "large", "full" })
        {
            Require(preference.TrySave(mode), "Could not set the isolated display preference.");
            var session = factory.Start(Guid.NewGuid(), clip, SceneDisplayMode.Centered, preview: false);
            try
            {
                Require(session.Started.WaitAsync(token).GetAwaiter().GetResult() is null, "Display preference preview did not start.");
                nint stage = FindOwnedMotionWindow(worker, LaunchTitle);
                Require(stage != 0, "The video window is missing.");
                Rect bounds = F18DisplayChecks.VerifyStageBounds(stage, mode == "full");
                widths.Add(bounds.Width);
                Require(PostMessageW(stage, 0x0100, 0x1B, 1), "Could not skip the display preference preview.");
                Require(session.Completion.WaitAsync(token).GetAwaiter().GetResult() == MotionHostOutcome.Skipped, "Placement preview did not stop.");
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        Require(widths[0] < widths[1] && widths[1] < widths[2], "Video stage sizes do not follow the display presets.");
        Console.WriteLine($"PASS F21 video display presets: centered compact {widths[0]}, centered large {widths[1]}, full monitor {widths[2]} px.");
    }

    private static void CheckPlayback(VideoPlaybackSessionFactory factory, VideoClip clip, string worker, SceneDisplayMode mode,
        bool skip, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var session = factory.Start(Guid.NewGuid(), clip, mode, preview: false);
        try
        {
            Require(session.Started.WaitAsync(token).GetAwaiter().GetResult() is null, $"No first frame: {session.FailureReason}.");
            long visible = clock.ElapsedMilliseconds;
            nint stage = FindOwnedMotionWindow(worker, LaunchTitle);
            Require(stage != 0, "The first-frame event was sent before the window became visible.");
            F18DisplayChecks.VerifyStageBounds(stage, mode == SceneDisplayMode.Full);
            long skipRequested = clock.ElapsedMilliseconds;
            if (skip) Keys(stage, 0x1B);
            MotionHostOutcome result = session.Completion.WaitAsync(token).GetAwaiter().GetResult();
            long elapsed = clock.ElapsedMilliseconds - visible;
            Require(result == (skip ? MotionHostOutcome.Skipped : MotionHostOutcome.Completed), $"Playback ended with {result}.");
            Require(skip ? clock.ElapsedMilliseconds - skipRequested < 1000 : elapsed >= clip.DurationMilliseconds - 150 &&
                elapsed < clip.DurationMilliseconds + VideoPlaybackRequest.FinishMilliseconds + VideoPlaybackRequest.CleanupMilliseconds,
                $"Unexpected playback duration of {elapsed} ms.");
            Require(FindOwnedMotionWindow(worker, LaunchTitle) == 0, "The owned video window survived completion.");
            Console.WriteLine($"PASS F21 {Path.GetFileName(clip.AssetPath)}, {clip.Fit}/{mode}: first frame {visible} ms, {result} and cleaned up {elapsed} ms later.");
        }
        finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    internal static int CheckTimeouts(string media)
    {
        string worker = Path.ChangeExtension(typeof(F21VideoChecks).Assembly.Location, ".exe");
        if (!File.Exists(worker) || !File.Exists(media)) return 2;
        var factory = new VideoPlaybackSessionFactory(worker);
        try
        {
            foreach (bool visible in new[] { false, true })
            {
                using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(7));
                var clip = new VideoClip(visible ? "f21-hang" : "f21-timeout", Path.GetFullPath(media), 1000, VideoFit.Complete, false);
                var clock = Stopwatch.StartNew();
                var session = factory.Start(Guid.NewGuid(), clip, SceneDisplayMode.Centered, preview: true);
                try
                {
                    Require(session.Started.WaitAsync(watchdog.Token).GetAwaiter().GetResult() ==
                        (visible ? null : MotionHostOutcome.TimedOut), "Incorrect startup result for stalled worker.");
                    Require(session.Completion.WaitAsync(watchdog.Token).GetAwaiter().GetResult() == MotionHostOutcome.TimedOut &&
                        session.FailureReason == MotionHostError.TimedOut, "Stalled worker did not time out.");
                    foreach (Process candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(worker)))
                    {
                        using (candidate)
                            Require(candidate.Id == Environment.ProcessId || candidate.HasExited ||
                                !string.Equals(candidate.MainModule?.FileName, worker, StringComparison.OrdinalIgnoreCase),
                                "The owned stalled worker remained active after completion.");
                    }
                    Console.WriteLine($"PASS F21 {(visible ? "playback" : "decoder startup")} timeout: owned process gone in {clock.ElapsedMilliseconds} ms.");
                }
                finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }
            return 0;
        }
        catch (Exception error) { Console.WriteLine($"FAIL F21 timeout: {error}"); return 1; }
    }

    internal static int CheckReducedMotion(string media)
    {
        string studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH") ?? "";
        if (!File.Exists(studio) || !File.Exists(media)) return 2;
        string worker = Path.Combine(Path.GetDirectoryName(studio)!, "MotionHost", VideoPlaybackRequest.WorkerName);
        var factory = new VideoPlaybackSessionFactory(worker);
        var clip = new VideoClip("f21-reduced", Path.GetFullPath(media), 6000, VideoFit.Complete, true);
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var session = factory.Start(Guid.NewGuid(), clip, SceneDisplayMode.Centered, preview: false);
        try
        {
            Require(session.Started.WaitAsync(watchdog.Token).GetAwaiter().GetResult() is null, "Reduced-motion first frame missing.");
            var elapsed = Stopwatch.StartNew();
            Require(session.Completion.WaitAsync(watchdog.Token).GetAwaiter().GetResult() == MotionHostOutcome.Completed &&
                elapsed.ElapsedMilliseconds < 900, "Windows reduced motion must replace the clip with a brief still.");
            Console.WriteLine($"PASS F21 Windows reduced motion: 6000 ms clip reduced to a brief still; closed and cleaned up in {elapsed.ElapsedMilliseconds} ms.");
            return 0;
        }
        catch (Exception error) { Console.WriteLine($"FAIL F21 reduced motion: {error}"); return 1; }
        finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    internal static int RunStalledWorker(string[] args)
    {
        if (args[2] != "--request-id" || !Guid.TryParseExact(args[3], "D", out Guid id) ||
            id == Guid.Empty || args[1] != $"dms-motionhost-{id:N}") return 2;
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        pipe.ConnectAsync(watchdog.Token).GetAwaiter().GetResult();
        var request = VideoPlaybackWire.ReadAsync<VideoPlaybackRequest>(pipe, watchdog.Token).GetAwaiter().GetResult();
        if (!request.IsValid || request.RequestId != id) return 2;
        if (request.Clip.SceneId == "f21-hang")
            VideoPlaybackWire.WriteAsync(pipe, new VideoPlaybackEvent(VideoPlaybackRequest.CurrentVersion, id, VideoEventKind.Visible),
                watchdog.Token).GetAwaiter().GetResult();
        else if (request.Clip.SceneId != "f21-timeout") return 2;
        Thread.Sleep(TimeSpan.FromSeconds(6));
        return 3;
    }

    private static void CheckRejected(VideoPlaybackSessionFactory factory, VideoClip clip, string name, CancellationToken token)
    {
        var session = factory.Start(Guid.NewGuid(), clip, SceneDisplayMode.Centered, preview: true);
        try
        {
            Require(session.Started.WaitAsync(token).GetAwaiter().GetResult() == MotionHostOutcome.Failed &&
                session.Completion.WaitAsync(token).GetAwaiter().GetResult() == MotionHostOutcome.Failed &&
                session.FailureReason == MotionHostError.UnsupportedPlayback, $"{name} was not rejected before visibility.");
            Console.WriteLine($"PASS F21 {name}: rejected before showing a stage.");
        }
        finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static void CheckStudio(string studio, string worker, string root, string fixtures)
    {
        string catalog = Directory.CreateDirectory(Path.Combine(root, "Scenes")).FullName;
        AddScene(catalog, fixtures, sixSecond: true);
        string profile = Path.Combine(root, "profile.json");
        var store = new StudioStateRepository(profile);
        Require(store.Save(StudioState.Empty with { Settings = new("dark", true) }) == RepositoryError.None,
            "Could not save isolated muted profile.");
        byte[] original = File.ReadAllBytes(profile);
        using Process process = Start(studio, profile, catalog);
        try
        {
            nint home = Window(process, "Desktop Motion Studio");
            Invoke(AutomationElement.FromHandle(home), "打开动效作品库");
            var library = AutomationElement.FromHandle(Window(process, "动效作品库"));
            Require(Program.WaitFor(() => ListCount(library, "完整动效作品") == 1), "The fixture did not appear in the library.");
            Select(library, "完整动效作品", "F21 Video Fixture");
            Invoke(library, "预览作品");
            nint stage = 0;
            Require(Program.WaitFor(() => (stage = FindOwnedMotionWindow(worker, PreviewTitle)) != 0), "Studio did not open the video preview.");
            Require(PostMessageW(stage, 0x0100, 0x1B, 1), "Could not send Escape to the owned preview window.");
            Require(Program.WaitFor(() => library.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "预览已跳过。")) is not null), "Studio did not report the skip.");
            Require(FindOwnedMotionWindow(worker, PreviewTitle) == 0 && File.ReadAllBytes(profile).SequenceEqual(original),
                "Preview left a window open or changed the profile.");
            Console.WriteLine("PASS F21 packaged Studio: video is discoverable, preview stops with Escape, profile stays byte-identical.");
        }
        finally { Stop(process); }
    }

    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nuint key, nint flags);
}
