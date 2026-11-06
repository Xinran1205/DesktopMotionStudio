using System.Windows;
using System.Windows.Controls;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Studio;

public partial class SettingsWindow : Window
{
    private readonly StudioSettingsService _service;
    private StudioSettings? _expected;
    private bool _busy;
    private bool _closed;

    internal SettingsWindow(StudioSettingsService service)
    {
        InitializeComponent();
        _service = service;
        Loaded += async (_, _) => await LoadAsync();
        Closing += (_, args) => { if (_busy) args.Cancel = true; };
        Closed += (_, _) => _closed = true;
    }

    private async Task LoadAsync()
    {
        SettingsResult read;
        try { read = await Task.Run(_service.Read); }
        catch (Exception) { read = new(SettingsStatus.StorageError, RepositoryError: RepositoryError.IoError); }
        if (_closed) return;
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
        CancelButton.IsEnabled = !_busy;
    }

    private async void OnSave(object sender, RoutedEventArgs args)
    {
        if (_busy || _expected is not StudioSettings expected ||
            ThemeBox.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
        var desired = new StudioSettings(theme, MuteBox.IsChecked == true);
        _busy = true;
        UpdateEnabled();
        PreferenceStatus.Text = "正在保存设置…";
        try
        {
            SettingsResult result = await Task.Run(() => _service.Apply(expected, desired));
            if (result.Status is SettingsStatus.Applied or SettingsStatus.Unchanged && result.Settings is not null)
            {
                ShowValues(result.Settings);
                try { StudioTheme.Apply(Application.Current, result.Settings.Theme); }
                catch (Exception)
                {
                    PreferenceStatus.Text = "设置已保存。主题将在重新打开 Studio 后应用。";
                    return;
                }
                _busy = false;
                DialogResult = true;
                return;
            }
            ShowValues(result.Settings ?? expected);
            PreferenceStatus.Text = result.Status switch
            {
                SettingsStatus.Conflict => "设置已在另一窗口更新。已载入当前值，请重新选择。",
                SettingsStatus.StorageError => UiText.Storage(result.RepositoryError),
                _ => "设置值无效。"
            };
        }
        catch (Exception) { PreferenceStatus.Text = "无法确认设置已保存，请重试。"; }
        finally { _busy = false; if (!_closed) UpdateEnabled(); }
    }
}
