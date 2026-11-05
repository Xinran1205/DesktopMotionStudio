namespace DesktopMotionStudio.Core.Launch;

public sealed class LaunchTargetIdentityResolver
{
    public const int MaxArgumentCount = 256;
    public const int MaxCommandLineLength = 30_000;
    public const int MaxAumidLength = 129; // APPLICATION_USER_MODEL_ID_MAX_LENGTH includes the null terminator.

    public LaunchTargetResolution Resolve(LaunchTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!OperatingSystem.IsWindows()) return Invalid();

        return target switch
        {
            ExecutableTarget executable => ResolveExecutable(executable),
            ShortcutTarget shortcut => ResolveShortcut(shortcut),
            PackagedAppTarget packaged => ResolvePackaged(packaged),
            _ => Invalid()
        };
    }

    public static bool IsValidAumid(string? aumid) =>
        !string.IsNullOrWhiteSpace(aumid) && aumid.Length <= MaxAumidLength && !aumid.Any(char.IsControl) &&
        aumid.IndexOf('!') > 0 && aumid.IndexOf('!') < aumid.Length - 1 &&
        aumid.IndexOf('!') == aumid.LastIndexOf('!');

    private static LaunchTargetResolution ResolveExecutable(ExecutableTarget target)
    {
        if (!IsLocalPath(target.Path) ||
            !string.Equals(Path.GetExtension(target.Path), ".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(target.Path) || target.Arguments is null || target.Arguments.Count > MaxArgumentCount ||
            target.Arguments.Any(argument => argument is null) ||
            target.Arguments.Sum(argument => (long)argument.Length + 1) > MaxCommandLineLength)
            return Invalid();

        string path;
        try { path = Path.GetFullPath(target.Path); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return Invalid(); }

        string workingDirectory = target.WorkingDirectory ?? Path.GetDirectoryName(path)!;
        if (!IsLocalPath(workingDirectory) || !Directory.Exists(workingDirectory)) return Invalid();

        string[] arguments = target.Arguments.ToArray();
        return Resolved(new ResolvedExecutableLaunch(
            new ExecutableLaunchIdentity(path), Array.AsReadOnly(arguments), Path.GetFullPath(workingDirectory)));
    }

    private static LaunchTargetResolution ResolveShortcut(ShortcutTarget target)
    {
        if (!IsLocalPath(target.Path) ||
            !File.Exists(target.Path) || !string.Equals(Path.GetExtension(target.Path), ".lnk", StringComparison.OrdinalIgnoreCase))
            return Invalid();

        ShellLinkTargetReadResult link = ShellLinkTargetReader.Read(target.Path);
        if (link.Status != LaunchTargetResolutionStatus.Resolved || link.ExecutablePath is null || link.Arguments is null)
            return new LaunchTargetResolution(null, link.Status, link.NativeErrorCode);

        var executable = new ExecutableTarget(link.ExecutablePath, link.Arguments, link.WorkingDirectory);
        LaunchTargetResolution resolution = ResolveExecutable(executable);
        if (!resolution.Succeeded || resolution.Target is not ResolvedExecutableLaunch resolved) return resolution;
        return Resolved(resolved);
    }

    private static LaunchTargetResolution ResolvePackaged(PackagedAppTarget target) =>
        IsValidAumid(target.Aumid)
            ? Resolved(new ResolvedPackagedLaunch(new PackagedAppLaunchIdentity(target.Aumid)))
            : Invalid();

    private static LaunchTargetResolution Resolved(ResolvedLaunchTarget target) =>
        new(target, LaunchTargetResolutionStatus.Resolved);

    private static LaunchTargetResolution Invalid() =>
        new(null, LaunchTargetResolutionStatus.InvalidTarget);

    public static bool IsLocalPath(string? path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)) return false;
            string normalized = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(normalized);
            if (root is null || root.Length < 3 || root[1] != ':') return false;
            if (new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram)) return false;

            // Inspect each component before descending: even a local drive can contain a
            // junction/symlink to a network share. GetAttributes inspects the final link
            // itself; probing the complete path first could already follow that link.
            string current = root;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            foreach (string component in normalized[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return false; }
    }
}
