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
            { Binding.Binding: null } => ProductText.Get("BindAWorkToTestLaunch"),
            { TargetAvailable: false } => ProductText.Get("ConfirmTheLaunchEntryFirst"),
            { Binding.Status: BindingResolutionStatus.StorageError } => ProductText.Get("CouldNotReadTheBindingRefreshAndTry"),
            _ => ""
        };
    }

    private async void OnTestLaunch(object sender, RoutedEventArgs e)
    {
        if (!CanTestLaunch || Selected?.Binding.Binding?.BindingId is not string bindingId) return;

        int selection = _detailsVersion;
        using var wait = new CancellationTokenSource();
        _testLaunchWait = wait;
        TestLaunchStatusText.Text = ProductText.Get("TestingLaunch");
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
                TestLaunchStatusText.Text = ProductText.Get("CouldNotConfirmTheResultCheckTheTarget");
        }
        finally
        {
            _testLaunchWait = null;
            if (!_closed && !_closing) UpdateEnabled();
        }
    }

    private static string TestLaunchText(TestLaunchResult result) => result.Status switch
    {
        TestLaunchClientStatus.ComponentUnavailable => ProductText.Get("TheLauncherIsUnavailableRepairOrReinstallDesktop"),
        TestLaunchClientStatus.TimedOut => ProductText.Get("TheTestTimedOutCheckWhetherTheTarget"),
        TestLaunchClientStatus.Detached => "",
        _ => result.Outcome switch
        {
            TestLaunchOutcome.Opened => ProductText.Get("TargetAppOpened"),
            TestLaunchOutcome.OpenedWithoutMotion => ProductText.Get("TargetAppOpenedWithoutMotion"),
            TestLaunchOutcome.ExistingWindow => ProductText.Get("TheAppWasAlreadyRunningStudioTriedTo"),
            TestLaunchOutcome.BindingUnavailable => ProductText.Get("TheBindingChangedRefreshAndConfirmTheBinding"),
            TestLaunchOutcome.TargetUnavailable => ProductText.Get("TheLaunchEntryIsUnavailableConfirmItAgain"),
            TestLaunchOutcome.ConfigurationUnavailable => ProductText.Get("CouldNotReadTheConfigurationRefreshAndCheck"),
            TestLaunchOutcome.TimedOut => ProductText.Get("NoTargetWindowWasConfirmedInTimeCheck"),
            TestLaunchOutcome.Busy => ProductText.Get("ThisAppIsAlreadyStartingTryAgainShortly"),
            TestLaunchOutcome.ComponentUnavailable => ProductText.Get("TheLauncherIsUnavailableRepairOrReinstallDesktop"),
            _ => ProductText.Get("CouldNotConfirmThatTheTargetAppOpened")
        }
    };
}
