using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application();
        var window = new Window
        {
            Title = "Desktop Motion Studio WPF feasibility",
            Width = 640,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBlock
            {
                Text = "Desktop Motion Studio\nWPF window lifecycle verification",
                FontSize = 24,
                Margin = new Thickness(32)
            }
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
        window.Loaded += (_, _) => timer.Start();
        return app.Run(window);
    }
}
