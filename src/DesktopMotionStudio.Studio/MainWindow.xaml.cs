using System.Windows;
using System.Windows.Controls;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.Shortcuts;
using System.IO;

namespace DesktopMotionStudio.Studio;

internal sealed record ApplicationRow(ApplicationProfile Profile, BindingResolution Binding, string TargetStatus, SceneLibraryItem? Work)
{
    public string Name => Profile.DisplayName;
    public string Initial => Name[..1].ToUpperInvariant();
    public string Summary => UiText.Kind(Profile.Target?.Kind) + " · " + TargetStatus;
    public string Scene => Binding.Binding is null ? "未绑定动效" : $"{Work?.DisplayName ?? Binding.Binding.SceneId} · {(Binding.Binding.Enabled ? "已启用" : "已停用")}";
}

public partial class MainWindow : Window
{
    private readonly ApplicationRegistry _registry;
    private readonly BindingService _bindings;
    private readonly IStudioStateStore _store;
    private readonly SceneWorkspace _workspace;
    private readonly PreviewCoordinator _previews;
    private readonly PreviewPresenter _presenter;
    private readonly PreviewIntentController _intents;
    private ApplicationRow[] _rows = [];
    private bool _busy;
    private bool _readable;
    private bool _closed;
    private int _refreshVersion;
    private int _detailsVersion;
    private bool _closing, _allowClose;

    internal MainWindow(IStudioStateStore store, string catalogRoot, IPreviewSessionFactory? previewFactory = null,
        ManagedShortcutService? shortcuts = null)
    {
        InitializeComponent();
        _shortcuts = shortcuts;
        ShortcutPanel.Visibility = shortcuts is null ? Visibility.Collapsed : Visibility.Visible;
        _registry = new ApplicationRegistry(store);
        _store = store; _workspace = new SceneWorkspace(catalogRoot); _bindings = _workspace.CreateBindings(store);
        _previews = new(previewFactory ?? new MotionHostPreviewSessionFactory(new WindowsMotionHostProcessFactory(Path.Combine(AppContext.BaseDirectory, "MotionHost", "DesktopMotionStudio.MotionHost.exe"))));
        _intents = new(_previews, _workspace.FindPlayable);
        _presenter = new(_previews, this, PreviewStatusText, StopPreviewButton);
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 60));
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 60));
        Loaded += async (_, _) => await RefreshAsync();
        Closing += async (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            if (_busy) { StatusText.Text = "正在保存配置，请稍候再关闭。"; return; }
            if (_closing) return;
            _closing = true; _refreshVersion++; IsEnabled = false;
            await _intents.DisposeAsync(); _allowClose = true; Close();
        };
        Closed += (_, _) => { _closed = true; _refreshVersion++; _detailsVersion++; _presenter.Dispose(); };
    }

    private ApplicationRow? Selected => ApplicationList.SelectedItem as ApplicationRow;

    internal async Task RefreshAsync(string? selectId = null)
    {
        int version = ++_refreshVersion;
        StatusText.Text = "正在读取配置…";
        try
        {
            var loaded = await Task.Run(() =>
            {
                RepositoryResult result = _registry.Load();
                SceneLibrarySnapshot library = _workspace.Scan();
                ApplicationRow[] rows = result.IsSuccess ? result.State!.Applications.Select(profile =>
                {
                    LaunchTarget? target = ApplicationRegistry.GetTarget(profile);
                    string status = target is null ? "需要确认目标" : new LaunchTargetIdentityResolver().Resolve(target).Succeeded
                        ? profile.Target!.Kind == ApplicationTargetKind.Packaged ? "身份格式有效" : "入口可用" : "入口不可用";
                    BindingResolution binding = _bindings.Resolve(profile.AppId);
                    SceneLibraryItem? work = binding.Binding is null ? null : library.Items.FirstOrDefault(item => string.Equals(item.Catalog.Manifest?.SceneId, binding.Binding.SceneId, StringComparison.OrdinalIgnoreCase));
                    return new ApplicationRow(profile, binding, status, work);
                }).OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray() : [];
                return (result, rows);
            });
            if (_closed || _closing || version != _refreshVersion) return;
            _readable = loaded.result.IsSuccess;
            _rows = loaded.rows;
            Filter(selectId);
            CountText.Text = $"{_rows.Length} 个已添加应用 · 选择应用查看详情";
            StatusText.Text = loaded.result.IsSuccess
                ? _rows.Any(row => row.Profile.Target is null) ? "部分旧配置需要确认启动入口，原始内容已保留。" : "已读取此设备上的应用配置。"
                : UiText.Storage(loaded.result.Error);
        }
        catch (Exception)
        {
            if (_closed || version != _refreshVersion) return;
            _readable = false;
            StatusText.Text = "读取配置未完成，请刷新重试。";
        }
        if (!_closed) UpdateEnabled();
    }

    private void Filter(string? selectedId = null)
    {
        selectedId ??= Selected?.Profile.AppId;
        string query = SearchBox.Text.Trim();
        ApplicationRow[] visible = _rows.Where(row => row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        ApplicationList.ItemsSource = visible;
        ApplicationList.SelectedItem = visible.FirstOrDefault(row => row.Profile.AppId == selectedId) ?? visible.FirstOrDefault();
        EmptyText.Text = _rows.Length == 0 ? "还没有添加应用。\n选择“添加应用”开始配置。" : "没有找到匹配的已添加应用。";
        EmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDetails();
    }

    private async void UpdateDetails()
    {
        _intents?.CancelPending();
        ApplicationRow? row = Selected;
        NameBox.Text = row?.Name ?? string.Empty;
        TargetKindText.Text = UiText.Kind(row?.Profile.Target?.Kind);
        TargetText.Text = row?.Profile.Target?.Identifier ?? row?.Profile.LaunchTarget ?? "选择一个应用查看配置";
        TargetStatusText.Text = row?.TargetStatus ?? string.Empty;
        BindingText.Text = row?.Binding.Status switch
        {
            BindingResolutionStatus.Ready => row.Scene,
            BindingResolutionStatus.Disabled => row.Scene,
            BindingResolutionStatus.SceneUnavailable => row.Scene + "\n作品暂不可用，绑定已保留。",
            BindingResolutionStatus.TargetUnavailable => row.Scene + "\n请先确认启动入口。",
            BindingResolutionStatus.StorageError => UiText.Storage(row.Binding.RepositoryError),
            _ => "未绑定动效"
        };
        BoundSceneNameText.Text = row?.Work?.DisplayName ?? row?.Binding.Binding?.SceneId ?? "未绑定作品";
        if (row?.Binding.Binding is not null && row.Work?.IsPlayable != true && row.Binding.Status == BindingResolutionStatus.Disabled)
            BindingText.Text += "\n作品暂不可用，绑定已保留。";
        BoundCover.Source = null; BoundFallback.Visibility = Visibility.Visible;
        int version = ++_detailsVersion;
        ToggleButton.Content = row?.Binding.Binding?.Enabled == true ? "停用动效" : "启用动效";
        RefreshShortcutDetails(row?.Binding.Binding?.BindingId, version);
        UpdateEnabled();
        if (row?.Work is not SceneLibraryItem work) return;
        var cover = await Task.Run(() => SceneCoverLoader.Load(work));
        if (_closed || _closing || version != _detailsVersion) return;
        BoundCover.Source = cover; BoundFallback.Visibility = cover is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateEnabled()
    {
        AddButton.IsEnabled = _readable && !_busy;
        DetailsPanel.IsEnabled = _readable && !_busy && Selected is not null;
        ToggleButton.IsEnabled = Selected?.Binding.Binding is not null;
        LibraryButton.IsEnabled = !_busy && !_closing;
        PreviewBoundButton.IsEnabled = !_busy && !_closing && Selected?.Work?.IsPlayable == true;
        ReplaceButton.IsEnabled = !_busy && !_closing && Selected is not null;
        UpdateShortcutButtons();
    }

    private async Task MutateAsync(Func<ApplicationRegistryResult> operation, string? selectedId)
    {
        if (_busy || !_readable) return;
        _busy = true;
        UpdateEnabled();
        try
        {
            ApplicationRegistryResult result = await Task.Run(operation);
            await RefreshAsync(selectedId);
            if (!_closed && _readable) StatusText.Text = UiText.Result(result);
        }
        catch (Exception) { if (!_closed) StatusText.Text = "操作未完成，现有配置已保留。请刷新重试。"; }
        finally { _busy = false; if (!_closed) UpdateEnabled(); }
    }

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        var dialog = new ApplicationEditorWindow(_registry) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await RefreshAsync(dialog.Result?.Profile?.AppId);
            if (!_closed && _readable && dialog.Result is not null) StatusText.Text = UiText.Result(dialog.Result);
        }
    }

    private async void OnRepair(object sender, RoutedEventArgs e)
    {
        if (Selected is not ApplicationRow row) return;
        var dialog = new ApplicationEditorWindow(_registry, row.Profile) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await RefreshAsync(row.Profile.AppId);
            if (!_closed && _readable && dialog.Result is not null) StatusText.Text = UiText.Result(dialog.Result);
        }
    }

    private async void OnRename(object sender, RoutedEventArgs e)
    {
        if (Selected is not ApplicationRow row) return;
        string name = NameBox.Text;
        await MutateAsync(() => _registry.Rename(row.Profile.AppId, name), row.Profile.AppId);
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Selected is not ApplicationRow row) return;
        string shortcutNotice = _shortcuts is not null && row.Binding.Binding is not null
            ? "\n如已创建桌面入口，请先在本页移除入口。移除绑定后，原入口将失效。" : string.Empty;
        if (MessageBox.Show(this, $"移除“{row.Name}”及其动效绑定配置？{shortcutNotice}", "移除应用配置", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await MutateAsync(() => _registry.Remove(row.Profile.AppId), null);
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        if (_busy || Selected is not ApplicationRow row || row.Binding.Binding is null) return;
        _busy = true;
        UpdateEnabled();
        try
        {
            BindingMutationResult result = await Task.Run(() => _bindings.SetEnabled(row.Profile.AppId, !row.Binding.Binding.Enabled));
            await RefreshAsync(row.Profile.AppId);
            if (!_closed && _readable) StatusText.Text = result.Succeeded ? "动效状态已保存。" : result.Status switch
            {
                BindingMutationStatus.StorageError => UiText.Storage(result.RepositoryError),
                BindingMutationStatus.SceneUnavailable => "作品暂不可用，启用状态未改变。",
                _ => "启动入口不可用或配置已改变，请刷新后重试。"
            };
        }
        catch (Exception) { if (!_closed) StatusText.Text = "动效状态未能保存，请重试。"; }
        finally { _busy = false; if (!_closed) UpdateEnabled(); }
    }

    private void OnSearch(object sender, TextChangedEventArgs e) { if (ApplicationList is not null) Filter(); }
    private void OnSelection(object sender, SelectionChangedEventArgs e) { if (NameBox is not null) UpdateDetails(); }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task OpenLibraryAsync(bool forSelected)
    {
        ApplicationRow? row = forSelected ? Selected : null;
        _refreshVersion++; _intents.CancelPending();
        var library = new SceneLibraryWindow(_store, _workspace, _previews, row?.Binding.Binding?.SceneId, row?.Profile.AppId, _intents) { Owner = this };
        library.ShowDialog();
        if (!_closed && !_closing) await RefreshAsync(row?.Profile.AppId);
    }

    private async void OnLibrary(object sender, RoutedEventArgs e) => await OpenLibraryAsync(false);
    private async void OnReplace(object sender, RoutedEventArgs e) => await OpenLibraryAsync(true);
    private async void OnStopPreview(object sender, RoutedEventArgs e) => await _intents.StopAsync();
    private async void OnPreviewBound(object sender, RoutedEventArgs e)
    {
        if (Selected?.Binding.Binding?.SceneId is not string id) return;
        PreviewResult result = await _intents.PlayAsync(id);
        if (_closed || _closing || !_intents.IsCurrent(result)) return;
        if (result.Outcome == PreviewOutcome.Unavailable) { PreviewStatusText.Text = "作品暂不可用，绑定已保留。请刷新后重试。"; PreviewBoundButton.IsEnabled = false; }
    }
}
