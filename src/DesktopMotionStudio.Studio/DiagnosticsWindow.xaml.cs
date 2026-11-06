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
        DiagnosticStatusText.Text = "正在读取诊断…";
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
            CapabilityText.Text = result.host == "不可用" ? "内置渲染器不可用。" :
                $"内置 GDI+ 矢量渲染 · Windows 动画效果{(SystemParameters.ClientAreaAnimation ? "开启" : "关闭")}";
        }
        catch (Exception)
        {
            if (!_closed && version == _refreshVersion) DiagnosticStatusText.Text = "无法读取本地诊断。";
        }
        if (!_closed && version == _refreshVersion) UpdateEnabled();
    }

    private (string Status, bool CanRepair) ReadRecovery()
    {
        if (_bindingId is null || _shortcuts is null) return ("选择一个有动效绑定的应用后可查看入口。", false);
        RepositoryResult profile = _state.Load();
        if (!profile.IsSuccess) return (UiText.Storage(profile.Error), false);
        ApplicationBinding? binding = profile.State!.Bindings.FirstOrDefault(item =>
            string.Equals(item.BindingId, _bindingId, StringComparison.OrdinalIgnoreCase));
        if (binding is null) return ("所选绑定已不存在。", false);
        ApplicationProfile? app = profile.State.Applications.FirstOrDefault(item =>
            string.Equals(item.AppId, binding.AppId, StringComparison.OrdinalIgnoreCase));
        if (app is null) return ("所选应用已不存在。", false);
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
        RepairResultText.Text = "正在验证并修复桌面入口…";
        try
        {
            ShortcutResult result = await Task.Run(() => _shortcuts.Repair(_bindingId));
            if (_closed) return;
            Repaired |= result.Status == ShortcutStatus.Created;
            RepairResultText.Text = MainWindow.ShortcutText(result.Status);
            await RefreshAsync();
        }
        catch (Exception) { if (!_closed) RepairResultText.Text = "无法确认修复结果，请刷新。"; }
        finally { _busy = false; if (!_closed) UpdateEnabled(); }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_busy || _snapshot.Status != DiagnosticStatus.Ready) return;
        try
        {
            _copyReport(DiagnosticReport.Format(_snapshot.Events));
            DiagnosticStatusText.Text = "已复制脱敏摘要。";
        }
        catch (Exception) { DiagnosticStatusText.Text = "剪贴板不可用，请稍后重试。"; }
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (_busy || !_canOpen) return;
        try
        {
            if (!Directory.Exists(_logs.DirectoryPath) || !LaunchTargetIdentityResolver.IsLocalPath(_logs.DirectoryPath))
            {
                DiagnosticStatusText.Text = "本地日志目录不可用。";
                return;
            }
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            var start = new ProcessStartInfo(explorer) { UseShellExecute = false };
            start.ArgumentList.Add(_logs.DirectoryPath);
            using Process? process = Process.Start(start);
        }
        catch (Exception) { DiagnosticStatusText.Text = "无法打开本地日志目录。"; }
    }

    private static string ComponentVersion(string component)
    {
        string path = Path.Combine(AppContext.BaseDirectory, component, $"DesktopMotionStudio.{component}.exe");
        if (!File.Exists(path)) return "不可用";
        try
        {
            FileVersionInfo value = FileVersionInfo.GetVersionInfo(path);
            return $"{value.FileMajorPart}.{value.FileMinorPart}.{value.FileBuildPart}.{value.FilePrivatePart}";
        }
        catch (Exception) { return "不可用"; }
    }

    private static string LogStatus(DiagnosticSnapshot value) => value.Status switch
    {
        DiagnosticStatus.Ready => $"本机保留 {value.Events.Count} 条记录，摘要可手动复制。",
        DiagnosticStatus.Empty => "还没有启动诊断记录。",
        DiagnosticStatus.Busy => "诊断日志正被使用，请刷新。",
        DiagnosticStatus.Unsupported => "日志版本不受支持，已保留原始记录。",
        DiagnosticStatus.Invalid => "日志内容无法验证，已保留原始记录。",
        _ => "本地日志不可用。"
    };

    private static string Display(DiagnosticEvent item)
    {
        string stage = item.Stage switch { DiagnosticStage.Request => "请求", DiagnosticStage.Target => "目标启动", _ => "渲染" };
        string outcome = item.Result switch
        {
            DiagnosticResultCode.InvalidRequest => "请求无效",
            DiagnosticResultCode.WindowReady => "目标窗口就绪",
            DiagnosticResultCode.HandedOff => "动效已交接",
            DiagnosticResultCode.BindingMissing => "找不到绑定",
            DiagnosticResultCode.ConfigurationUnavailable => "配置不可用",
            DiagnosticResultCode.TargetUnavailable => "目标不可用",
            DiagnosticResultCode.RendererFailed => "渲染失败",
            DiagnosticResultCode.NotStarted => "未启动",
            DiagnosticResultCode.TimedOut => "超时",
            _ => item.Result.ToString()
        };
        string fallback = item.Fallback switch
        {
            DiagnosticFallback.AnimationDisabled => " · 动效已停用",
            DiagnosticFallback.PlaybackUnavailable => " · 作品不可用",
            DiagnosticFallback.RendererUnavailable => " · 渲染器不可用",
            DiagnosticFallback.RendererFailed => " · 渲染失败后退出",
            DiagnosticFallback.RendererCleanupFailed => " · 渲染清理异常",
            _ => ""
        };
        string guidance = item.Fallback switch
        {
            DiagnosticFallback.PlaybackUnavailable => "在作品库确认所绑定的作品可用。",
            DiagnosticFallback.RendererUnavailable or DiagnosticFallback.RendererFailed or
                DiagnosticFallback.RendererCleanupFailed => "在组件状态中检查渲染器，重开 Studio 后重试。",
            _ => item.Result switch
            {
                DiagnosticResultCode.InvalidRequest or DiagnosticResultCode.BindingMissing =>
                    "在应用列表中检查绑定和受管桌面入口。",
                DiagnosticResultCode.ConfigurationUnavailable => "在 Studio 检查配置状态，配置可读后重试。",
                DiagnosticResultCode.TargetUnavailable or DiagnosticResultCode.RecursiveTarget or
                    DiagnosticResultCode.LaunchFailed or DiagnosticResultCode.IdentityUnavailable or
                    DiagnosticResultCode.IdentityChanged => "在应用列表中检查目标，确认目标可直接打开。",
                DiagnosticResultCode.NoUsableWindow or DiagnosticResultCode.ForegroundDenied or
                    DiagnosticResultCode.AmbiguousWindow => "切换到目标窗口，再从桌面入口重试。",
                DiagnosticResultCode.TimedOut or DiagnosticResultCode.Busy or DiagnosticResultCode.ProcessExited or
                    DiagnosticResultCode.UnexpectedFailure => "确认目标已退出后重试。",
                DiagnosticResultCode.RendererFailed =>
                    "在组件状态中检查渲染器和系统动画设置。",
                _ => ""
            }
        };
        string recovery = guidance.Length == 0 ? "" : $" · {guidance}";
        return $"{item.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC · {stage} · {outcome}{fallback}{recovery}";
    }
}
