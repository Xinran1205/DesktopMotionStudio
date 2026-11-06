namespace DesktopMotionStudio.Core.Distribution;

public static class InstalledLayout
{
    internal const string StudioName = "DesktopMotionStudio.Studio.exe";
    internal const string LauncherName = "DesktopMotionStudio.Launcher.exe";
    internal const string Versions = "Versions";
    internal const string ProductDirectory = "DesktopMotionStudio";
    internal const string StartMenuName = "Desktop Motion Studio.lnk";

    internal static string CurrentUserRoot() =>
        ResolveUserRoot() ?? throw new IOException("The current user's program directory is unavailable.");

    private static string? ResolveUserRoot()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.IsPathFullyQualified(local) ? Path.Combine(local, "Programs", ProductDirectory) : null;
    }

    internal static string LauncherPath(string root) => Path.Combine(root, "Launcher", LauncherName);

    public static string LauncherForBundle(string bundle) =>
        ResolveUserRoot() is string root ? LauncherForBundle(bundle, root) : LauncherPath(bundle);

    internal static string LauncherForBundle(string bundle, string installRoot)
    {
        string generation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bundle));
        if (InstalledSelection.ParseVersion(Path.GetFileName(generation)) is not null &&
            string.Equals(Path.GetDirectoryName(generation), Path.Combine(installRoot, Versions), StringComparison.OrdinalIgnoreCase))
            return LauncherPath(installRoot);
        return LauncherPath(bundle);
    }
}
