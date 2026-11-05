using System.Windows;
using System.Windows.Controls;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Studio;

internal sealed record ApplicationRow(ApplicationProfile Profile, BindingResolution Binding, string TargetStatus)
{
    public string Name => Profile.DisplayName;
    public string Initial => Name[..1].ToUpperInvariant();
    public string Summary => UiText.Kind(Profile.Target?.Kind) + " · " + TargetStatus;
    public string Scene => Binding.Binding is null ? "未绑定动效" : $"{Binding.Binding.SceneId} · {(Binding.Binding.Enabled ? "已启用" : "已停用")}";
}

public partial class MainWindow : Window
{
    private readonly ApplicationRegistry _registry;
    private readonly BindingService _bindings;
    private ApplicationRow[] _rows = [];
    private bool _busy;
    private bool _readable;
    private bool _closed;
    private int _refreshVersion;

    internal MainWindow(IStudioStateStore store, string catalogRoot)
    {
        InitializeComponent();
        _registry = new ApplicationRegistry(store);
        _bindings = new BindingService(store, () => new BuiltInSceneCatalog().Scan(catalogRoot), ApplicationRegistry.GetTarget);
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 60));
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 60));
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => { _closed = true; _refreshVersion++; };
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
                ApplicationRow[] rows = result.IsSuccess ? result.State!.Applications.Select(profile =>
                {
                    LaunchTarget? target = ApplicationRegistry.GetTarget(profile);
                    string status = target is null ? "需要确认目标" : new LaunchTargetIdentityResolver().Resolve(target).Succeeded
                        ? profile.Target!.Kind == ApplicationTargetKind.Packaged ? "身份格式有效" : "入口可用" : "入口不可用";
                    return new ApplicationRow(profile, _bindings.Resolve(profile.AppId), status);
                }).OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray() : [];
                return (result, rows);
            });
            if (_closed || version != _refreshVersion) return;
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

    private void UpdateDetails()
    {
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
        ToggleButton.Content = row?.Binding.Binding?.Enabled == true ? "停用动效" : "启用动效";
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        AddButton.IsEnabled = _readable && !_busy;
        DetailsPanel.IsEnabled = _readable && !_busy && Selected is not null;
        ToggleButton.IsEnabled = Selected?.Binding.Binding is not null;
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
        if (MessageBox.Show(this, $"移除“{row.Name}”及其动效绑定配置？", "移除应用配置", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
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
}
