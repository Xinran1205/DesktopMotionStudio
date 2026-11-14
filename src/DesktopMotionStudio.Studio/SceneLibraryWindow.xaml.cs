using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopMotionStudio.Core.Video;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Studio;

internal sealed record SceneRow(SceneLibraryItem Item)
{
    public string Name => SceneUiText.Name(Item);
    public string? Id => Item.Catalog.Manifest?.SceneId;
    public string Summary => Item.IsPlayable ? SceneUiText.Presentation(Item)?.Style is { Length: > 0 } style
        ? style + ProductText.Get("Playable") : ProductText.Get("Playable2") : ProductText.Get("Unavailable2");
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
        if (workspace.Imports is not null)
        {
            MyMotions.Visibility = ImportButton.Visibility = Visibility.Visible;
            MyMotions.IsChecked = ImportedVideoWork.IsId(sceneId);
            _preferredScene = sceneId;
        }
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
        int generation = ++_generation; _loading = true; UpdateActions(); StatusText.Text = ProductText.Get("LoadingWorks");
        try
        {
            SceneLibrarySnapshot snapshot = await Task.Run(_workspace.Scan);
            if (_closed || _closing || generation != _generation) return;
            _rows = snapshot.Items.Select(item => new SceneRow(item)).ToArray();
            Filter();
            StatusText.Text = snapshot.Error switch
            {
                SceneLibraryError.TooManyScenes => ProductText.Get("TheLibraryExceedsTheSupportedSizeCheckThe"),
                SceneLibraryError.Unavailable => ProductText.Get("CouldNotReadTheLibraryCheckTheInstallation"),
                _ => _preferredScene is not null && Selected is null ? ProductText.Get("ThePreviouslySelectedWorkIsUnavailableChooseAnother") : ProductText.Format("WorksEachWorkCanBeUsedWithSeveral", _rows.Length)
            };
        }
        catch (Exception)
        {
            if (_closed || _closing || generation != _generation) return;
            _rows = []; Filter(); StatusText.Text = ProductText.Get("CouldNotLoadTheLibraryRefreshAndTry");
        }
        finally { if (!_closed && generation == _generation) { _loading = false; UpdateActions(); } }
    }

    private void Filter()
    {
        string query = SearchBox.Text.Trim();
        bool imports = MyMotions?.IsChecked == true;
        var section = _rows.Where(row => ImportedVideoWork.IsId(row.Id) == imports).ToArray();
        SceneRow[] visible = section.Where(row => row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        SceneList.ItemsSource = visible;
        SceneList.SelectedItem = _preferredScene is null ? visible.FirstOrDefault() : visible.FirstOrDefault(row => string.Equals(row.Id, _preferredScene, StringComparison.OrdinalIgnoreCase));
        EmptyText.Text = section.Length == 0 ? ProductText.Get(imports ? "NoImportedMotions" : "NoWorksAvailableRefreshOrReturnToMy")
            : ProductText.Get("NoWorksMatchTheSearch");
        EmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDetails();
    }

    private async void UpdateDetails()
    {
        _intents?.CancelPending();
        int version = ++_selectionVersion;
        SceneRow? row = Selected;
        if (row?.Id is string id) _preferredScene = id;
        SceneNameText.Text = row?.Name ?? ProductText.Get("ChooseAWork");
        ScenePresentation? presentation = row is null ? null : SceneUiText.Presentation(row.Item);
        DescriptionText.Text = presentation?.Description is { Length: > 0 } description ? description : ProductText.Get("NoDescriptionAvailable");
        PresentationText.Text = presentation is null ? "" : string.Join(" · ", new[] { presentation.Style, presentation.Theme }.Where(text => !string.IsNullOrWhiteSpace(text)));
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
        ManageImport.Visibility = !_loading && !_closing && ImportedVideoWork.IsId(Selected?.Id)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnPreview(object sender, RoutedEventArgs e)
    {
        if (Selected?.Id is not string id) return;
        PreviewResult result = await _intents.PlayAsync(id);
        if (_closed || _closing || !_intents.IsCurrent(result)) return;
        if (result.Outcome == PreviewOutcome.Unavailable)
        { AvailabilityText.Text = ProductText.Get("ThisWorkIsUnavailableRefreshAndChooseIt"); PreviewButton.IsEnabled = BindButton.IsEnabled = false; }
        else if (PreviewButton.IsEnabled)
        {
            FocusManager.SetFocusedElement(this, PreviewButton);
            if (IsActive) PreviewButton.Focus();
        }
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
                ? Selected?.Item.IsPlayable == true ? ProductText.Get("MotionBindingSaved") : ProductText.Get("BindingSavedThisWorkIsCurrentlyUnavailable")
                : ProductText.Get("AnotherOperationChangedTheConfigurationRefreshAndCheck");
        }
        catch (Exception) { if (!_closed && !_closing) StatusText.Text = ProductText.Get("CouldNotReloadTheBindingRefreshAndCheck"); }
    }

    private void OnSearch(object sender, TextChangedEventArgs e) { if (SceneList is not null) Filter(); }
    private void OnSelection(object sender, SelectionChangedEventArgs e) { if (SceneNameText is not null) UpdateDetails(); }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void OnHome(object sender, RoutedEventArgs e) => Close();
    private async void OnStop(object sender, RoutedEventArgs e) => await _intents.StopAsync();
    private void OnSection(object sender, RoutedEventArgs e)
    {
        if (SceneList is null) return;
        _preferredScene = null;
        Filter();
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        if (_workspace.Imports is not { } imports) return;
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = ProductText.Get("ImportMp4"), Filter = "MP4 (*.mp4)|*.mp4", CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        await _intents.StopAsync();
        if (_closing) return;
        var editor = new ImportMotionWindow(imports, _previews, picker.FileName) { Owner = this };
        if (editor.ShowDialog() != true) return;
        MyMotions.IsChecked = true;
        SearchBox.Clear();
        _preferredScene = editor.SavedId;
        await RefreshAsync();
    }

    private async void OnEditImport(object sender, RoutedEventArgs e)
    {
        if (Selected?.Id is not { } id || _workspace.Imports is not { } imports) return;
        await _intents.StopAsync();
        if (_closing) return;
        var work = await Task.Run(() => imports.Find(id));
        if (_closing) return;
        if (work is null) { await RefreshAsync(); return; }
        var editor = new ImportMotionWindow(imports, _previews, "", work) { Owner = this };
        if (editor.ShowDialog() == true) await RefreshAsync();
    }

    private async void OnRemoveImport(object sender, RoutedEventArgs e)
    {
        if (Selected?.Id is not { } id || _workspace.Imports is not { } imports) return;
        var work = await Task.Run(() => imports.Find(id));
        if (_closing || Selected?.Id != id) return;
        if (work is null) { await RefreshAsync(); return; }
        if (MessageBox.Show(this, ProductText.Format("RemoveImportConfirm", work.Options.Name), ProductText.Get("RemoveImport"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await _intents.StopAsync();
        if (_closing) return;
        var error = await Task.Run(() => imports.Remove(id, work.Revision));
        if (_closing) return;
        if (error == ImportError.None) { _preferredScene = null; await RefreshAsync(); }
        else StatusText.Text = ImportMotionWindow.ErrorText(error);
    }
}
