using System.Windows;
using System.Windows.Controls;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Studio;

internal sealed record SceneRow(SceneLibraryItem Item)
{
    public string Name => Item.DisplayName;
    public string? Id => Item.Catalog.Manifest?.SceneId;
    public string Summary => Item.IsPlayable ? Item.Presentation?.Style is { Length: > 0 } style ? style + " · 可播放" : "可播放" : "暂不可用";
}

public partial class SceneLibraryWindow : Window
{
    private readonly IStudioStateStore _store;
    private readonly SceneWorkspace _workspace;
    private readonly PreviewCoordinator _previews;
    private readonly PreviewPresenter _presenter;
    private readonly PreviewIntentController _intents;
    private readonly string? _appId;
    private SceneRow[] _rows = [];
    private string? _preferredScene;
    private int _generation, _selectionVersion;
    private bool _closed, _closing, _allowClose, _loading;

    internal SceneLibraryWindow(IStudioStateStore store, SceneWorkspace workspace, PreviewCoordinator previews, string? sceneId = null, string? appId = null, PreviewIntentController? intents = null)
    {
        InitializeComponent(); _store = store; _workspace = workspace; _previews = previews; _preferredScene = sceneId; _appId = appId;
        _intents = intents ?? new(previews, workspace.FindPlayable);
        _presenter = new(previews, this, PreviewStatusText, StopPreviewButton);
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 60));
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 60));
        Loaded += async (_, _) => await RefreshAsync();
        Closing += async (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true; _selectionVersion++; _generation++; IsEnabled = false;
            await _intents.StopAsync(); _allowClose = true; Close();
        };
        Closed += (_, _) => { _closed = true; _generation++; _selectionVersion++; _presenter.Dispose(); };
    }

    private SceneRow? Selected => SceneList.SelectedItem as SceneRow;

    internal async Task RefreshAsync()
    {
        int generation = ++_generation; _loading = true; UpdateActions(); StatusText.Text = "正在读取作品…";
        try
        {
            SceneLibrarySnapshot snapshot = await Task.Run(_workspace.Scan);
            if (_closed || _closing || generation != _generation) return;
            _rows = snapshot.Items.Select(item => new SceneRow(item)).ToArray();
            Filter();
            StatusText.Text = snapshot.Error switch
            {
                SceneLibraryError.TooManyScenes => "作品目录超出允许数量，请检查安装内容后重试。",
                SceneLibraryError.Unavailable => "无法读取作品库，请检查安装内容后刷新。",
                _ => _preferredScene is not null && Selected is null ? "原先选择的作品暂不可用，请重新选择。" : $"{_rows.Length} 个作品 · 同一作品可用于多个应用。"
            };
        }
        catch (Exception)
        {
            if (_closed || _closing || generation != _generation) return;
            _rows = []; Filter(); StatusText.Text = "无法读取作品库，请刷新重试。";
        }
        finally { if (!_closed && generation == _generation) { _loading = false; UpdateActions(); } }
    }

    private void Filter()
    {
        string query = SearchBox.Text.Trim();
        SceneRow[] visible = _rows.Where(row => row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        SceneList.ItemsSource = visible;
        SceneList.SelectedItem = _preferredScene is null ? visible.FirstOrDefault() : visible.FirstOrDefault(row => string.Equals(row.Id, _preferredScene, StringComparison.OrdinalIgnoreCase));
        EmptyText.Text = _rows.Length == 0 ? "暂无可显示作品。\n可刷新或返回我的应用。" : "没有匹配的作品。";
        EmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDetails();
    }

    private async void UpdateDetails()
    {
        _intents?.CancelPending();
        int version = ++_selectionVersion;
        SceneRow? row = Selected;
        if (row?.Id is string id) _preferredScene = id;
        SceneNameText.Text = row?.Name ?? "请选择作品";
        DescriptionText.Text = row?.Item.Presentation?.Description is { Length: > 0 } description ? description : "暂无作品介绍。";
        PresentationText.Text = row is null ? "" : string.Join(" · ", new[] { row.Item.Presentation?.Style, row.Item.Presentation?.Theme }.Where(text => !string.IsNullOrWhiteSpace(text)));
        AvailabilityText.Text = row is null ? "" : SceneUiText.Availability(row.Item);
        CoverImage.Source = null; FallbackMark.Visibility = Visibility.Visible;
        FallbackMark.Text = row?.Name[..1] ?? "M"; UpdateActions();
        if (row is null) return;
        var cover = await Task.Run(() => SceneCoverLoader.Load(row.Item));
        if (_closed || _closing || version != _selectionVersion) return;
        CoverImage.Source = cover; FallbackMark.Visibility = cover is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateActions()
    {
        PreviewButton.IsEnabled = BindButton.IsEnabled = !_loading && !_closing && Selected?.Item.IsPlayable == true;
    }

    private async void OnPreview(object sender, RoutedEventArgs e)
    {
        if (Selected?.Id is not string id) return;
        PreviewResult result = await _intents.PlayAsync(id);
        if (_closed || _closing || !_intents.IsCurrent(result)) return;
        if (result.Outcome == PreviewOutcome.Unavailable)
        { AvailabilityText.Text = "作品暂不可用，请刷新后重新选择。"; PreviewButton.IsEnabled = BindButton.IsEnabled = false; }
    }

    private async void OnBind(object sender, RoutedEventArgs e)
    {
        if (Selected?.Id is not string id) return;
        var picker = new BindingPickerWindow(_store, _workspace, id, _appId) { Owner = this };
        bool? confirmed = picker.ShowDialog();
        try
        {
            await RefreshAsync();
            if (_closed || _closing || picker.Result is null) return;
            RepositoryResult current = await Task.Run(_store.Load);
            if (_closed || _closing) return;
            if (!current.IsSuccess) { StatusText.Text = UiText.Storage(current.Error); return; }
            if (confirmed != true) { StatusText.Text = picker.OutcomeMessage; return; }
            ApplicationBinding? expected = picker.Result.Binding;
            ApplicationBinding? actual = current.State!.Bindings.FirstOrDefault(binding => string.Equals(binding.AppId, expected?.AppId, StringComparison.OrdinalIgnoreCase));
            StatusText.Text = actual is not null && actual == expected
                ? Selected?.Item.IsPlayable == true ? "动效绑定已保存。" : "绑定已保存；作品当前不可用。"
                : "配置已被另一项操作改变，请刷新后核对。";
        }
        catch (Exception) { if (!_closed && !_closing) StatusText.Text = "无法重新读取绑定，请刷新后核对配置。"; }
    }

    private void OnSearch(object sender, TextChangedEventArgs e) { if (SceneList is not null) Filter(); }
    private void OnSelection(object sender, SelectionChangedEventArgs e) { if (SceneNameText is not null) UpdateDetails(); }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void OnHome(object sender, RoutedEventArgs e) => Close();
    private async void OnStop(object sender, RoutedEventArgs e) => await _intents.StopAsync();
}
