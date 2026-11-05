using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.Preview;

internal static class F09PlaybackProcessChecks
{
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
}
