using System.Windows;
using System.Windows.Controls;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Studio;

internal sealed record BindingAppRow(ApplicationProfile Profile, bool CanBind)
{
    public string Name => Profile.DisplayName;
    public string Status => CanBind ? UiText.Kind(Profile.Target?.Kind) + ProductText.Get("ReadyToBind") : ProductText.Get("ConfirmAWorkingLaunchEntryUnderMyApps");
}

public partial class BindingPickerWindow : Window
{
    private readonly IStudioStateStore _store;
    private readonly SceneWorkspace _workspace;
    private readonly BindingService _bindings;
    private readonly string _sceneId;
    private string? _preferredApp;
    private BindingAppRow[] _rows = [];
    private bool _readable, _playable, _saving, _closed;
    private int _generation;
    internal BindingMutationResult? Result { get; private set; }
    internal string OutcomeMessage => StatusText.Text;

    internal BindingPickerWindow(IStudioStateStore store, SceneWorkspace workspace, string sceneId, string? appId = null)
    {
        InitializeComponent(); _store = store; _workspace = workspace; _sceneId = sceneId; _preferredApp = appId; _bindings = workspace.CreateBindings(store);
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 80));
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 80));
        Loaded += async (_, _) => await RefreshAsync();
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
        Closed += (_, _) => { _closed = true; _generation++; };
    }

    internal async Task RefreshAsync()
    {
        if (_saving) return;
        int generation = ++_generation; _readable = false; UpdateEnabled(); StatusText.Text = ProductText.Get("LoadingAppsAndWorks");
        try
        {
            var loaded = await Task.Run(() =>
            {
                RepositoryResult state = _store.Load(); var scene = _workspace.FindPlayable(_sceneId);
                BindingAppRow[] rows = state.IsSuccess ? state.State!.Applications.Select(profile =>
                {
                    LaunchTarget? target = ApplicationRegistry.GetTarget(profile);
                    return new BindingAppRow(profile, target is not null && new LaunchTargetIdentityResolver().Resolve(target).Succeeded);
                }).ToArray() : [];
                return (state, scene, rows);
            });
            if (_closed || generation != _generation) return;
            _readable = loaded.state.IsSuccess; _playable = loaded.scene is not null; _rows = loaded.rows;
            SceneText.Text = ProductText.Format("BindingSelectedWork",
                loaded.scene is { } work ? SceneUiText.Name(work) : _sceneId);
            Filter();
            StatusText.Text = !_readable ? UiText.Storage(loaded.state.Error) : !_playable ? ProductText.Get("ThisWorkIsUnavailableTheExistingBindingHas") : _preferredApp is not null && BindingAppsList.SelectedItem is null ? ProductText.Get("ThePreviousAppIsNoLongerListedChoose") : ProductText.Get("ChooseASavedAppWithAWorkingLaunch");
        }
        catch (Exception)
        {
            if (_closed || generation != _generation) return;
            _rows = []; _readable = false; Filter(); StatusText.Text = ProductText.Get("CouldNotLoadTheAppsOrWorksRefresh");
        }
        if (!_closed && generation == _generation) UpdateEnabled();
    }

    private void Filter()
    {
        string query = SearchBox.Text.Trim();
        BindingAppRow[] visible = _rows.Where(row => row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        BindingAppsList.ItemsSource = visible;
        BindingAppsList.SelectedItem = visible.FirstOrDefault(row => string.Equals(row.Profile.AppId, _preferredApp, StringComparison.OrdinalIgnoreCase));
        EmptyText.Text = _rows.Length == 0 ? ProductText.Get("NoAppsAddedAddAnAppAndConfirm2") : ProductText.Get("NoMatchingSavedApps");
        EmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed; UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        SaveButton.IsEnabled = !_saving && _readable && _playable && BindingAppsList.SelectedItem is BindingAppRow { CanBind: true };
        CancelButton.IsEnabled = SearchBox.IsEnabled = BindingAppsList.IsEnabled = RefreshButton.IsEnabled = !_saving;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_saving || !SaveButton.IsEnabled || BindingAppsList.SelectedItem is not BindingAppRow row) return;
        _saving = true; _generation++; UpdateEnabled(); StatusText.Text = ProductText.Get("SavingBinding");
        try
        {
            BindingMutationResult result = await Task.Run(() => _bindings.Bind(row.Profile.AppId, _sceneId));
            if (_closed) return;
            Result = result; StatusText.Text = SceneUiText.Binding(result);
            if (!result.Succeeded) return;
            var current = await Task.Run(() => (_store.Load(), _workspace.FindPlayable(_sceneId)));
            if (_closed) return;
            if (!current.Item1.IsSuccess) { _readable = false; StatusText.Text = UiText.Storage(current.Item1.Error); return; }
            ApplicationBinding? saved = current.Item1.State!.Bindings.SingleOrDefault(binding => string.Equals(binding.AppId, row.Profile.AppId, StringComparison.OrdinalIgnoreCase));
            if (saved?.BindingId != result.Binding?.BindingId || !string.Equals(saved?.SceneId, _sceneId, StringComparison.OrdinalIgnoreCase) || saved?.Enabled != true)
            { StatusText.Text = ProductText.Get("AnotherOperationChangedTheConfigurationRefreshAndCheck"); return; }
            if (current.Item2 is null) { _playable = false; StatusText.Text = ProductText.Get("BindingSavedThisWorkIsCurrentlyUnavailableRefresh"); return; }
            _saving = false; DialogResult = true;
        }
        catch (Exception) { if (!_closed) StatusText.Text = ProductText.Get("CouldNotFinishBindingOrReloadTheConfiguration"); }
        finally { _saving = false; if (!_closed) UpdateEnabled(); }
    }

    private void OnSearch(object sender, TextChangedEventArgs e) { if (BindingAppsList is not null) Filter(); }
    private void OnSelection(object sender, SelectionChangedEventArgs e) { if (BindingAppsList.SelectedItem is BindingAppRow row) _preferredApp = row.Profile.AppId; if (SaveButton is not null) UpdateEnabled(); }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();
}
