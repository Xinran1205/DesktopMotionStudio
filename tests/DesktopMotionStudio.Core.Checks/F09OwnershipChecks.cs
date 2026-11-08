using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;

internal static class F09OwnershipChecks
{
    private static string Harness => Path.Combine(AppContext.BaseDirectory, "DesktopMotionStudio.Core.Checks.exe");

    internal static int Run()
    {
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP F09 Windows ownership checks"); return 0; }
        int failures = 0;
        string directory = Path.Combine(Path.GetTempPath(), $"dms-f09-ownership-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string? oldBehavior = Environment.GetEnvironmentVariable("DMS_F09_HOST_FIXTURE");
        string? oldMarker = Environment.GetEnvironmentVariable("DMS_F09_HOST_MARKER");
        void Check(string name, bool passed) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")} F09 {name}"); if (!passed) failures++; }
        try
        {
            using var outside = Process.Start(new ProcessStartInfo(Harness, "--f09-unassigned-worker") { UseShellExecute = false, CreateNoWindow = true })!;
            try
            {
                Guid request = Guid.NewGuid();
                using (OwnedMotionHostProcess owned = OwnedMotionHostProcess.Start(Harness, $"dms-motionhost-{request:N}", request,
                    suspended => Check("native child belongs to its job before resume", suspended.IsInOwnedJob && !suspended.ContainsProcess(Environment.ProcessId))))
                {
                    Check("unassigned target fixture is outside renderer job", !owned.ContainsProcess(outside.Id));
                    owned.Terminate();
                    Check("closing job kills resumed owned process only", owned.Process.WaitForExit(1000) && !outside.HasExited);
                }

                Process? failedChild = null;
                bool startRejected = false;
                try
                {
                    using var rejected = OwnedMotionHostProcess.Start(Harness, $"dms-motionhost-{request:N}", request,
                        suspended => { failedChild = Process.GetProcessById(suspended.Process.Id); _ = failedChild.SafeHandle; throw new InvalidOperationException("Injected pre-resume initialization failure"); });
                }
                catch (InvalidOperationException) { startRejected = true; }
                using (failedChild) Check("failed initialization kills suspended child before returning error", startRejected && failedChild is not null && failedChild.WaitForExit(1000) && !outside.HasExited);

                Process? resumeChild = null;
                bool resumeRejected = false;
                try
                {
                    using var rejected = OwnedMotionHostProcess.Start(Harness, $"dms-motionhost-{request:N}", request,
                        suspended => { resumeChild = Process.GetProcessById(suspended.Process.Id); _ = resumeChild.SafeHandle; resumeChild.Kill(); resumeChild.WaitForExit(1000); });
                }
                catch (Win32Exception ex) { resumeRejected = ex.NativeErrorCode != 0; }
                catch (InvalidOperationException ex) { resumeRejected = ex.Message == "Unexpected renderer initial-thread suspension state."; }
                using (resumeChild) Check("native resume failure is reported with owned child already cleaned", resumeRejected && resumeChild is { HasExited: true } && !outside.HasExited);

                bool invalidImage = false;
                string invalidPath = Path.Combine(directory, "invalid.exe"); File.WriteAllText(invalidPath, "invalid executable fixture");
                try { using var invalid = OwnedMotionHostProcess.Start(invalidPath, $"dms-motionhost-{request:N}", request); }
                catch (Win32Exception ex) { invalidImage = ex.NativeErrorCode != 0; }
                Check("native creation failure is reported without affecting other process", invalidImage && !outside.HasExited);

                foreach (string phase in new[] { "early", "running" })
                {
                    string marker = Path.Combine(directory, "parent-" + phase + ".txt");
                    using Process parent = StartParent(phase, marker);
                    try
                    {
                        string[] ready = (parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult() ?? "").Split(':');
                        if (ready.Length != 3 || ready[0] != "READY") throw new InvalidOperationException("Owned parent fixture did not become ready.");
                        using Process child = Process.GetProcessById(int.Parse(ready[1], System.Globalization.CultureInfo.InvariantCulture));
                        if (child.StartTime.ToUniversalTime().Ticks != long.Parse(ready[2], System.Globalization.CultureInfo.InvariantCulture)) throw new InvalidOperationException("Child identity changed.");
                        bool reachedPhase = File.Exists(marker) == (phase == "running");
                        var elapsed = Stopwatch.StartNew(); parent.Kill();
                        bool cleaned = parent.WaitForExit(1000) && child.WaitForExit(1500);
                        Console.WriteLine($"  ownership {phase}: parent={parent.Id}, child={child.Id}, cleanup={elapsed.ElapsedMilliseconds}ms");
                        Check($"parent death cleans {phase} child without inherited ownership handle", reachedPhase && cleaned && !outside.HasExited);
                    }
                    finally { if (!parent.HasExited) { parent.Kill(); parent.WaitForExit(1000); } }
                }

                foreach (string behavior in new[] { "hang", "silent", "starting", "crash" })
                {
                    string marker = Path.Combine(directory, behavior + ".txt");
                    Environment.SetEnvironmentVariable("DMS_F09_HOST_FIXTURE", behavior);
                    Environment.SetEnvironmentVariable("DMS_F09_HOST_MARKER", marker);
                    IMotionHostSession session = new WindowsMotionHostProcessFactory(Harness).Start(new MotionHostStartRequest(Guid.NewGuid(), "studio-pulse", "1.0.0", SceneDisplayMode.Centered, 800));
                    var elapsed = Stopwatch.StartNew();
                    try
                    {
                        if (behavior is not ("silent" or "starting")) _ = session.Started.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                        MotionHostOutcome outcome = session.Completion.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                        string[] identity = File.ReadAllText(marker).Split(':');
                        bool exited = Exited(identity);
                        Console.WriteLine($"  {behavior}: child={identity[0]}, elapsed={elapsed.ElapsedMilliseconds}ms, outcome={outcome}, error={session.FailureReason}");
                        Check($"live parent cleans {behavior} without caller Stop or Dispose", exited && !outside.HasExited && elapsed.Elapsed < TimeSpan.FromSeconds(3) &&
                            (behavior == "hang" ? outcome == MotionHostOutcome.TimedOut && session.FailureReason == MotionHostError.TimedOut : outcome == MotionHostOutcome.Failed));
                    }
                    finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                }

                foreach (string phase in new[] { "silent", "starting", "hang", "dispose-hang" })
                {
                    string marker = Path.Combine(directory, "stop-" + phase + ".txt");
                    Environment.SetEnvironmentVariable("DMS_F09_HOST_FIXTURE", phase == "dispose-hang" ? "hang" : phase);
                    Environment.SetEnvironmentVariable("DMS_F09_HOST_MARKER", marker);
                    IMotionHostSession session = new WindowsMotionHostProcessFactory(Harness).Start(new(Guid.NewGuid(), "studio-pulse", "1.0.0", SceneDisplayMode.Centered));
                    try
                    {
                        if (!SpinWait.SpinUntil(() => File.Exists(marker) && new FileInfo(marker).Length > 0, 2000)) throw new TimeoutException("Stop fixture did not reach phase.");
                        if (phase is "hang" or "dispose-hang") _ = session.Started.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                        var elapsed = Stopwatch.StartNew();
                        if (phase == "dispose-hang") session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                        else _ = session.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                        Check($"stop/dispose during {phase} cleans ownership without protocol failure", session.Completion.GetAwaiter().GetResult() == MotionHostOutcome.Exited && session.FailureReason is null && Exited(File.ReadAllText(marker).Split(':')) && elapsed.Elapsed < TimeSpan.FromSeconds(2));
                    }
                    finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                }
            }
            finally { if (!outside.HasExited) { outside.Kill(); outside.WaitForExit(1000); } }
            foreach (string behavior in new[] { "starting", "complete-on-handoff", "skip-on-handoff" })
            {
                string marker = Path.Combine(directory, "f13-" + behavior + ".txt");
                Environment.SetEnvironmentVariable("DMS_F09_HOST_FIXTURE", behavior);
                Environment.SetEnvironmentVariable("DMS_F09_HOST_MARKER", marker);
                ScenePlaybackDefinition playback = F09Checks.TechPlayback();
                IMotionHostSession session = new WindowsMotionHostProcessFactory(Harness).Start(
                    new(Guid.NewGuid(), playback.SceneId, playback.SceneVersion, SceneDisplayMode.Centered, Playback: playback));
                try
                {
                    if (behavior == "starting")
                    {
                        if (!SpinWait.SpinUntil(() => File.Exists(marker) && new FileInfo(marker).Length > 0, 2000))
                            throw new TimeoutException("Finite host did not receive Start.");
                    }
                    else if (session.Started.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult() is not null)
                        throw new InvalidOperationException("Finite host did not reach Started.");
                    MotionHostOutcome expected = behavior switch
                    {
                        "starting" => MotionHostOutcome.NotStarted,
                        "complete-on-handoff" => MotionHostOutcome.Completed,
                        _ => MotionHostOutcome.Skipped
                    };
                    MotionHostOutcome outcome = session.HandoffAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                    Check($"F13 {behavior} preserves {expected}, observed {outcome}", outcome == expected && session.FailureReason is null &&
                        Exited(File.ReadAllText(marker).Split(':')));
                }
                finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }
        }
        catch (Exception ex) { Console.WriteLine($"FAIL F09 ownership: {ex.GetType().Name}: {ex.Message}"); failures++; }
        finally
        {
            Environment.SetEnvironmentVariable("DMS_F09_HOST_FIXTURE", oldBehavior);
            Environment.SetEnvironmentVariable("DMS_F09_HOST_MARKER", oldMarker);
            Directory.Delete(directory, true);
        }
        return failures;
    }

    internal static int Parent(string phase)
    {
        Guid request = Guid.NewGuid();
        using OwnedMotionHostProcess owned = OwnedMotionHostProcess.Start(Harness, $"dms-motionhost-{request:N}", request,
            phase == "early" ? ReportAndWait : null);
        if (!SpinWait.SpinUntil(() => File.Exists(Environment.GetEnvironmentVariable("DMS_F09_HOST_MARKER")), 2000)) return 3;
        ReportAndWait(owned);
        return 0;
    }

    private static void ReportAndWait(OwnedMotionHostProcess owned)
    {
        Console.WriteLine($"READY:{owned.Process.Id}:{owned.Process.StartTime.ToUniversalTime().Ticks}"); Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite);
    }

    private static Process StartParent(string phase, string marker)
    {
        var info = new ProcessStartInfo(Harness) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("--f09-owned-parent"); info.ArgumentList.Add(phase);
        info.Environment["DMS_F09_HOST_FIXTURE"] = "unconnected";
        info.Environment["DMS_F09_HOST_MARKER"] = marker;
        return Process.Start(info) ?? throw new InvalidOperationException("Parent fixture did not start.");
    }

    private static bool Exited(string[] identity)
    {
        try
        {
            using Process child = Process.GetProcessById(int.Parse(identity[0], System.Globalization.CultureInfo.InvariantCulture));
            return child.StartTime.ToUniversalTime().Ticks != long.Parse(identity[1], System.Globalization.CultureInfo.InvariantCulture) || child.WaitForExit(1000);
        }
        catch (ArgumentException) { return true; } // The owned process has already left the process table.
    }

    internal static int HostWorker(string pipeName, string requestText, string behavior)
    {
        if (!Guid.TryParseExact(requestText, "D", out Guid request) || pipeName != $"dms-motionhost-{request:N}" || request == Guid.Empty) return 2;
        void Mark()
        {
            string? marker = Environment.GetEnvironmentVariable("DMS_F09_HOST_MARKER");
            using Process current = Process.GetCurrentProcess();
            if (marker is not null) File.WriteAllText(marker, $"{Environment.ProcessId}:{current.StartTime.ToUniversalTime().Ticks}");
        }
        if (behavior == "unconnected") { Mark(); Thread.Sleep(Timeout.Infinite); return 0; }
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        pipe.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        if (behavior == "silent") { Mark(); Thread.Sleep(Timeout.Infinite); return 0; }
        MotionHostFrameCodec.WriteAsync(pipe, new(MotionHostFrame.CurrentProtocolVersion, request, MotionHostMessageKind.Ready)).GetAwaiter().GetResult();
        _ = MotionHostFrameCodec.ReadAsync(pipe).GetAwaiter().GetResult();
        Mark();
        if (behavior == "starting") { Thread.Sleep(Timeout.Infinite); return 0; }
        MotionHostFrameCodec.WriteAsync(pipe, new(MotionHostFrame.CurrentProtocolVersion, request, MotionHostMessageKind.Started)).GetAwaiter().GetResult();
        if (behavior is "complete-on-handoff" or "skip-on-handoff")
        {
            if (MotionHostFrameCodec.ReadAsync(pipe).GetAwaiter().GetResult()?.Kind != MotionHostMessageKind.Handoff) return 4;
            MotionHostMessageKind terminal = behavior == "complete-on-handoff" ? MotionHostMessageKind.Completed : MotionHostMessageKind.SkipRequested;
            MotionHostFrameCodec.WriteAsync(pipe, new(MotionHostFrame.CurrentProtocolVersion, request, terminal)).GetAwaiter().GetResult();
            return 0;
        }
        if (behavior == "crash") return 21;
        Thread.Sleep(Timeout.Infinite); // Deliberately ignores Stop and UI timers, parent remains alive.
        return 0;
    }
}
