using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.Diagnostics;
using DesktopMotionStudio.Core.Video;
using System.IO;

namespace DesktopMotionStudio.Studio;

internal sealed record ApplicationRow(ApplicationProfile Profile, BindingResolution Binding, string TargetStatus,
    bool TargetAvailable, SceneLibraryItem? Work)
{
    public string Name => Profile.DisplayName;
    public string Initial => Name[..1].ToUpperInvariant();
    public string Summary => UiText.Kind(Profile.Target?.Kind) + " · " + TargetStatus;
    public string Scene => Binding.Binding is null ? ProductText.Get("NoMotionBound")
        : ProductText.Format("BoundWorkState", Work is null ? Binding.Binding.SceneId : SceneUiText.Name(Work),
            ProductText.Get(Binding.Binding.Enabled ? "BindingStateEnabled" : "BindingStateDisabled"));
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
    private readonly DiagnosticLogStore? _diagnostics;
    private readonly StudioLanguagePreference? _language;
    private readonly MotionDisplayPreference? _display;
    private ApplicationRow[] _rows = [];
    private bool _busy;
    private bool _readable;
    private bool _closed;
    private int _refreshVersion;
    private int _detailsVersion;
    private bool _closing, _allowClose;

    internal MainWindow(IStudioStateStore store, string catalogRoot, IPreviewSessionFactory? previewFactory = null,
        ManagedShortcutService? shortcuts = null, DiagnosticLogStore? diagnostics = null, TestLaunchClient? testLaunch = null,
        StudioLanguagePreference? language = null, MotionDisplayPreference? display = null)
    {
        InitializeComponent();
        _shortcuts = shortcuts;
        _diagnostics = diagnostics;
        _language = language;
        _display = display;
        _testLaunch = testLaunch;
        ShortcutPanel.Visibility = shortcuts is null ? Visibility.Collapsed : Visibility.Visible;
        TestLaunchPanel.Visibility = testLaunch is null ? Visibility.Collapsed : Visibility.Visible;
        DiagnosticsButton.Visibility = diagnostics is null ? Visibility.Collapsed : Visibility.Visible;
        _registry = new ApplicationRegistry(store);
        _store = store; _workspace = new SceneWorkspace(catalogRoot); _bindings = _workspace.CreateBindings(store);
        string tools = Path.Combine(AppContext.BaseDirectory, "MotionHost");
        var nativePreviews = new MotionHostPreviewSessionFactory(new WindowsMotionHostProcessFactory(
            Path.Combine(tools, "DesktopMotionStudio.MotionHost.exe"), ProductText.Language, display));
        var videoPreviews = new VideoPlaybackSessionFactory(Path.Combine(tools, VideoPlaybackRequest.WorkerName), display);
        _previews = new(previewFactory ?? new VideoAwarePreviewFactory(nativePreviews, videoPreviews, store));
        _intents = new(_previews, _workspace.FindPlayable);
        _presenter = new(_previews, this, PreviewStatusText, StopPreviewButton);
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 60));
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 60));
        Loaded += async (_, _) => await RefreshAsync();
        Closing += async (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            if (_busy) { StatusText.Text = ProductText.Get("SavingConfigurationWaitBeforeClosing"); return; }
            if (_closing) return;
            _closing = true; _refreshVersion++; IsEnabled = false;
            _testLaunchWait?.Cancel();
            await _intents.DisposeAsync(); _allowClose = true; Close();
        };
        Closed += (_, _) => { _closed = true; _refreshVersion++; _detailsVersion++; _presenter.Dispose(); };
    }

    private ApplicationRow? Selected => ApplicationList.SelectedItem as ApplicationRow;

    internal async Task RefreshAsync(string? selectId = null)
    {
        int version = ++_refreshVersion;
        StatusText.Text = ProductText.Get("LoadingConfiguration");
        try
        {
            var loaded = await Task.Run(() =>
            {
                RepositoryResult result = _registry.Load();
                SceneLibrarySnapshot library = _workspace.Scan();
                ApplicationRow[] rows = result.IsSuccess ? result.State!.Applications.Select(profile =>
                {
                    LaunchTarget? target = ApplicationRegistry.GetTarget(profile);
                    bool targetAvailable = target is not null && new LaunchTargetIdentityResolver().Resolve(target).Succeeded;
                    string status = target is null ? ProductText.Get("TargetNeedsConfirmation") : targetAvailable
                        ? profile.Target!.Kind == ApplicationTargetKind.Packaged ? ProductText.Get("IdentityFormatIsValid") : ProductText.Get("EntryAvailable") : ProductText.Get("EntryUnavailable");
                    BindingResolution binding = _bindings.Resolve(profile.AppId);
                    SceneLibraryItem? work = binding.Binding is null ? null : library.Items.FirstOrDefault(item => string.Equals(item.Catalog.Manifest?.SceneId, binding.Binding.SceneId, StringComparison.OrdinalIgnoreCase));
                    return new ApplicationRow(profile, binding, status, targetAvailable, work);
                }).OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray() : [];
                return (result, rows);
            });
            if (_closed || _closing || version != _refreshVersion) return;
            _readable = loaded.result.IsSuccess;
            _rows = loaded.rows;
            Filter(selectId);
            CountText.Text = ProductText.Format("SavedApps2", _rows.Length);
            StatusText.Text = loaded.result.IsSuccess
                ? _rows.Any(row => row.Profile.Target is null) ? ProductText.Get("SomeOlderEntriesNeedConfirmationTheirOriginalValues") : ProductText.Get("LoadedThisDeviceSSavedApps")
                : UiText.Storage(loaded.result.Error);
        }
        catch (Exception)
        {
            if (_closed || version != _refreshVersion) return;
            _readable = false;
            StatusText.Text = ProductText.Get("CouldNotLoadTheConfigurationRefreshAndTry");
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
        EmptyText.Text = _rows.Length == 0 ? ProductText.Get("NoAppsYetSelectAddAppToGet") : ProductText.Get("NoSavedAppsMatchTheSearch");
        EmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDetails();
    }

    private async void UpdateDetails()
    {
        _intents?.CancelPending();
        ApplicationRow? row = Selected;
        DetailsPanel.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        SelectionPrompt.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = row is null && _rows.Length > 0 && SearchBox.Text.Trim().Length > 0
            ? Visibility.Visible : Visibility.Collapsed;
        SelectionPromptTitle.Text = !_readable ? ProductText.Get("AppsCouldNotBeLoaded") : _rows.Length == 0 ? ProductText.Get("AddAnAppToGetStarted") : ProductText.Get("NoMatchingApps");
        SelectionPromptText.Text = !_readable ? ProductText.Get("RefreshTheConfigurationOrCheckLocalDiagnostics")
            : _rows.Length == 0 ? ProductText.Get("AddAnAppChooseAMotionAndTest") : ProductText.Get("ClearTheSearchToChooseAnAppAgain");
        SelectedAppText.Text = row?.Name ?? string.Empty;
        NameBox.Text = row?.Name ?? string.Empty;
        TargetKindText.Text = UiText.Kind(row?.Profile.Target?.Kind);
        TargetText.Text = row?.Profile.Target?.Identifier ?? row?.Profile.LaunchTarget ?? ProductText.Get("SelectAnAppToViewItsConfiguration");
        TargetStatusText.Text = row?.TargetStatus ?? string.Empty;
        TargetStatusText.Visibility = row?.TargetAvailable == false ? Visibility.Visible : Visibility.Collapsed;
        Artwork.Visibility = row?.TargetAvailable == false ? Visibility.Collapsed : Visibility.Visible;
        BindingText.Text = row?.Binding.Status switch
        {
            BindingResolutionStatus.Ready => row.Scene,
            BindingResolutionStatus.Disabled => row.Scene,
            BindingResolutionStatus.SceneUnavailable => row.Scene + ProductText.Get("TheWorkIsUnavailableTheBindingHasBeen"),
            BindingResolutionStatus.TargetUnavailable => row.Scene + ProductText.Get("ConfirmTheLaunchEntryFirst2"),
            BindingResolutionStatus.StorageError => UiText.Storage(row.Binding.RepositoryError),
            _ => ProductText.Get("NoMotionBound")
        };
        BoundSceneNameText.Text = row?.Work is { } selectedWork ? SceneUiText.Name(selectedWork)
            : row?.Binding.Binding?.SceneId ?? ProductText.Get("NoWorkBound");
        ReplaceButton.Content = row?.Binding.Binding is null ? ProductText.Get("ChooseMotionB") : ProductText.Get("ReplaceMotionB");
        BoundFallback.Text = row?.Binding.Binding is null ? ProductText.Get("ChooseAWorkToSeeItsCover") : ProductText.Get("NoCoverAvailable");
        if (row?.Binding.Binding is not null && row.Work?.IsPlayable != true && row.Binding.Status == BindingResolutionStatus.Disabled)
            BindingText.Text += ProductText.Get("TheWorkIsUnavailableTheBindingHasBeen");
        BoundCover.Source = null; BoundFallback.Visibility = Visibility.Visible;
        int version = ++_detailsVersion;
        ToggleButton.Content = row?.Binding.Binding?.Enabled == true ? ProductText.Get("DisableMotion") : ProductText.Get("EnableMotion");
        RefreshTestLaunchDetails(row);
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
        SettingsButton.IsEnabled = !_busy && !_closing;
        DiagnosticsButton.IsEnabled = !_busy && !_closing && _diagnostics is not null;
        PreviewBoundButton.IsEnabled = !_busy && !_closing && Selected?.Work?.IsPlayable == true;
        ReplaceButton.IsEnabled = !_busy && !_closing && Selected is not null;
        TestLaunchButton.IsEnabled = CanTestLaunch;
        Button? primary = _readable ? Selected switch
        {
            null => _rows.Length == 0 ? AddButton : ClearSearchButton,
            { TargetAvailable: false } => RepairButton,
            { Binding.Binding: null } => ReplaceButton,
            _ => _testLaunch is null ? PreviewBoundButton : TestLaunchButton
        } : null;
        AddButton.Tag = primary == AddButton;
        ClearSearchButton.Tag = primary == ClearSearchButton;
        RepairButton.Tag = primary == RepairButton;
        ReplaceButton.Tag = primary == ReplaceButton;
        PreviewBoundButton.Tag = primary == PreviewBoundButton;
        TestLaunchButton.Tag = primary == TestLaunchButton;
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
        catch (Exception) { if (!_closed) StatusText.Text = ProductText.Get("TheOperationDidNotFinishTheExistingConfiguration"); }
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
            ? ProductText.Get("RemoveAnyManagedShortcutFromThisPageFirst") : string.Empty;
        if (MessageBox.Show(this, ProductText.Format("RemoveAndItsMotionBinding", row.Name, shortcutNotice), ProductText.Get("RemoveAppConfiguration"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
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
            if (!_closed && _readable) StatusText.Text = result.Succeeded ? ProductText.Get("MotionPreferenceSaved") : result.Status switch
            {
                BindingMutationStatus.StorageError => UiText.Storage(result.RepositoryError),
                BindingMutationStatus.SceneUnavailable => ProductText.Get("TheWorkIsUnavailableTheMotionSettingWas"),
                _ => ProductText.Get("TheLaunchEntryIsUnavailableOrTheConfiguration")
            };
        }
        catch (Exception) { if (!_closed) StatusText.Text = ProductText.Get("CouldNotSaveTheMotionSettingTryAgain"); }
        finally { _busy = false; if (!_closed) UpdateEnabled(); }
    }

    private void OnSearch(object sender, TextChangedEventArgs e) { if (ApplicationList is not null) Filter(); }
    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }
    private void OnSelection(object sender, SelectionChangedEventArgs e)
    {
        if (NameBox is null) return;
        DetailsScroll.ScrollToTop();
        UpdateDetails();
    }
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
    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (_busy || _closing) return;
        var window = new SettingsWindow(new StudioSettingsService(_store), _language, _display) { Owner = this };
        if (window.ShowDialog() == true)
            StatusText.Text = ProductText.Get(window.RequiresLanguageRestart ? "LanguageSaved" : "SettingsSaved");
    }
    private async void OnDiagnostics(object sender, RoutedEventArgs e)
    {
        if (_busy || _closing || _diagnostics is null) return;
        ApplicationRow? selected = Selected;
        var window = new DiagnosticsWindow(_store, _workspace.Root, _diagnostics, _shortcuts,
            selected?.Binding.Binding?.BindingId)
        { Owner = this };
        window.ShowDialog();
        if (window.Repaired && !_closed && !_closing) await RefreshAsync(selected?.Profile.AppId);
    }
    private async void OnReplace(object sender, RoutedEventArgs e) => await OpenLibraryAsync(true);
    private async void OnStopPreview(object sender, RoutedEventArgs e) => await _intents.StopAsync();
    private async void OnPreviewBound(object sender, RoutedEventArgs e)
    {
        if (Selected?.Binding.Binding?.SceneId is not string id) return;
        PreviewResult result = await _intents.PlayAsync(id);
        if (_closed || _closing || !_intents.IsCurrent(result)) return;
        if (result.Outcome == PreviewOutcome.Unavailable) { PreviewStatusText.Text = ProductText.Get("TheWorkIsUnavailableTheBindingHasBeen2"); PreviewBoundButton.IsEnabled = false; }
        else if (Selected?.Binding.Binding?.SceneId == id && PreviewBoundButton.IsEnabled)
        {
            FocusManager.SetFocusedElement(this, PreviewBoundButton);
            if (IsActive) PreviewBoundButton.Focus();
        }
    }
}
