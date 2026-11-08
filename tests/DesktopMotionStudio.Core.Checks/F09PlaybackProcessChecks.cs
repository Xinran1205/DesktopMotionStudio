using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.Checks;

internal static class F09PlaybackProcessChecks
{
    internal static int RunFiniteLaunch(string legacyHost, string delayedTarget)
    {
        string? hostPath = Environment.GetEnvironmentVariable("DMS_F06_HOST_PATH");
        if (!OperatingSystem.IsWindows() || !File.Exists(hostPath) || !File.Exists(legacyHost) || !File.Exists(delayedTarget)) return 2;
        ScenePlaybackDefinition playback = F09Checks.TechPlayback() with { SceneId = "dragon-portal" };
        var factory = new WindowsMotionHostProcessFactory(hostPath);
        int failures = 0;
        foreach (SceneDisplayMode mode in new[] { SceneDisplayMode.Centered, SceneDisplayMode.Full })
        {
            IMotionHostSession session = factory.Start(new(Guid.NewGuid(), playback.SceneId, playback.SceneVersion, mode, Playback: playback));
            try
            {
                MotionHostOutcome? start = session.Started.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                nint window = FindHostWindow(session.HostProcessId!.Value);
                var visible = Stopwatch.StartNew();
                MotionHostOutcome end = session.Completion.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                long duration = visible.ElapsedMilliseconds;
                bool passed = start is null && window != 0 && end == MotionHostOutcome.Completed &&
                    session.FailureReason is null && duration <= 2500 && !IsWindowVisible(window);
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")} F13 actual {mode} naturally closes in {duration}ms as {end}");
                if (!passed) failures++;
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        foreach (bool escape in new[] { false, true })
        {
            IMotionHostSession session = factory.Start(new(Guid.NewGuid(), playback.SceneId, playback.SceneVersion, SceneDisplayMode.Centered, Playback: playback));
            try
            {
                MotionHostOutcome? started = session.Started.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                nint window = FindHostWindow(session.HostProcessId!.Value);
                var elapsed = Stopwatch.StartNew();
                bool sent = !escape || window != 0 && F06DesktopAcceptance.ActivateWindow(window) && F06DesktopAcceptance.SendEscape();
                MotionHostOutcome result = (escape ? session.Completion : session.HandoffAsync())
                    .WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                bool passed = started is null && window != 0 && sent && !IsWindowVisible(window) && session.FailureReason is null &&
                    (escape ? result == MotionHostOutcome.Skipped : result == MotionHostOutcome.HandedOff && elapsed.ElapsedMilliseconds <= 300);
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")} F13 launch {(escape ? "Esc" : "handoff")} closes in {elapsed.ElapsedMilliseconds}ms as {result}");
                if (!passed) failures++;
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        failures += F06Checks.RunRealTargetHostIntegration(hostPath, playback, delayedTarget, ["--target", "550"]);
        failures += F06Checks.RunRealTargetHostIntegration(hostPath, playback, delayedTarget, ["--target", "2750"], MotionHostOutcome.Completed);
        failures += F06Checks.RunRealTargetHostIntegration(legacyHost, playback, delayedTarget, ["--target", "2750"],
            MotionHostOutcome.Failed, MotionHostError.UnsupportedProtocol);
        return failures + ActualFailures(hostPath, playback, MotionHostMode.Launch);
    }

    internal static int RunReducedMotion()
    {
        string? hostPath = Environment.GetEnvironmentVariable("DMS_F06_HOST_PATH");
        if (!OperatingSystem.IsWindows() || !File.Exists(hostPath)) return 2;
        if (!SystemParametersInfo(0x1042, 0, out bool enabled, 0) || enabled)
        {
            Console.WriteLine("FAIL F13 reduced-motion acceptance requires the actual Windows animation preference to be off.");
            return 1;
        }
        ScenePlaybackDefinition playback = F09Checks.TechPlayback();
        IMotionHostSession session = new WindowsMotionHostProcessFactory(hostPath).Start(
            new(Guid.NewGuid(), playback.SceneId, playback.SceneVersion, SceneDisplayMode.Centered, Playback: playback));
        try
        {
            MotionHostOutcome? started = session.Started.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            nint window = FindHostWindow(session.HostProcessId!.Value);
            var elapsed = Stopwatch.StartNew();
            MotionHostOutcome result = session.Completion.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            bool passed = started is null && window != 0 && result == MotionHostOutcome.Completed &&
                session.FailureReason is null && elapsed.ElapsedMilliseconds < 600 && !IsWindowVisible(window);
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} F13 Windows animations disabled, static launch closes in {elapsed.ElapsedMilliseconds}ms as {result}");
            return passed ? 0 : 1;
        }
        finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    internal static int Run()
    {
        string? hostPath = Environment.GetEnvironmentVariable("DMS_F06_HOST_PATH");
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath)) { Console.WriteLine("SKIP F09 actual playback host (DMS_F06_HOST_PATH unset)"); return 0; }
        int failures = 0;
        var playback = ScenePlaybackReader.Read(Encoding.UTF8.GetBytes(F09Checks.PlaybackJson), SceneManifestReader.Read(Encoding.UTF8.GetBytes(F09Checks.ManifestJson)).Manifest!).Value!;
        void Check(string name, bool passed) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")} F09 {name}"); if (!passed) failures++; }
        foreach (string operation in new[] { "natural", "stop", "handoff" })
        {
            var request = new MotionHostStartRequest(Guid.NewGuid(), playback.SceneId, playback.SceneVersion, SceneDisplayMode.Centered,
                Mode: operation == "handoff" ? MotionHostMode.Launch : MotionHostMode.Preview, Playback: playback);
            IMotionHostSession session = new WindowsMotionHostProcessFactory(hostPath).Start(request);
            var elapsed = Stopwatch.StartNew();
            try
            {
                MotionHostOutcome? start = session.Started.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                int processId = session.HostProcessId ?? 0;
                nint window = FindHostWindow(processId);
                Check($"{operation} selected scene reaches Started and a real visible HWND", start is null && window != 0);
                long visibleAt = elapsed.ElapsedMilliseconds;
                MotionHostOutcome result = operation switch
                {
                    "stop" => session.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(),
                    "handoff" => session.HandoffAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(),
                    _ => session.Completion.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()
                };
                long finishMs = elapsed.ElapsedMilliseconds - visibleAt;
                Console.WriteLine($"  scene {operation}: PID={processId}, HWND=0x{window:X}, afterStarted={finishMs}ms, result={result}, error={session.FailureReason}");
                Check($"{operation} returns precise playback outcome with bounded timing", session.FailureReason is null && operation switch
                {
                    "natural" => result == MotionHostOutcome.Completed && finishMs >= playback.DurationMilliseconds - 100 && finishMs <= playback.DurationMilliseconds + 500,
                    "stop" => result == MotionHostOutcome.Exited && finishMs < 2000,
                    _ => result == MotionHostOutcome.HandedOff && finishMs <= 300
                });
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        failures += F06Checks.RunRealTargetHostIntegration(hostPath, playback with { SceneId = "dragon-portal" });
        failures += CoordinatorAsync(hostPath, playback).GetAwaiter().GetResult();
        failures += ActualFailures(hostPath, playback);
        return failures;
    }

    private static int ActualFailures(string hostPath, ScenePlaybackDefinition playback, MotionHostMode mode = MotionHostMode.Preview)
    {
        int failures = 0;
        var shortWork = playback with { DurationMilliseconds = 1000, Keyframes = System.Collections.Immutable.ImmutableArray.Create(playback.Keyframes[0], playback.Keyframes[^1] with { AtMilliseconds = 1000 }) };
        foreach (bool hang in new[] { false, true })
        {
            IMotionHostSession session = new WindowsMotionHostProcessFactory(hostPath).Start(new(Guid.NewGuid(), shortWork.SceneId, shortWork.SceneVersion, SceneDisplayMode.Centered, 1500, mode, shortWork));
            Microsoft.Win32.SafeHandles.SafeWaitHandle? thread = null;
            bool suspended = false;
            Process? host = null;
            try
            {
                if (session.Started.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult() is not null) throw new InvalidOperationException("Failure fixture did not start");
                host = Process.GetProcessById(session.HostProcessId!.Value); _ = host.SafeHandle;
                nint window = FindHostWindow(host.Id);
                uint threadId = GetWindowThreadProcessId(window, out uint owner);
                if (window == 0 || owner != (uint)host.Id) throw new InvalidOperationException("Failure fixture window is not owned");
                var elapsed = Stopwatch.StartNew();
                if (hang)
                {
                    thread = OpenThread(0x0002 | 0x0800, false, threadId); // SUSPEND_RESUME | QUERY_LIMITED_INFORMATION
                    if (thread.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
                    if (GetProcessIdOfThread(thread) != (uint)host.Id) throw new InvalidOperationException("Thread handle no longer belongs to the retained host identity");
                    uint previous = SuspendThread(thread);
                    if (previous == uint.MaxValue) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
                    suspended = true;
                    if (previous != 0) throw new InvalidOperationException("Fixture thread was already suspended");
                }
                else host.Kill(entireProcessTree: false);
                MotionHostOutcome outcome = session.Completion.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                bool passed = host.WaitForExit(1000) && (hang ? outcome == MotionHostOutcome.TimedOut && session.FailureReason == MotionHostError.TimedOut : outcome == MotionHostOutcome.Failed);
                Console.WriteLine($"  real scene {(hang ? "UI-hang" : "crash")}: PID={host.Id}, HWND=0x{window:X}, result={outcome}, cleanup={elapsed.ElapsedMilliseconds}ms");
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")} F09 actual renderer {(hang ? "hung UI is killed by live parent deadline without Stop" : "crash returns structured failure and exits")}");
                if (!passed) failures++;
            }
            finally
            {
                if (thread is not null) { if (suspended && host is { HasExited: false }) _ = ResumeThread(thread); thread.Dispose(); }
                session.DisposeAsync().AsTask().GetAwaiter().GetResult(); host?.Dispose();
            }
        }
        return failures;
    }

    private static async Task<int> CoordinatorAsync(string hostPath, ScenePlaybackDefinition playback)
    {
        int failures = 0;
        void Check(string name, bool passed) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")} F09 actual coordinator {name}"); if (!passed) failures++; }
        var tracked = new TrackedHostFactory(hostPath);
        try
        {
            await using var coordinator = new PreviewCoordinator(new MotionHostPreviewSessionFactory(tracked));
            Task<PreviewResult>? previous = null;
            for (int index = 0; index < 3; index++)
            {
                Guid requestId = Guid.NewGuid();
                Task<PreviewResult> pending = coordinator.PlayAsync(new(requestId, playback, SceneDisplayMode.Centered));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                while (coordinator.Current.RequestId != requestId || coordinator.Current.Phase != PreviewPhase.Playing)
                {
                    if (pending.IsCompleted) throw new InvalidOperationException($"Actual coordinator did not play: {(await pending).Error}");
                    await Task.Delay(5, timeout.Token);
                }
                Process current = tracked.Processes[^1]; nint window = FindHostWindow(current.Id);
                Console.WriteLine($"  coordinated preview{index}: request={requestId:N}, PID={current.Id}, HWND=0x{window:X}");
                Check($"request{index} has its own visible scene window", window != 0 && (previous is null || (await previous).Outcome == PreviewOutcome.Superseded));
                previous = pending;
            }
            await coordinator.StopAsync();
            Check("replacement waits for old process exit and Stop cleans the last", tracked.PreviousExited && tracked.Processes.Count == 3 && tracked.Processes.All(process => process.WaitForExit(1000)) && (await previous!).Outcome == PreviewOutcome.Stopped && coordinator.Current.Phase == PreviewPhase.Stopped);
            await coordinator.DisposeAsync();
            Check("owner close rejects another host", (await coordinator.PlayAsync(new(Guid.NewGuid(), playback, SceneDisplayMode.Centered))).Outcome == PreviewOutcome.OwnerClosed && tracked.Processes.Count == 3);
        }
        finally { foreach (Process process in tracked.Processes) process.Dispose(); }
        return failures;
    }

    private sealed class TrackedHostFactory(string path) : IMotionHostSessionFactory
    {
        private readonly WindowsMotionHostProcessFactory _factory = new(path);
        internal readonly List<Process> Processes = [];
        internal bool PreviousExited = true;
        public IMotionHostSession Start(MotionHostStartRequest request)
        {
            PreviousExited &= Processes.All(process => process.HasExited);
            if (request.Mode != MotionHostMode.Preview) throw new InvalidOperationException("Preview adapter granted launch mode");
            IMotionHostSession session = _factory.Start(request);
            try
            {
                Process process = Process.GetProcessById(session.HostProcessId!.Value); _ = process.SafeHandle;
                Processes.Add(process); return session;
            }
            catch { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
        }
    }

    private static nint FindHostWindow(int processId)
    {
        nint found = 0;
        EnumWindows((window, _) => { GetWindowThreadProcessId(window, out uint owner); if (owner == (uint)processId && IsWindowVisible(window)) found = window; return found == 0; }, 0);
        return found;
    }
    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint value, [MarshalAs(UnmanagedType.Bool)] out bool setting, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern Microsoft.Win32.SafeHandles.SafeWaitHandle OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SuspendThread(Microsoft.Win32.SafeHandles.SafeWaitHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(Microsoft.Win32.SafeHandles.SafeWaitHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetProcessIdOfThread(Microsoft.Win32.SafeHandles.SafeWaitHandle thread);
}
