using System.Windows;
using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Shortcuts;

namespace DesktopMotionStudio.Studio;

public partial class MainWindow
{
    private readonly TestLaunchClient? _testLaunch;
    private CancellationTokenSource? _testLaunchWait;

    private bool CanTestLaunch => _testLaunch is not null && _testLaunchWait is null &&
        !_busy && !_closing && _readable && Selected is { TargetAvailable: true } row &&
        row.Binding is
        {
            Binding: not null, Status: BindingResolutionStatus.Ready or
            BindingResolutionStatus.Disabled or BindingResolutionStatus.SceneUnavailable
        };

    private void RefreshTestLaunchDetails(ApplicationRow? row)
    {
        TestLaunchStatusText.Text = row switch
        {
            null => "",
            { Binding.Binding: null } => "绑定动效后可测试启动。",
            { TargetAvailable: false } => "请先确认启动入口。",
            { Binding.Status: BindingResolutionStatus.StorageError } => "无法读取绑定，请刷新后重试。",
            _ => ""
        };
    }

    private async void OnTestLaunch(object sender, RoutedEventArgs e)
    {
        if (!CanTestLaunch || Selected?.Binding.Binding?.BindingId is not string bindingId) return;

        int selection = _detailsVersion;
        using var wait = new CancellationTokenSource();
        _testLaunchWait = wait;
        TestLaunchStatusText.Text = "正在测试启动…";
        UpdateEnabled();
        try
        {
            TestLaunchResult result = await _testLaunch!.LaunchAsync(bindingId, wait.Token);
            if (!_closed && !_closing && selection == _detailsVersion)
                TestLaunchStatusText.Text = TestLaunchText(result);
        }
        catch (Exception)
        {
            if (!_closed && !_closing && selection == _detailsVersion)
                TestLaunchStatusText.Text = "测试启动结果未确认，请检查目标应用。";
        }
        finally
        {
            _testLaunchWait = null;
            if (!_closed && !_closing) UpdateEnabled();
        }
    }

    private static string TestLaunchText(TestLaunchResult result) => result.Status switch
    {
        TestLaunchClientStatus.ComponentUnavailable => "启动组件不可用。请修复或重新安装 Desktop Motion Studio。",
        TestLaunchClientStatus.TimedOut => "等待启动结果超时，请检查目标应用是否已打开。",
        TestLaunchClientStatus.Detached => "",
        _ => result.Outcome switch
        {
            TestLaunchOutcome.Opened => "已打开目标应用。",
            TestLaunchOutcome.OpenedWithoutMotion => "已打开目标应用，未播放动效。",
            TestLaunchOutcome.ExistingWindow => "目标已运行，已尝试切换到它的窗口。",
            TestLaunchOutcome.BindingUnavailable => "绑定已改变，请刷新并重新确认绑定。",
            TestLaunchOutcome.TargetUnavailable => "启动入口不可用，请重新确认入口。",
            TestLaunchOutcome.ConfigurationUnavailable => "无法读取配置，请刷新后检查配置状态。",
            TestLaunchOutcome.TimedOut => "未在等待时间内确认目标窗口，请检查应用是否已打开。",
            TestLaunchOutcome.Busy => "同一个应用正在启动，请稍后重试。",
            TestLaunchOutcome.ComponentUnavailable => "启动组件不可用。请修复或重新安装 Desktop Motion Studio。",
            _ => "未能确认目标应用已打开，请检查启动入口。"
        }
    };
}
