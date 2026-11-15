using System.Windows;
using System.Windows.Controls;
using DesktopMotionStudio.Core.Preview;

namespace DesktopMotionStudio.Studio;

internal sealed class PreviewPresenter : IDisposable
{
    private readonly PreviewCoordinator _coordinator;
    private readonly Window _window;
    private readonly TextBlock _status;
    private readonly Button _stop;
    private bool _closed;

    internal PreviewPresenter(PreviewCoordinator coordinator, Window window, TextBlock status, Button stop)
    {
        _coordinator = coordinator; _window = window; _status = status; _stop = stop;
        coordinator.StateChanged += OnChanged;
        Apply();
    }

    private void OnChanged()
    {
        if (_window.Dispatcher.HasShutdownStarted) return;
        _window.Dispatcher.BeginInvoke(() => { if (!_closed) Apply(); });
    }

    private void Apply()
    {
        PreviewSnapshot state = _coordinator.Current;
        _status.Text = SceneUiText.Preview(state);
        _stop.IsEnabled = state.Phase is PreviewPhase.Starting or PreviewPhase.Playing;
    }

    public void Dispose() { _closed = true; _coordinator.StateChanged -= OnChanged; }
}
