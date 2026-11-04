namespace DesktopMotionStudio.Core.Launch;

public abstract record LaunchTarget;

public sealed record ExecutableTarget(string Path, IReadOnlyList<string> Arguments, string? WorkingDirectory = null) : LaunchTarget;

public sealed record ShortcutTarget(string Path) : LaunchTarget;

public sealed record PackagedAppTarget(string Aumid) : LaunchTarget;

public abstract record LaunchTargetIdentity(string Value);

public sealed record ExecutableLaunchIdentity(string Path) : LaunchTargetIdentity(Path);

public sealed record PackagedAppLaunchIdentity(string Aumid) : LaunchTargetIdentity(Aumid);

public abstract record ResolvedLaunchTarget(LaunchTargetIdentity Identity);

public sealed record ResolvedExecutableLaunch(
    ExecutableLaunchIdentity ExecutableIdentity,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory) : ResolvedLaunchTarget(ExecutableIdentity);

public sealed record ResolvedPackagedLaunch(PackagedAppLaunchIdentity PackagedIdentity) : ResolvedLaunchTarget(PackagedIdentity);

public enum LaunchTargetResolutionStatus { Resolved, InvalidTarget, ResolutionFailed }

public sealed record LaunchTargetResolution(ResolvedLaunchTarget? Target, LaunchTargetResolutionStatus Status, int? NativeErrorCode = null)
{
    public bool Succeeded => Target is not null && Status == LaunchTargetResolutionStatus.Resolved;
}

public enum LaunchStatus { Started, Activated, InvalidTarget, StartFailed }

public sealed record LaunchResult(LaunchStatus Status, int? ProcessId = null, int? NativeErrorCode = null, long? ProcessCreationFileTimeUtc = null)
{
    public bool Succeeded => Status is LaunchStatus.Started or LaunchStatus.Activated;
}
