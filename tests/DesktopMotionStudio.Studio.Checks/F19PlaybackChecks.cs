using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DesktopMotionStudio.Core.Instances;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;
using static StudioUiChecks;

internal static class F19PlaybackChecks
{
    internal static int Run(string oldRenderer)
    {
        string host = Environment.GetEnvironmentVariable("DMS_F06_HOST_PATH") ?? "";
        string targetSource = Environment.GetEnvironmentVariable("DMS_F06_TARGET_PATH") ?? "";
        if (!File.Exists(host) || !File.Exists(targetSource) || !File.Exists(oldRenderer)) return 2;
        string catalog = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(host)!, "..", "Scenes"));
        SceneLibraryItem work = new SceneLibrary().Scan(catalog).Items.Single(item => item.Catalog.Manifest?.SceneId == "zero-boundary");
        var root = Directory.CreateTempSubdirectory("dms-f19-");
        string target = Path.Combine(root.FullName, $"DmsF19Target{Guid.NewGuid():N}.exe");
        try
        {
            foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(targetSource)!))
                File.Copy(file, string.Equals(file, targetSource, StringComparison.OrdinalIgnoreCase)
                    ? target : Path.Combine(root.FullName, Path.GetFileName(file)));
            foreach (string scenario in new[] { "natural and duplicate", "escape", "external open", "cancel", "crash", "missing", "older IPC" })
                CheckScenario(scenario, host, oldRenderer, target, work);
            return 0;
        }
        catch (Exception error) { Console.WriteLine($"FAIL F19 order: {error}"); return 1; }
        finally { root.Delete(recursive: true); }
    }

    private static void CheckScenario(string scenario, string host, string oldRenderer, string target, SceneLibraryItem work)
    {
        string executable = scenario switch
        {
            "missing" => Path.Combine(Path.GetDirectoryName(target)!, "missing-host.exe"),
            "older IPC" => oldRenderer,
            _ => host
        };
        var factory = new TrackedHost(new WindowsMotionHostProcessFactory(executable));
        var nativeLauncher = new ApplicationLauncher();
        using var cancel = new CancellationTokenSource();
        Process? observedHost = null;
        Process? externalTarget = null;
        Process? launchedTarget = null;
        int dispatches = 0;
        bool closedAtDispatch = false;
        long dispatchMilliseconds = 0;
        long visibleMilliseconds = 0;
        var elapsed = Stopwatch.StartNew();
        var instances = new ApplicationInstanceCoordinator(new WindowsWindowPlatform(), resolved =>
        {
            Interlocked.Increment(ref dispatches);
            closedAtDispatch = observedHost is null || observedHost.HasExited;
            dispatchMilliseconds = elapsed.ElapsedMilliseconds;
            LaunchResult result = nativeLauncher.Launch(resolved);
            if (result.ProcessId is int pid) launchedTarget = Process.GetProcessById(pid);
            return result;
        });
        var coordinator = new MotionHostLifecycleCoordinator(instances, factory);
        var targetSpec = new ExecutableTarget(target, []);
        string appId = $"f19-{Guid.NewGuid():N}";
        Task<MotionHostLifecycleResult> StartRequest() => coordinator.OpenAfterPlaybackAsync(
            appId, targetSpec, work.Catalog.Manifest, work.Playback, cancel.Token);
        Task<MotionHostLifecycleResult>? duplicate = null;
        Task<MotionHostLifecycleResult> pending = StartRequest();
        try
        {
            if (scenario is not ("missing" or "older IPC"))
            {
                IMotionHostSession session = factory.Created.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                observedHost = Process.GetProcessById(session.HostProcessId!.Value);
                nint stage = Window(observedHost, "Desktop Motion Studio — Launch");
                visibleMilliseconds = elapsed.ElapsedMilliseconds;
                Require(scenario, dispatches == 0 && CountTargets(target) == 0 && !pending.IsCompleted, "target existed while the animation was visible");
                if (scenario == "natural and duplicate") duplicate = StartRequest();
                if (scenario == "escape")
                    Require(scenario, PostMessageW(stage, 0x0100, 0x1B, 1), "Could not send Esc to the owned animation window");
                if (scenario == "cancel") cancel.Cancel();
                if (scenario == "crash") observedHost.Kill();
                if (scenario == "external open")
                {
                    externalTarget = Process.Start(new ProcessStartInfo(target) { UseShellExecute = false })
                        ?? throw new InvalidOperationException("External fixture target did not start.");
                    Require(scenario, Program.WaitFor(() => { externalTarget.Refresh(); return externalTarget.MainWindowHandle != 0; }),
                        "External target did not expose its own window.");
                }
            }

            MotionHostLifecycleResult result = pending.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
            var expected = scenario switch
            {
                "escape" => MotionHostOutcome.Skipped,
                "missing" or "older IPC" or "crash" => MotionHostOutcome.Failed,
                _ => MotionHostOutcome.Completed
            };
            if (scenario == "cancel")
                Require(scenario, result.TargetResult.Outcome == InstanceOutcome.Canceled && dispatches == 0 && observedHost?.HasExited == true,
                    "cancel dispatched a target or left an animation behind");
            else if (scenario == "external open")
                Require(scenario, result.HostOutcome == expected && dispatches == 0 && result.TargetResult.Process?.ProcessId == externalTarget?.Id &&
                    result.TargetResult.Outcome is InstanceOutcome.ActivatedExisting or InstanceOutcome.ForegroundDenied,
                    "the externally opened target was not reused");
            else
                Require(scenario, result.HostOutcome == expected && result.TargetResult.Outcome == InstanceOutcome.WindowReady &&
                    closedAtDispatch && dispatches == 1 && launchedTarget is not null,
                    $"expected {expected} and one post-close dispatch, got {result.HostOutcome}/{result.HostError}/{result.TargetResult.Outcome}/{dispatches}");

            if (duplicate is not null)
            {
                MotionHostLifecycleResult repeated = duplicate.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
                Require(scenario, dispatches == 1 && factory.Starts == 1 && repeated.HostOutcome == MotionHostOutcome.NotStarted &&
                    repeated.TargetResult.Process?.ProcessId == launchedTarget?.Id && dispatchMilliseconds - visibleMilliseconds >= 1700,
                    "duplicate dispatch or replay occurred, or natural playback ended too early");
            }
            if (scenario == "older IPC")
                Require(scenario, result.HostError == MotionHostError.UnsupportedProtocol, "older host did not fail with a typed protocol mismatch");
            if (scenario == "missing")
                Require(scenario, result.HostError == MotionHostError.StartFailed, "missing host was not reported as unavailable");
            Console.WriteLine($"PASS F19 {scenario}: host={result.HostOutcome}, target={result.TargetResult.Outcome}, dispatches={dispatches}, visible={visibleMilliseconds} ms, dispatch={dispatchMilliseconds} ms");
        }
        finally
        {
            cancel.Cancel();
            try { pending.Wait(TimeSpan.FromSeconds(4)); duplicate?.Wait(TimeSpan.FromSeconds(4)); }
            catch (AggregateException) { }
            observedHost?.Dispose();
            if (externalTarget is not null) Stop(externalTarget);
            if (launchedTarget is not null) Stop(launchedTarget);
        }
    }

    private static void Require(string scenario, bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException($"{scenario}: {failure}");
    }

    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    private sealed class TrackedHost(IMotionHostSessionFactory factory) : IMotionHostSessionFactory
    {
        internal readonly TaskCompletionSource<IMotionHostSession> Created = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Starts { get; private set; }

        public IMotionHostSession Start(MotionHostStartRequest request)
        {
            Starts++;
            if (request.Mode != MotionHostMode.PreLaunch) throw new InvalidOperationException("Expected pre-launch protocol mode.");
            IMotionHostSession session = factory.Start(request);
            Created.TrySetResult(session);
            return session;
        }
    }
}
