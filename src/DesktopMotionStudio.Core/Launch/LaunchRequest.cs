namespace DesktopMotionStudio.Core.Launch;

public abstract record LaunchTarget;

public sealed record ExecutableTarget(string Path, IReadOnlyList<string> Arguments, string? WorkingDirectory = null) : LaunchTarget;

public sealed record ShortcutTarget(string Path) : LaunchTarget;

public sealed record PackagedAppTarget(string Aumid) : LaunchTarget;

public enum LaunchStatus { Started, Activated, InvalidTarget, StartFailed }

public sealed record LaunchResult(LaunchStatus Status, int? ProcessId = null, int? NativeErrorCode = null)
{
    public bool Succeeded => Status is LaunchStatus.Started or LaunchStatus.Activated;
}
