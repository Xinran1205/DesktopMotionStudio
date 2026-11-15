using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Mp4MotionProbe;

internal static class ProbeVerification
{
    internal static int Run(string fixtureDirectory, string targetPath)
    {
        if (!Path.IsPathFullyQualified(fixtureDirectory) || !Directory.Exists(fixtureDirectory) ||
            !Path.IsPathFullyQualified(targetPath) || Path.GetFileName(targetPath) != "DesktopMotionStudio.WindowHost.Poc.exe" ||
            !File.Exists(targetPath)) return 2;
        string probe = Environment.ProcessPath ?? "";
        if (Path.GetFileName(probe) != "Mp4MotionProbe.exe") return 2;

        var directory = Directory.CreateTempSubdirectory("dms-f20-probe-");
        string ownedTarget = Path.Combine(directory.FullName, $"F20Target-{Guid.NewGuid():N}.exe");
        try
        {
            foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(targetPath)!))
                File.Copy(file, string.Equals(file, targetPath, StringComparison.OrdinalIgnoreCase)
                    ? ownedTarget : Path.Combine(directory.FullName, Path.GetFileName(file)));
            foreach ((string name, string fixture, string fit, string outcome, bool hasAudio, int seconds) in new[]
            {
                ("landscape", "landscape.mp4", "fit", "completed", true, 6),
                ("portrait fit", "portrait.mp4", "fit", "completed", false, 6),
                ("portrait fill", "portrait.mp4", "fill", "completed", false, 6),
                ("ten seconds", "landscape-10s.mp4", "fit", "completed", false, 10),
                ("Escape", "landscape.mp4", "fit", "skipped", true, 6),
                ("malformed", "malformed.mp4", "fit", "failed", false, 0),
                ("startup deadline", "", "fit", "timeout", false, 0)
            })
            {
                VerifyCase(probe, ownedTarget, Path.Combine(fixtureDirectory, fixture), name, fit, outcome, hasAudio, seconds);
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL {error.Message}"); return 1; }
        finally { directory.Delete(recursive: true); }
    }

    private static void VerifyCase(string probe, string target, string video, string name, string fit, string expected,
        bool hasAudio, int seconds)
    {
        string targetName = Path.GetFileNameWithoutExtension(target);
        Require(Count(targetName) == 0, "Target already exists before the scenario.");
        bool stalled = expected == "timeout";
        var start = new ProcessStartInfo(probe) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (string argument in stalled ? ["--fixture-stall"] : new[] { "--play", video, fit })
            start.ArgumentList.Add(argument);

        var clock = Stopwatch.StartNew();
        using Process player = Process.Start(start) ?? throw new InvalidOperationException("Could not start the owned video probe.");
        try
        {
            Task<string> output = player.StandardOutput.ReadToEndAsync();
            bool shouldShow = expected is "completed" or "skipped";
            if (shouldShow)
            {
                nint window = 0;
                Require(WaitFor(() => (window = Window(player.Id)) != 0 || player.HasExited, 3600) && window != 0,
                    $"{name} did not show a playback window.");
                Require(Count(targetName) == 0, $"{name} started the target during playback.");
                if (expected == "skipped")
                    Require(PostMessageW(window, 0x0100, 0x1B, 1), "Could not send Escape to the owned window.");
            }

            if (stalled)
            {
                Require(!player.WaitForExit(3000) && Window(player.Id) == 0, "Startup timeout fixture did not stall invisibly.");
                player.Kill();
                Require(player.WaitForExit(1000), "The timed-out probe did not exit.");
            }
            else
            {
                Require(player.WaitForExit(Math.Max(1, 14_000 - (int)clock.ElapsedMilliseconds)), $"{name} exceeded the parent deadline.");
                ProbeResult record = JsonSerializer.Deserialize<ProbeResult>(output.GetAwaiter().GetResult())
                    ?? throw new InvalidDataException($"{name} returned no probe record.");
                int exitCode = expected switch { "completed" => 0, "skipped" => 10, _ => 3 };
                Require(record.Outcome == expected && player.ExitCode == exitCode,
                    $"{name} expected {expected}, got {record.Outcome} ({record.Error}).");
                if (shouldShow) VerifyFrames(record, fit, expected == "completed", hasAudio, seconds);
                else Require(record.ShownMs is null && record.Frames == 0, "Malformed video opened an overlay.");
                Console.WriteLine(JsonSerializer.Serialize(record));
            }

            Require(player.HasExited && Window(player.Id) == 0 && Count(targetName) == 0,
                $"{name} retained an overlay or launched the target too early.");
            long dispatch = clock.ElapsedMilliseconds;
            using Process application = Process.Start(new ProcessStartInfo(target) { UseShellExecute = false })
                ?? throw new InvalidOperationException("Could not start the test target.");
            try
            {
                Require(WaitFor(() => { application.Refresh(); return application.MainWindowHandle != 0; }, 5000) &&
                    Count(targetName) == 1, $"{name} did not create exactly one target window.");
                Console.WriteLine($"PASS {name}: one fixture target dispatched after player exit at {dispatch} ms.");
            }
            finally { Stop(application); }
        }
        finally { Stop(player); }
    }

    private static void VerifyFrames(ProbeResult record, string fit, bool complete, bool hasAudio, int seconds)
    {
        Require(record.FirstFrameMs is long first && record.ShownMs is long shown && first <= shown && first < 3000 &&
            record.ClosedMs is long closed && closed >= shown && record.FirstFrameHasContrast &&
            record.Muted && record.HasAudio == hasAudio, "Frame readiness, audio track, mute or close evidence was invalid.");
        if (!complete) return;
        long visible = record.ClosedMs!.Value - record.ShownMs!.Value;
        Require(record.Frames >= seconds * 25 && record.FullOpacityPaints > 0 &&
            record.FadeInPaints > 0 && record.FirstPaintOpacity is <= .25 && record.MinimumFadeInOpacity < .8 &&
            record.FadeOutPaints > 0 && record.MinimumFadeOutOpacity < .5 &&
            Math.Abs(record.DurationSeconds - seconds) <= .1 && visible >= seconds * 1000 - 500 && visible <= seconds * 1000 + 800,
            "Playback frame count, fixed fades or duration was outside the expected fixture range.");
        Require(Math.Abs(record.DrawWidth / record.DrawHeight - (float)record.SourceWidth / record.SourceHeight) < .001,
            "Video composition distorted the source aspect ratio.");
        Require(fit == "fit"
            ? record.DrawWidth <= record.CanvasWidth + 1 && record.DrawHeight <= record.CanvasHeight + 1
            : record.DrawWidth >= record.CanvasWidth - 1 && record.DrawHeight >= record.CanvasHeight - 1,
            "Fit or fill did not match the actual canvas.");
        if (record.SourceWidth < record.SourceHeight)
            Require(fit == "fit" ? record.DrawWidth < record.CanvasWidth : record.DrawHeight > record.CanvasHeight,
                "Portrait fixture did not exercise letterboxing or cropping.");
    }

    private static int Count(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        foreach (Process process in processes) process.Dispose();
        return processes.Length;
    }

    private static nint Window(int pid)
    {
        nint result = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint processId);
            if (processId != pid || !IsWindowVisible(window)) return true;
            var title = new StringBuilder(128);
            GetWindowTextW(window, title, title.Capacity);
            if (title.ToString() != VideoStage.StageTitle) return true;
            result = window;
            return false;
        }, 0);
        return result;
    }

    private static bool WaitFor(Func<bool> condition, int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            if (condition()) return true;
            Thread.Sleep(20);
        } while (clock.ElapsedMilliseconds < milliseconds);
        return false;
    }

    private static void Stop(Process process)
    {
        if (process.HasExited) return;
        process.CloseMainWindow();
        if (process.WaitForExit(500)) return;
        process.Kill();
        process.WaitForExit(1000);
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }

    private delegate bool EnumWindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowTextW(nint window, StringBuilder text, int capacity);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
