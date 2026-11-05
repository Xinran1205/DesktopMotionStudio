using System.IO;
using System.Windows;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Studio;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--discovery-worker"]) return DiscoveryWorker.Run();
        string? stateFile = null;
        string catalogRoot = Path.Combine(AppContext.BaseDirectory, "Scenes");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !seen.Add(args[i]) || !Path.IsPathFullyQualified(args[i + 1])) return 2;
            switch (args[i])
            {
                case "--state-file": stateFile = Path.GetFullPath(args[i + 1]); break;
                case "--catalog-root": catalogRoot = Path.GetFullPath(args[i + 1]); break;
                default: return 2;
            }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DesktopMotionStudio.Studio;component/Styles.xaml", UriKind.Relative)
        });
        var store = stateFile is null ? StudioStateRepository.ForCurrentUser() : new StudioStateRepository(stateFile);
        return app.Run(new MainWindow(store, catalogRoot));
    }
}
