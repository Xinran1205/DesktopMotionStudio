using System.Runtime.InteropServices;
using System.Diagnostics;
using DesktopMotionStudio.Core.Diagnostics;
using DesktopMotionStudio.Core.Instances;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.Video;

namespace DesktopMotionStudio.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string profile = StudioStateRepository.CurrentUserPath();
        ProductText.UseLanguage(new StudioLanguagePreference(profile).Read());
        return Run(args, StudioStateRepository.ForCurrentUser, ReportLaunchError,
            diagnostics: static events => new DiagnosticLogStore(DiagnosticLogStore.CurrentUserDirectory()).Append(events),
            display: new MotionDisplayPreference(profile), imports: ImportedVideoStore.ForProfile(profile));
    }

    internal static int Run(string[] args, Func<IStudioStateStore> openState, Func<string, int, int> reportError,
        Action<ManagedLaunchPlan, InstanceResult, MotionHostLifecycleResult?>? observe = null,
        Action<DiagnosticEvent[]>? diagnostics = null, MotionDisplayPreference? display = null, ImportedVideoStore? imports = null)
    {
        bool test = args.Length > 0 && args[0] == TestLaunchProtocol.Command;
        if (!test && (args.Length == 0 || args[0] != "--binding")) return 2;
        var elapsed = Stopwatch.StartNew();
        Guid request = Guid.NewGuid();
        DiagnosticAdapter adapter = DiagnosticAdapter.Unknown;
        DiagnosticStage stage = DiagnosticStage.Request;
        string? bindingId = test ? TestLaunchProtocol.ParseArguments(args) : ManagedLaunchRequest.ParseArguments(args);
        if (bindingId is null)
        {
            Record(diagnostics, request, elapsed, adapter, (stage, DiagnosticResultCode.InvalidRequest, DiagnosticFallback.None));
            return 2;
        }
        try
        {
            string bundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            var requests = new ManagedLaunchRequest(openState(), Path.Combine(bundle, "Scenes"), imports);
            ManagedLaunchPlan plan = requests.Resolve(bindingId);
            if (plan is not { Status: ManagedLaunchStatus.Ready, AppId: not null, Target: not null })
            {
                Record(diagnostics, request, elapsed, adapter, (stage, RequestResult(plan.Status), DiagnosticFallback.None));
                if (test) return (int)(plan.Status switch
                {
                    ManagedLaunchStatus.StorageError => TestLaunchOutcome.ConfigurationUnavailable,
                    ManagedLaunchStatus.BindingMissing => TestLaunchOutcome.BindingUnavailable,
                    _ => TestLaunchOutcome.TargetUnavailable
                });
                return reportError(ProductText.Get(plan.Status switch
                {
                    ManagedLaunchStatus.StorageError => "LauncherConfigurationUnavailable",
                    ManagedLaunchStatus.BindingMissing => "LauncherBindingMissing",
                    ManagedLaunchStatus.RecursiveTarget => "LauncherRecursiveTarget",
                    _ => "LauncherTargetUnavailable"
                }), 3);
            }

            stage = DiagnosticStage.Target;
            adapter = plan.Target is PackagedAppTarget ? DiagnosticAdapter.Packaged : DiagnosticAdapter.Executable;

            var launcher = new ApplicationLauncher();
            var instances = new ApplicationInstanceCoordinator(new WindowsWindowPlatform(), target =>
                target is ResolvedExecutableLaunch exe && ManagedLaunchRequest.IsProductExecutable(exe.ExecutableIdentity.Path)
                    ? new LaunchResult(LaunchStatus.InvalidTarget)
                    : launcher.Launch(target));

            InstanceResult result;
            MotionHostLifecycleResult? animated = null;
            if (plan.Scene is not null && plan.Video is { } clip)
            {
                var videos = new VideoPlaybackSessionFactory(Path.Combine(bundle, "MotionHost", VideoPlaybackRequest.WorkerName), display);
                animated = videos.OpenAfterPlaybackAsync(instances, plan.AppId, plan.Target, clip, plan.Scene.DisplayMode).GetAwaiter().GetResult();
                result = animated.TargetResult;
            }
            else if (plan.Scene is not null && plan.Playback is not null)
            {
                var hosts = new WindowsMotionHostProcessFactory(
                    Path.Combine(bundle, "MotionHost", "DesktopMotionStudio.MotionHost.exe"), ProductText.Language, display);
                var lifecycle = new MotionHostLifecycleCoordinator(instances, hosts);
                animated = lifecycle.OpenAfterPlaybackAsync(plan.AppId, plan.Target, plan.Scene, plan.Playback).GetAwaiter().GetResult();
                result = animated.TargetResult;
            }
            else
            {
                result = instances.OpenAsync(plan.AppId, plan.Target).GetAwaiter().GetResult();
            }

            var animation = AnimationResult(plan, animated);
            Record(diagnostics, request, elapsed, adapter,
                (DiagnosticStage.Target, TargetResult(result.Outcome), DiagnosticFallback.None),
                (DiagnosticStage.Animation, animation.Result, animation.Fallback));
            try { observe?.Invoke(plan, result, animated); }
            catch (Exception) { }
            if (test) return (int)(result.Outcome switch
            {
                InstanceOutcome.WindowReady => animation.Fallback is DiagnosticFallback.None or DiagnosticFallback.RendererCleanupFailed
                    ? TestLaunchOutcome.Opened : TestLaunchOutcome.OpenedWithoutMotion,
                InstanceOutcome.ActivatedExisting or InstanceOutcome.ForegroundDenied => TestLaunchOutcome.ExistingWindow,
                InstanceOutcome.TimedOut or InstanceOutcome.NoUsableWindow => TestLaunchOutcome.TimedOut,
                InstanceOutcome.GateTimedOut => TestLaunchOutcome.Busy,
                InstanceOutcome.InvalidTarget => TestLaunchOutcome.TargetUnavailable,
                _ => TestLaunchOutcome.Failed
            });
            return result.Outcome is InstanceOutcome.WindowReady or InstanceOutcome.ActivatedExisting or InstanceOutcome.ForegroundDenied ||
                   result.Process is not null && result.Outcome is InstanceOutcome.NoUsableWindow or InstanceOutcome.TimedOut
                ? 0
                : reportError(ProductText.Get("LauncherOpenUnconfirmed"), 4);
        }
        catch (Exception)
        {
            Record(diagnostics, request, elapsed, adapter, (stage, DiagnosticResultCode.UnexpectedFailure, DiagnosticFallback.None));
            // Dispatch may already have happened. Report the failure without a second launch attempt.
            return test ? (int)TestLaunchOutcome.Failed :
                reportError(ProductText.Get("LauncherUnexpectedError"), 4);
        }
    }

    private static DiagnosticResultCode RequestResult(ManagedLaunchStatus status) => status switch
    {
        ManagedLaunchStatus.StorageError => DiagnosticResultCode.ConfigurationUnavailable,
        ManagedLaunchStatus.BindingMissing => DiagnosticResultCode.BindingMissing,
        ManagedLaunchStatus.TargetUnavailable => DiagnosticResultCode.TargetUnavailable,
        ManagedLaunchStatus.RecursiveTarget => DiagnosticResultCode.RecursiveTarget,
        _ => DiagnosticResultCode.InvalidRequest
    };

    private static DiagnosticResultCode TargetResult(InstanceOutcome outcome) => outcome switch
    {
        InstanceOutcome.ActivatedExisting => DiagnosticResultCode.Existing,
        InstanceOutcome.WindowReady => DiagnosticResultCode.WindowReady,
        InstanceOutcome.ForegroundDenied => DiagnosticResultCode.ForegroundDenied,
        InstanceOutcome.NoUsableWindow => DiagnosticResultCode.NoUsableWindow,
        InstanceOutcome.AmbiguousWindow => DiagnosticResultCode.AmbiguousWindow,
        InstanceOutcome.TimedOut => DiagnosticResultCode.TimedOut,
        InstanceOutcome.ProcessExited => DiagnosticResultCode.ProcessExited,
        InstanceOutcome.IdentityChanged => DiagnosticResultCode.IdentityChanged,
        InstanceOutcome.IdentityUnavailable => DiagnosticResultCode.IdentityUnavailable,
        InstanceOutcome.InvalidTarget => DiagnosticResultCode.TargetUnavailable,
        InstanceOutcome.LaunchFailed => DiagnosticResultCode.LaunchFailed,
        InstanceOutcome.GateTimedOut => DiagnosticResultCode.Busy,
        InstanceOutcome.Canceled => DiagnosticResultCode.Canceled,
        _ => DiagnosticResultCode.UnexpectedFailure
    };

    internal static (DiagnosticResultCode Result, DiagnosticFallback Fallback) AnimationResult(
        ManagedLaunchPlan plan, MotionHostLifecycleResult? animated)
    {
        if (animated is null)
            return (DiagnosticResultCode.NotStarted, plan.PlaybackStatus == ManagedPlaybackStatus.Disabled
                ? DiagnosticFallback.AnimationDisabled : DiagnosticFallback.PlaybackUnavailable);
        if (animated.HostError == MotionHostError.TimedOut)
            return (DiagnosticResultCode.TimedOut, DiagnosticFallback.RendererFailed);
        DiagnosticFallback cleanup = animated.HostError is not (null or MotionHostError.None)
            ? DiagnosticFallback.RendererCleanupFailed : DiagnosticFallback.None;
        return animated.HostOutcome switch
        {
            MotionHostOutcome.HandedOff => (DiagnosticResultCode.HandedOff, cleanup),
            MotionHostOutcome.Skipped => (DiagnosticResultCode.Skipped, cleanup),
            MotionHostOutcome.Exited => (DiagnosticResultCode.Exited, cleanup),
            MotionHostOutcome.Completed => (DiagnosticResultCode.Completed, cleanup),
            MotionHostOutcome.NotStarted => (DiagnosticResultCode.NotStarted, DiagnosticFallback.None),
            MotionHostOutcome.TimedOut => (DiagnosticResultCode.TimedOut, DiagnosticFallback.RendererFailed),
            _ => (DiagnosticResultCode.RendererFailed, animated.HostError == MotionHostError.StartFailed
                ? DiagnosticFallback.RendererUnavailable : DiagnosticFallback.RendererFailed)
        };
    }

    private static void Record(Action<DiagnosticEvent[]>? sink, Guid request, Stopwatch elapsed, DiagnosticAdapter adapter,
        params (DiagnosticStage Stage, DiagnosticResultCode Result, DiagnosticFallback Fallback)[] outcomes)
    {
        if (sink is null) return;
        try
        {
            Version version = typeof(Program).Assembly.GetName().Version ?? new Version(0, 0);
            var product = new DiagnosticVersion(Part(version.Major), Part(version.Minor), Part(version.Build), Part(version.Revision));
            DateTimeOffset utc = DateTimeOffset.UtcNow;
            int duration = (int)Math.Min(elapsed.ElapsedMilliseconds, 120_000);
            int build = Environment.OSVersion.Version.Build;
            sink(outcomes.Select(item => new DiagnosticEvent(request, utc, product, build, adapter,
                item.Stage, item.Result, item.Fallback, duration)).ToArray());
        }
        catch (Exception) { }
    }

    private static ushort Part(int value) => (ushort)Math.Clamp(value, 0, ushort.MaxValue);

    private static int ReportLaunchError(string text, int exitCode)
    {
        if (Environment.UserInteractive) MessageBoxW(0, text, "Desktop Motion Studio", 0x10);
        return exitCode;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
