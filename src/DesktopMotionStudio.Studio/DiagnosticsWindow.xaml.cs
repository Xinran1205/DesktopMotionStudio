using System.Diagnostics;
using System.IO;
using System.Windows;
using DesktopMotionStudio.Core.Diagnostics;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Studio;

public partial class DiagnosticsWindow : Window
{
    private readonly IStudioStateStore _state;
    private readonly ManagedLaunchRequest _requests;
    private readonly DiagnosticLogStore _logs;
    private readonly ManagedShortcutService? _shortcuts;
    private readonly string? _bindingId;
    private readonly Action<string> _copyReport;
    private DiagnosticSnapshot _snapshot = new(DiagnosticStatus.Empty, []);
    private bool _canRepair, _canOpen, _busy, _closed;
    private int _refreshVersion;
    internal bool Repaired { get; private set; }

    internal DiagnosticsWindow(IStudioStateStore state, string catalogRoot, DiagnosticLogStore logs,
        ManagedShortcutService? shortcuts, string? bindingId, Action<string>? copyReport = null)
    {
        InitializeComponent();
        _state = state;
        _requests = new(state, catalogRoot);
        _logs = logs;
        _shortcuts = shortcuts;
        _bindingId = bindingId;
        _copyReport = copyReport ?? Clipboard.SetText;
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 30));
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 30));
        Loaded += async (_, _) => await RefreshAsync();
        Closing += (_, args) => { if (_busy) args.Cancel = true; };
        Closed += (_, _) => { _closed = true; _refreshVersion++; };
    }

    internal async Task RefreshAsync()
    {
        int version = ++_refreshVersion;
        _canRepair = _canOpen = false;
        _snapshot = new(DiagnosticStatus.Empty, []);
        UpdateEnabled();
        DiagnosticStatusText.Text = ProductText.Get("LoadingDiagnostics");
        try
        {
            var result = await Task.Run(() =>
            {
                DiagnosticSnapshot snapshot = _logs.Read();
                (string entry, bool repair) = ReadRecovery();
                string launcher = ComponentVersion("Launcher");
                string host = ComponentVersion("MotionHost");
                return (snapshot, entry, repair, launcher, host,
                    folder: Directory.Exists(_logs.DirectoryPath) && LaunchTargetIdentityResolver.IsLocalPath(_logs.DirectoryPath));
            });
            if (_closed || version != _refreshVersion) return;
            _snapshot = result.snapshot;
            _canOpen = result.folder;
            _canRepair = result.repair;
            EntryStatusText.Text = result.entry;
            DiagnosticStatusText.Text = LogStatus(_snapshot);
            RecentEventsText.Text = string.Join(Environment.NewLine, _snapshot.Events.TakeLast(12).Reverse().Select(Display));
            ComponentsText.Text = $"Studio {typeof(DiagnosticsWindow).Assembly.GetName().Version} · Launcher {result.launcher} · MotionHost {result.host} · IPC {MotionHostFrame.CurrentProtocolVersion}";
            CapabilityText.Text = result.host == ProductText.Get("Unavailable") ? ProductText.Get("TheBundledRendererIsUnavailable") :
                ProductText.Format("RendererCapability", ProductText.Get(SystemParameters.ClientAreaAnimation ? "AnimationOn" : "AnimationOff"));
        }
        catch (Exception)
        {
            if (!_closed && version == _refreshVersion) DiagnosticStatusText.Text = ProductText.Get("CouldNotLoadLocalDiagnostics");
        }
        if (!_closed && version == _refreshVersion) UpdateEnabled();
    }

    private (string Status, bool CanRepair) ReadRecovery()
    {
        if (_bindingId is null || _shortcuts is null) return (ProductText.Get("SelectAnAppWithAMotionBindingTo"), false);
        RepositoryResult profile = _state.Load();
        if (!profile.IsSuccess) return (UiText.Storage(profile.Error), false);
        ApplicationBinding? binding = profile.State!.Bindings.FirstOrDefault(item =>
            string.Equals(item.BindingId, _bindingId, StringComparison.OrdinalIgnoreCase));
        if (binding is null) return (ProductText.Get("TheSelectedBindingIsNoLongerAvailable"), false);
        ApplicationProfile? app = profile.State.Applications.FirstOrDefault(item =>
            string.Equals(item.AppId, binding.AppId, StringComparison.OrdinalIgnoreCase));
        if (app is null) return (ProductText.Get("TheSelectedAppIsNoLongerAvailable"), false);
        ShortcutResult entry = _shortcuts.Inspect(binding.BindingId);
        bool eligible = entry.Status is ShortcutStatus.Missing or ShortcutStatus.RepairRequired or ShortcutStatus.OutOfScope &&
            _requests.Resolve(binding.BindingId).Playback is not null;
        return ($"{app.DisplayName} · {MainWindow.ShortcutText(entry.Status)}", eligible);
    }

    private void UpdateEnabled()
    {
        RefreshButton.IsEnabled = CloseButton.IsEnabled = !_busy;
        RepairEntryButton.IsEnabled = !_busy && _canRepair;
        OpenLogsButton.IsEnabled = !_busy && _canOpen;
        CopyReportButton.IsEnabled = !_busy && _snapshot.Status == DiagnosticStatus.Ready;
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnRepair(object sender, RoutedEventArgs e)
    {
        if (_busy || !_canRepair || _bindingId is null || _shortcuts is null) return;
        _busy = true;
        UpdateEnabled();
        RepairResultText.Text = ProductText.Get("CheckingAndRepairingTheDesktopShortcut");
        try
        {
            ShortcutResult result = await Task.Run(() => _shortcuts.Repair(_bindingId));
            if (_closed) return;
            Repaired |= result.Status == ShortcutStatus.Created;
            RepairResultText.Text = MainWindow.ShortcutText(result.Status);
            await RefreshAsync();
        }
        catch (Exception) { if (!_closed) RepairResultText.Text = ProductText.Get("CouldNotConfirmTheRepairRefreshToCheck"); }
        finally { _busy = false; if (!_closed) UpdateEnabled(); }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_busy || _snapshot.Status != DiagnosticStatus.Ready) return;
        try
        {
            _copyReport(DiagnosticReport.Format(_snapshot.Events));
            DiagnosticStatusText.Text = ProductText.Get("RedactedSummaryCopied");
        }
        catch (Exception) { DiagnosticStatusText.Text = ProductText.Get("ClipboardUnavailableTryAgain"); }
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (_busy || !_canOpen) return;
        try
        {
            if (!Directory.Exists(_logs.DirectoryPath) || !LaunchTargetIdentityResolver.IsLocalPath(_logs.DirectoryPath))
            {
                DiagnosticStatusText.Text = ProductText.Get("TheLocalLogFolderIsUnavailable");
                return;
            }
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            var start = new ProcessStartInfo(explorer) { UseShellExecute = false };
            start.ArgumentList.Add(_logs.DirectoryPath);
            using Process? process = Process.Start(start);
        }
        catch (Exception) { DiagnosticStatusText.Text = ProductText.Get("CouldNotOpenTheLocalLogFolder"); }
    }

    private static string ComponentVersion(string component)
    {
        string path = Path.Combine(AppContext.BaseDirectory, component, $"DesktopMotionStudio.{component}.exe");
        if (!File.Exists(path)) return ProductText.Get("Unavailable");
        try
        {
            FileVersionInfo value = FileVersionInfo.GetVersionInfo(path);
            return $"{value.FileMajorPart}.{value.FileMinorPart}.{value.FileBuildPart}.{value.FilePrivatePart}";
        }
        catch (Exception) { return ProductText.Get("Unavailable"); }
    }

    private static string LogStatus(DiagnosticSnapshot value) => value.Status switch
    {
        DiagnosticStatus.Ready => ProductText.Format("LocalRecordsYouCanCopyASummary", value.Events.Count),
        DiagnosticStatus.Empty => ProductText.Get("NoLaunchDiagnosticsYet"),
        DiagnosticStatus.Busy => ProductText.Get("TheDiagnosticLogIsInUseRefreshTo"),
        DiagnosticStatus.Unsupported => ProductText.Get("TheLogFormatIsUnsupportedTheOriginalLog"),
        DiagnosticStatus.Invalid => ProductText.Get("TheLogCouldNotBeVerifiedTheOriginal"),
        _ => ProductText.Get("LocalLogsAreUnavailable")
    };

    private static string Display(DiagnosticEvent item)
    {
        string stage = item.Stage switch { DiagnosticStage.Request => ProductText.Get("Request"), DiagnosticStage.Target => ProductText.Get("AppLaunch"), _ => ProductText.Get("Rendering") };
        string outcome = item.Result switch
        {
            DiagnosticResultCode.InvalidRequest => ProductText.Get("InvalidRequest"),
            DiagnosticResultCode.WindowReady => ProductText.Get("AppWindowReady"),
            DiagnosticResultCode.HandedOff => ProductText.Get("MotionHandedOff"),
            DiagnosticResultCode.BindingMissing => ProductText.Get("BindingNotFound"),
            DiagnosticResultCode.ConfigurationUnavailable => ProductText.Get("ConfigurationUnavailable"),
            DiagnosticResultCode.TargetUnavailable => ProductText.Get("TargetUnavailable"),
            DiagnosticResultCode.RendererFailed => ProductText.Get("RendererFailed"),
            DiagnosticResultCode.NotStarted => ProductText.Get("NotStarted"),
            DiagnosticResultCode.TimedOut => ProductText.Get("TimedOut"),
            _ => item.Result.ToString()
        };
        string fallback = item.Fallback switch
        {
            DiagnosticFallback.AnimationDisabled => ProductText.Get("MotionDisabled"),
            DiagnosticFallback.PlaybackUnavailable => ProductText.Get("WorkUnavailable"),
            DiagnosticFallback.RendererUnavailable => ProductText.Get("RendererUnavailable"),
            DiagnosticFallback.RendererFailed => ProductText.Get("ClosedAfterARenderFailure"),
            DiagnosticFallback.RendererCleanupFailed => ProductText.Get("RendererCleanupIssue"),
            _ => ""
        };
        string guidance = item.Fallback switch
        {
            DiagnosticFallback.PlaybackUnavailable => ProductText.Get("CheckThatTheBoundWorkIsAvailableIn"),
            DiagnosticFallback.RendererUnavailable or DiagnosticFallback.RendererFailed or
                DiagnosticFallback.RendererCleanupFailed => ProductText.Get("CheckTheRendererUnderComponentsAndReopenStudio"),
            _ => item.Result switch
            {
                DiagnosticResultCode.InvalidRequest or DiagnosticResultCode.BindingMissing =>
                    ProductText.Get("CheckTheBindingAndManagedShortcutUnderMy"),
                DiagnosticResultCode.ConfigurationUnavailable => ProductText.Get("CheckTheConfigurationInStudioBeforeTryingAgain"),
                DiagnosticResultCode.TargetUnavailable or DiagnosticResultCode.RecursiveTarget or
                    DiagnosticResultCode.LaunchFailed or DiagnosticResultCode.IdentityUnavailable or
                    DiagnosticResultCode.IdentityChanged => ProductText.Get("CheckTheTargetUnderMyAppsAndConfirm"),
                DiagnosticResultCode.NoUsableWindow or DiagnosticResultCode.ForegroundDenied or
                    DiagnosticResultCode.AmbiguousWindow => ProductText.Get("SwitchToTheTargetWindowAndTryThe"),
                DiagnosticResultCode.TimedOut or DiagnosticResultCode.Busy or DiagnosticResultCode.ProcessExited or
                    DiagnosticResultCode.UnexpectedFailure => ProductText.Get("CloseTheTargetAppBeforeTryingAgain"),
                DiagnosticResultCode.RendererFailed =>
                    ProductText.Get("CheckTheRendererAndWindowsAnimationSettingsUnder"),
                _ => ""
            }
        };
        string recovery = guidance.Length == 0 ? "" : $" · {guidance}";
        return $"{item.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC · {stage} · {outcome}{fallback}{recovery}";
    }
}
