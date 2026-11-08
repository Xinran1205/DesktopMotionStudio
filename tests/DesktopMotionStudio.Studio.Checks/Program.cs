using System.Diagnostics;
using System.IO;
using System.Text;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Studio;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--worker") return Worker(args[1], args[2]);
        if (args is ["--discovery-worker"]) return DiscoveryWorker.Run();
        if (args is ["--ui"]) return StudioUiChecks.Run();
        if (args is ["--f09-ui"]) return F09UiChecks.Run();
        if (args is ["--f13-preview-ui"]) return F09UiChecks.RunTechPreview();
        if (args is ["--f10-ui"]) return F10UiChecks.Run();
        if (args is ["--f13-launch-ui", var delayedTarget]) return F10UiChecks.RunTechLaunch(delayedTarget);
        if (args is ["--f13-legacy-launch-ui", var legacyTarget]) return F10UiChecks.RunTechLaunch(legacyTarget, legacy: true);
        if (args is ["--f16-home-layout", var layoutOutput]) return F16HomeChecks.Run(layoutOutput);
        if (args is ["--f17-language-ui"]) return F17LanguageChecks.Run();
        if (args is ["--f18-display-ui"]) return F18DisplayChecks.Run();
        if (args is ["--f19-playback-order", var oldRenderer]) return F19PlaybackChecks.Run(oldRenderer);
        if (args is ["--f11-launch-ui"]) return F10UiChecks.Run(diagnostics: true);
        if (args is ["--f11-ui"]) return F10UiChecks.Run(diagnostics: true, settingsAndRecovery: true);
        if (args is ["--f15-native", var fixture, var pid, var executable] && int.TryParse(pid, out int ownedPid))
            return F15NativeChecks.Run(fixture, ownedPid, executable);
        if (args is ["--preview-races"]) { int previewFailures = 0; PreviewRaceChecks.Run(ref previewFailures); return previewFailures; }
        if (args.Length != 0) return 2;
        string root = Path.Combine(Path.GetTempPath(), $"dms-discovery-checks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        int failures = 0;
        try
        {
            string capRoot = Path.Combine(root, "cap");
            Directory.CreateDirectory(capRoot);
            for (int i = 0; i < 2000; i++) File.WriteAllBytes(Path.Combine(capRoot, $"candidate-{i}.lnk"), []);
            Record("worker accepts exactly 2000 entries", DiscoveryWorker.Scan([capRoot], includePackaged: false) is { Status: ApplicationDiscoveryStatus.Complete, Candidates.Count: 2000 }, ref failures);
            File.WriteAllBytes(Path.Combine(capRoot, "over-limit.lnk"), []);
            Record("worker reports failure rather than truncated success on candidate overflow", DiscoveryWorker.Scan([capRoot], includePackaged: false) is { Status: ApplicationDiscoveryStatus.Failed, Candidates.Count: 0 }, ref failures);
            foreach (string mode in new[] { "valid", "malformed", "oversized", "error" })
            {
                var client = new DiscoveryClient(() => CreateWorker(mode, string.Empty));
                ApplicationDiscoveryResult result = client.ScanAsync(CancellationToken.None).GetAwaiter().GetResult();
                Record($"discovery process {mode} output classified", result.Status == (mode == "valid" ? ApplicationDiscoveryStatus.Empty : ApplicationDiscoveryStatus.Failed), ref failures);
            }
            string pidPath = Path.Combine(root, "timeout-pid");
            var timed = new DiscoveryClient(() => CreateWorker("hang", pidPath), TimeSpan.FromMilliseconds(700));
            var timer = Stopwatch.StartNew();
            ApplicationDiscoveryResult timeout = timed.ScanAsync(CancellationToken.None).GetAwaiter().GetResult();
            Record("discovery timeout terminates only its owned worker within bounded cleanup",
                timeout.Status == ApplicationDiscoveryStatus.TimedOut && timer.Elapsed < TimeSpan.FromSeconds(2) && Exited(pidPath), ref failures);
            pidPath = Path.Combine(root, "cancel-pid");
            using var cancellation = new CancellationTokenSource();
            Task<ApplicationDiscoveryResult> pending = new DiscoveryClient(() => CreateWorker("hang", pidPath)).ScanAsync(cancellation.Token);
            if (!WaitFor(() => File.Exists(pidPath), TimeSpan.FromSeconds(2))) throw new InvalidOperationException("Cancel fixture did not start.");
            cancellation.Cancel();
            ApplicationDiscoveryResult cancelled = pending.GetAwaiter().GetResult();
            Record("discovery cancellation cleans up the owned worker", cancelled.Status == ApplicationDiscoveryStatus.Cancelled && Exited(pidPath), ref failures);
            bool factoryCalled = false;
            var precancelled = new DiscoveryClient(() => { factoryCalled = true; return CreateWorker("valid", string.Empty); });
            Record("pre-cancelled discovery does not start a process",
                precancelled.ScanAsync(new CancellationToken(true)).GetAwaiter().GetResult().Status == ApplicationDiscoveryStatus.Cancelled && !factoryCalled, ref failures);

            string? studio = Environment.GetEnvironmentVariable("DMS_F08_STUDIO_PATH");
            if (!string.IsNullOrWhiteSpace(studio) && File.Exists(studio))
            {
                var client = new DiscoveryClient(() =>
                {
                    var start = new ProcessStartInfo(studio);
                    start.ArgumentList.Add("--discovery-worker");
                    return start;
                });
                ApplicationDiscoveryResult actual = client.ScanAsync(CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"  real discovery: {actual.Status}, {actual.Candidates.Count} candidates, {actual.Candidates.Count(item => item.Target.Kind == ApplicationTargetKind.Packaged)} packaged");
                Record("production GUI executable completes bounded discovery protocol", actual.Status is ApplicationDiscoveryStatus.Complete or ApplicationDiscoveryStatus.Empty, ref failures);
            }
            else Console.WriteLine("SKIP real discovery (DMS_F08_STUDIO_PATH unset)");
            PreviewRaceChecks.Run(ref failures);
            PreviewIntentChecks.Run(ref failures);
            WpfRaceChecks.Run(ref failures);
        }
        catch (Exception ex) { Console.WriteLine($"FAIL discovery checks: {ex.GetType().Name}: {ex.Message}"); failures++; }
        finally { Directory.Delete(root, recursive: true); }
        return failures == 0 ? 0 : 1;
    }

    private static int Worker(string mode, string pidPath)
    {
        if (pidPath.Length > 0) File.WriteAllText(pidPath, Environment.ProcessId.ToString());
        if (mode == "hang") { Thread.Sleep(10_000); return 0; }
        if (mode == "error") return 7;
        byte[] bytes = mode switch
        {
            "valid" => ApplicationDiscoveryProtocol.Encode(new(ApplicationDiscoveryStatus.Empty, [])),
            "oversized" => new byte[ApplicationDiscoveryProtocol.MaximumBytes + 1],
            _ => Encoding.UTF8.GetBytes("{")
        };
        using Stream stdout = Console.OpenStandardOutput();
        stdout.Write(bytes);
        return 0;
    }

    private static ProcessStartInfo CreateWorker(string mode, string pidPath)
    {
        string executable = Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable);
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--worker"); start.ArgumentList.Add(mode); start.ArgumentList.Add(pidPath);
        return start;
    }

    private static bool Exited(string pidPath)
    {
        if (!File.Exists(pidPath)) return false;
        int id = int.Parse(File.ReadAllText(pidPath), System.Globalization.CultureInfo.InvariantCulture);
        try { using Process process = Process.GetProcessById(id); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }

    internal static bool WaitFor(Func<bool> condition, TimeSpan? timeout = null)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < (timeout ?? TimeSpan.FromSeconds(5)))
        {
            try { if (condition()) return true; }
            catch (Exception ex) when (ex is InvalidOperationException or System.Windows.Automation.ElementNotAvailableException) { }
            Thread.Sleep(30);
        }
        return false;
    }

    internal static void Record(string name, bool passed, ref int failures)
    {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
        if (!passed) failures++;
    }
}
