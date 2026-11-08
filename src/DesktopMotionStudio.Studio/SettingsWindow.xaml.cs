using System.Windows;
using System.Windows.Controls;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Studio;

public partial class SettingsWindow : Window
{
    private readonly StudioSettingsService _service;
    private readonly StudioLanguagePreference? _language;
    private string _savedLanguage = ProductText.Language;
    private StudioSettings? _expected;
    private bool _busy;
    private bool _closed;

    internal SettingsWindow(StudioSettingsService service, StudioLanguagePreference? language = null)
    {
        InitializeComponent();
        _service = service;
        _language = language;
        Loaded += async (_, _) => await LoadAsync();
        Closing += (_, args) => { if (_busy) args.Cancel = true; };
        Closed += (_, _) => _closed = true;
    }

    internal bool RequiresLanguageRestart => _savedLanguage != ProductText.Language;

    private async Task LoadAsync()
    {
        SettingsResult read;
        try { read = await Task.Run(_service.Read); }
        catch (Exception) { read = new(SettingsStatus.StorageError, RepositoryError: RepositoryError.IoError); }
        if (_closed) return;
        _savedLanguage = _language?.Read() ?? ProductText.Language;
        LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => (string)item.Tag == _savedLanguage);
        ShowValues(read.Settings);
        PreferenceStatus.Text = read.Status == SettingsStatus.Ready ? "" : UiText.Storage(read.RepositoryError);
        if (_expected is not null) ThemeBox.Focus();
    }

    private void ShowValues(StudioSettings? current)
    {
        _expected = current;
        ThemeBox.SelectedItem = ThemeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == current?.Theme);
        MuteBox.IsChecked = current?.Muted == true;
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        ThemeBox.IsEnabled = MuteBox.IsEnabled = SaveButton.IsEnabled = !_busy && _expected is not null;
        LanguageBox.IsEnabled = SaveButton.IsEnabled && _language is not null;
        CancelButton.IsEnabled = !_busy;
    }

    private async void OnSave(object sender, RoutedEventArgs args)
    {
        if (_busy || _expected is not StudioSettings expected ||
            ThemeBox.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
        var desired = new StudioSettings(theme, MuteBox.IsChecked == true);
        string language = LanguageBox.SelectedItem is ComboBoxItem { Tag: string selected } ? selected : _savedLanguage;
        _busy = true;
        UpdateEnabled();
        PreferenceStatus.Text = ProductText.Get("SavingSettings");
        try
        {
            SettingsResult result = await Task.Run(() => _service.Apply(expected, desired));
            if (result.Status is SettingsStatus.Applied or SettingsStatus.Unchanged && result.Settings is not null)
            {
                ShowValues(result.Settings);
                string notice = "";
                if (_language is not null && language != _savedLanguage)
                {
                    if (await Task.Run(() => _language.TrySave(language))) _savedLanguage = language;
                    else notice = ProductText.Get("LanguageSaveFailed");
                }
                try { StudioTheme.Apply(Application.Current, result.Settings.Theme); }
                catch (Exception)
                {
                    string themeNotice = ProductText.Get("SettingsSavedReopenStudioToApplyTheTheme");
                    notice = notice.Length == 0 ? themeNotice : notice + " " + themeNotice;
                }
                if (notice.Length > 0)
                {
                    PreferenceStatus.Text = notice;
                    return;
                }
                _busy = false;
                DialogResult = true;
                return;
            }
            ShowValues(result.Settings ?? expected);
            PreferenceStatus.Text = result.Status switch
            {
                SettingsStatus.Conflict => ProductText.Get("SettingsChangedInAnotherWindowTheCurrentValues"),
                SettingsStatus.StorageError => UiText.Storage(result.RepositoryError),
                _ => ProductText.Get("InvalidSettings")
            };
        }
        catch (Exception) { PreferenceStatus.Text = ProductText.Get("CouldNotConfirmTheSaveTryAgain"); }
        finally { _busy = false; if (!_closed) UpdateEnabled(); }
    }
}
