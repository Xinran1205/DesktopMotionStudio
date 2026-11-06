using System.Text.Json;
using DesktopMotionStudio.Core.Diagnostics;
using DesktopMotionStudio.Core.Instances;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Launcher;

// Uses the production entry implementation with an explicit isolated fixture store.
internal static class FixtureProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--diagnostic-mapping-check"]) return DiagnosticMappingCheck();
        string? profile = Environment.GetEnvironmentVariable("DMS_F10_FIXTURE_STATE");
        if (profile is null || !Path.IsPathFullyQualified(profile) || !File.Exists(profile)) return 2;
        Action<DiagnosticEvent[]>? diagnostics = Environment.GetEnvironmentVariable("DMS_F11_FIXTURE_LOG_MODE") switch
        {
            "record" => events => new DiagnosticLogStore(Path.Combine(Path.GetDirectoryName(profile)!, "logs")).Append(events),
            "throw" => _ => throw new IOException("Injected diagnostic writer failure."),
            _ => null
        };
        return Program.Run(args, () => new StudioStateRepository(profile), (_, code) => code,
            (plan, instance, host) => File.WriteAllText(profile + ".launch-trace", JsonSerializer.Serialize(new
            {
                Scene = plan.Scene?.SceneId,
                Instance = instance.Outcome.ToString(),
                TargetPid = instance.Process?.ProcessId,
                Window = (long)(instance.WindowHandle ?? 0),
                Host = host?.HostOutcome.ToString(),
                HostError = host?.HostError?.ToString()
            })), diagnostics);
    }

    private static int DiagnosticMappingCheck()
    {
        DiagnosticEvent[] captured = [];
        int result = Program.Run(["--binding", "private fixture text", "--invalid"],
            () => throw new InvalidOperationException("Malformed invocation must not read profile."),
            (_, code) => code, diagnostics: events => captured = events);
        bool invalid = result == 2 && captured is [{ Stage: DiagnosticStage.Request, Result: DiagnosticResultCode.InvalidRequest }] &&
            !DiagnosticReport.Format(captured).Contains("private fixture text", StringComparison.Ordinal);
        var plan = new ManagedLaunchPlan(ManagedLaunchStatus.Ready);
        var target = new InstanceResult(InstanceOutcome.WindowReady);
        bool cleanup = Program.AnimationResult(plan, new(target, MotionHostOutcome.HandedOff, MotionHostError.RendererFailed)) ==
            (DiagnosticResultCode.HandedOff, DiagnosticFallback.RendererCleanupFailed);
        bool timeout = Program.AnimationResult(plan, new(target, MotionHostOutcome.Failed, MotionHostError.TimedOut)) ==
            (DiagnosticResultCode.TimedOut, DiagnosticFallback.RendererFailed);
        Console.WriteLine($"{(invalid ? "PASS" : "FAIL")} malformed invocation emits only typed request result");
        Console.WriteLine($"{(cleanup ? "PASS" : "FAIL")} successful handoff retains cleanup failure");
        Console.WriteLine($"{(timeout ? "PASS" : "FAIL")} renderer timeout retains exact result");
        return invalid && cleanup && timeout ? 0 : 1;
    }
}
