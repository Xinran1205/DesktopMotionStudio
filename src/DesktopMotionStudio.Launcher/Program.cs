using System.Runtime.InteropServices;
using DesktopMotionStudio.Core.Instances;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => Run(args, StudioStateRepository.ForCurrentUser, ReportLaunchError);

    internal static int Run(string[] args, Func<IStudioStateStore> openState, Func<string, int, int> reportError,
        Action<ManagedLaunchPlan, InstanceResult, MotionHostLifecycleResult?>? observe = null)
    {
        if (ManagedLaunchRequest.ParseArguments(args) is not string bindingId) return 2;

        try
        {
            string bundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            var requests = new ManagedLaunchRequest(openState(), Path.Combine(bundle, "Scenes"));
            ManagedLaunchPlan plan = requests.Resolve(bindingId);
            if (plan is not { Status: ManagedLaunchStatus.Ready, AppId: not null, Target: not null })
                return reportError(plan.Status switch
                {
                    ManagedLaunchStatus.StorageError => "无法读取应用配置。请打开 Desktop Motion Studio 检查配置状态。",
                    ManagedLaunchStatus.BindingMissing => "找不到此快捷方式的绑定。请打开 Desktop Motion Studio 检查桌面入口。",
                    ManagedLaunchStatus.RecursiveTarget => "此快捷方式的目标指向 Desktop Motion Studio 的组件。",
                    _ => "无法打开已绑定的应用。请在 Desktop Motion Studio 中检查应用路径。"
                }, 3);

            var launcher = new ApplicationLauncher();
            var instances = new ApplicationInstanceCoordinator(new WindowsWindowPlatform(), target =>
                target is ResolvedExecutableLaunch exe && ManagedLaunchRequest.IsProductExecutable(exe.ExecutableIdentity.Path)
                    ? new LaunchResult(LaunchStatus.InvalidTarget)
                    : launcher.Launch(target));

            InstanceResult result;
            MotionHostLifecycleResult? animated = null;
            if (plan.Scene is not null && plan.Playback is not null)
            {
                var hosts = new WindowsMotionHostProcessFactory(Path.Combine(bundle, "MotionHost", "DesktopMotionStudio.MotionHost.exe"));
                var lifecycle = new MotionHostLifecycleCoordinator(instances, hosts);
                animated = lifecycle.OpenAsync(plan.AppId, plan.Target, plan.Scene, playback: plan.Playback).GetAwaiter().GetResult();
                result = animated.TargetResult;
            }
            else
            {
                result = instances.OpenAsync(plan.AppId, plan.Target).GetAwaiter().GetResult();
            }

            observe?.Invoke(plan, result, animated);
            return result.Outcome is InstanceOutcome.WindowReady or InstanceOutcome.ActivatedExisting or InstanceOutcome.ForegroundDenied ||
                   result.Process is not null && result.Outcome is InstanceOutcome.NoUsableWindow or InstanceOutcome.TimedOut
                ? 0
                : reportError("未能确认应用已打开。请检查目标应用或使用其原有入口。", 4);
        }
        catch (Exception)
        {
            // Dispatch may already have happened. Report the failure without a second launch attempt.
            return reportError("启动过程中出现错误。请检查目标应用或使用其原有入口。", 4);
        }
    }

    private static int ReportLaunchError(string text, int exitCode)
    {
        if (Environment.UserInteractive) MessageBoxW(0, text, "Desktop Motion Studio", 0x10);
        return exitCode;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
