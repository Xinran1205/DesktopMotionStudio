using System.IO;
using System.Windows;
using DesktopMotionStudio.Core.State;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Diagnostics;
using DesktopMotionStudio.Core.Distribution;

namespace DesktopMotionStudio.Studio;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--discovery-worker"]) return DiscoveryWorker.Run();
        string? stateFile = null;
        string? desktopRoot = null;
        string catalogRoot = Path.Combine(AppContext.BaseDirectory, "Scenes");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !seen.Add(args[i]) || !Path.IsPathFullyQualified(args[i + 1])) return 2;
            switch (args[i])
            {
                case "--state-file": stateFile = Path.GetFullPath(args[i + 1]); break;
                case "--catalog-root": catalogRoot = Path.GetFullPath(args[i + 1]); break;
                case "--desktop-root": desktopRoot = Path.GetFullPath(args[i + 1]); break;
                default: return 2;
            }
        }
        if (desktopRoot is not null && (stateFile is null || !IsFixtureDesktop(stateFile, desktopRoot))) return 2;
        string profile = stateFile ?? StudioStateRepository.CurrentUserPath();
        var language = new StudioLanguagePreference(profile);
        ProductText.UseLanguage(language.Read());
        var store = new StudioStateRepository(profile);
        ManagedShortcutService? shortcuts = stateFile is not null && desktopRoot is null ? null : new(
            store, catalogRoot, Path.Combine(Path.GetDirectoryName(profile)!, "managed-shortcuts"),
            InstalledLayout.LauncherForBundle(AppContext.BaseDirectory),
            () => desktopRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify));

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        SettingsResult preferences = new StudioSettingsService(store).Read();
        StudioTheme.Apply(app, preferences.Settings?.Theme ?? StudioSettings.Default.Theme);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DesktopMotionStudio.Studio;component/Styles.xaml", UriKind.Relative)
        });
        var diagnostics = new DiagnosticLogStore(stateFile is null ? DiagnosticLogStore.CurrentUserDirectory() :
            Path.Combine(Path.GetDirectoryName(profile)!, "logs"));
        TestLaunchClient? testLaunch = stateFile is null && !seen.Contains("--catalog-root")
            ? new(InstalledLayout.LauncherForBundle(AppContext.BaseDirectory)) : null;
        return app.Run(new MainWindow(store, catalogRoot, shortcuts: shortcuts, diagnostics: diagnostics,
            testLaunch: testLaunch, language: language, display: new MotionDisplayPreference(profile)));
    }

    private static bool IsFixtureDesktop(string stateFile, string desktop)
    {
        string? directory = Path.GetDirectoryName(stateFile);
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (directory is null || !Path.GetFileName(directory).StartsWith("dms-f10-", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(directory), temp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stateFile, StudioStateRepository.CurrentUserPath(), StringComparison.OrdinalIgnoreCase) ||
            !LaunchTargetIdentityResolver.IsLocalPath(stateFile) || !LaunchTargetIdentityResolver.IsLocalPath(desktop))
            return false;
        string realDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrEmpty(realDesktop) &&
            (string.Equals(desktop, realDesktop, StringComparison.OrdinalIgnoreCase) || IsDescendant(realDesktop, desktop))) return false;
        return IsDescendant(directory, desktop);
    }

    private static bool IsDescendant(string parent, string path)
    {
        string relative = Path.GetRelativePath(parent, path);
        return relative is not ("." or "..") && !Path.IsPathRooted(relative) &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
