using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace DesktopMotionStudio.Studio;

internal static class StudioTheme
{
    private static readonly ConditionalWeakTable<Application, Palette> Palettes = new();

    internal static void Apply(Application application, string choice) =>
        Palettes.GetValue(application, app => new Palette(app)).SetChoice(choice);

    private sealed class Palette
    {
        private readonly Application _app;
        private string _choice = "system";

        internal Palette(Application app)
        {
            _app = app;
            SystemParameters.StaticPropertyChanged += OnSystemPropertyChanged;
            app.Exit += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemPropertyChanged;
        }

        internal void SetChoice(string choice)
        {
            _choice = choice;
            SetPalette(refresh: false);
        }

        private void OnSystemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SystemParameters.HighContrast) || _app.Dispatcher.HasShutdownStarted) return;
            // Run after WPF has applied its OS palette transition. Restore the explicit
            // Studio choice on exit from contrast mode even if WPF selected System.
            _app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => SetPalette(refresh: true));
        }

        private void SetPalette(bool refresh)
        {
#pragma warning disable WPF0001 // ThemeMode is isolated here and verified against the pinned desktop runtime.
            ThemeMode mode = _choice switch
            {
                "light" => ThemeMode.Light,
                "dark" => ThemeMode.Dark,
                _ => ThemeMode.System
            };
            if (refresh && _app.ThemeMode == mode) _app.ThemeMode = ThemeMode.None;
            _app.ThemeMode = mode;
#pragma warning restore WPF0001
        }
    }
}
