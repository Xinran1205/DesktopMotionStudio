using System.Globalization;
using System.IO;
using System.Windows;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.Video;

namespace DesktopMotionStudio.Studio;

public partial class ImportMotionWindow : Window
{
    private readonly ImportedVideoStore _store;
    private readonly PreviewCoordinator _previews;
    private readonly ImportedVideoWork? _editing;
    private readonly CancellationTokenSource _cancel = new();
    private ImportedVideoStore.Draft? _draft;
    private Task _pending = Task.CompletedTask;
    private bool _closing, _allowClose;
    private long MaximumDuration => Math.Min(10_000, _editing?.SourceDurationMilliseconds ?? _draft?.Inspection.DurationMilliseconds ?? 0);
    private bool HasAudio => _editing?.HasAudio ?? _draft?.Inspection.HasAudio ?? false;
    internal string? SavedId { get; private set; }

    internal ImportMotionWindow(ImportedVideoStore store, PreviewCoordinator previews, string source, ImportedVideoWork? editing = null)
    {
        InitializeComponent();
        _store = store;
        _previews = previews;
        _editing = editing;
        if (editing is not null)
        {
            Heading.Text = Title = ProductText.Get("EditImport");
            ShowOptions(editing.Options);
        }
        else Loaded += (_, _) => _pending = PrepareAsync(source);
        Closing += async (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            IsEnabled = false;
            _cancel.Cancel();
            await _previews.StopAsync();
            await _pending;
            _allowClose = true;
            if (SavedId is not null) DialogResult = true;
            else Close();
        };
        Closed += (_, _) => { _draft?.Dispose(); _cancel.Dispose(); };
    }

    private async Task PrepareAsync(string source)
    {
        Status.Text = ProductText.Get("ImportChecking");
        try
        {
            var inspector = new VideoInspector(Path.Combine(AppContext.BaseDirectory, "MotionHost", VideoPlaybackRequest.WorkerName));
            var result = await Task.Run(() => _store.PrepareAsync(source, inspector, _cancel.Token));
            _draft = result.Value;
            if (_closing) return;
            if (_draft is null) { Status.Text = ErrorText(result.Error); return; }
            string suggested = _draft.SuggestedName.Trim();
            if (suggested.Length > 80 || suggested.Length == 0 || suggested.Any(char.IsControl))
                suggested = ProductText.Get("ImportedMotion");
            ShowOptions(new(suggested, (int)MaximumDuration, VideoFit.Complete, false));
        }
        catch (OperationCanceledException) { }
    }

    private void ShowOptions(ImportedVideoOptions options)
    {
        WorkName.Text = options.Name;
        Duration.Text = Seconds(options.DurationMilliseconds);
        DurationHint.Text = ProductText.Format("ImportDurationLimit", Seconds(MaximumDuration));
        Fit.SelectedIndex = options.Fit == VideoFit.Complete ? 0 : 1;
        Sound.IsChecked = options.Sound;
        Sound.IsEnabled = HasAudio;
        Fields.IsEnabled = Preview.IsEnabled = Save.IsEnabled = true;
        Status.Text = "";
    }

    private static string Seconds(long milliseconds) => (milliseconds / 1000m).ToString("0.###", CultureInfo.CurrentCulture);

    private ImportedVideoOptions? ReadOptions()
    {
        string name = WorkName.Text.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl) ||
            !decimal.TryParse(Duration.Text, NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out decimal seconds) ||
            seconds < 1 || seconds > MaximumDuration / 1000m || seconds * 1000 != decimal.Truncate(seconds * 1000))
        {
            Status.Text = ErrorText(ImportError.InvalidOptions);
            return null;
        }
        return new(name, (int)(seconds * 1000), Fit.SelectedIndex == 0 ? VideoFit.Complete : VideoFit.Fill, HasAudio && Sound.IsChecked == true);
    }

    private async void OnPreview(object sender, RoutedEventArgs e)
    {
        if (_closing || ReadOptions() is not { } options) return;
        string? asset = _draft?.AssetPath ?? _editing?.AssetPath;
        string? id = _draft?.Id ?? _editing?.Id;
        if (asset is null || id is null) return;
        var clip = new VideoClip(id, asset, options.DurationMilliseconds, options.Fit, options.Sound);
        Status.Text = ProductText.Get("ImportPreviewing");
        var result = await _previews.PlayAsync(new(Guid.NewGuid(), null, SceneDisplayMode.Centered, clip), _cancel.Token);
        if (!_closing && Save.IsEnabled)
            Status.Text = result.Outcome == PreviewOutcome.Failed ? ErrorText(ImportError.InvalidMedia) : "";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_closing || ReadOptions() is not { } options) return;
        _pending = SaveAsync(options);
    }

    private async Task SaveAsync(ImportedVideoOptions options)
    {
        Fields.IsEnabled = Preview.IsEnabled = Save.IsEnabled = false;
        await _previews.StopAsync();
        try
        {
            var result = await Task.Run(() =>
            {
                _cancel.Token.ThrowIfCancellationRequested();
                return _editing is { } work ? _store.Edit(work.Id, work.Revision, options) : _draft!.Save(options, _cancel.Token);
            });
            if (result.Value is { } saved)
            {
                SavedId = saved.Id;
                if (!_closing) { _allowClose = true; DialogResult = true; }
            }
            else if (!_closing)
            {
                Status.Text = ErrorText(result.Error);
                Fields.IsEnabled = Preview.IsEnabled = Save.IsEnabled = true;
            }
        }
        catch (OperationCanceledException) { }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
    internal static string ErrorText(ImportError error) => ProductText.Get("ImportError" + error);
}
