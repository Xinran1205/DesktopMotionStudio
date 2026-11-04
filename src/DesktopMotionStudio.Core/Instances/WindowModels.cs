using DesktopMotionStudio.Core.Launch;

namespace DesktopMotionStudio.Core.Instances;

public enum InstanceOutcome
{
    ActivatedExisting,
    WindowReady,
    ForegroundDenied,
    NoUsableWindow,
    AmbiguousWindow,
    TimedOut,
    ProcessExited,
    IdentityChanged,
    IdentityUnavailable,
    InvalidTarget,
    LaunchFailed,
    GateTimedOut,
    Canceled
}

public enum ProcessVerificationStatus { Confirmed, Exited, IdentityChanged, Unavailable }

public enum WindowActivationStatus { Activated, ForegroundDenied, WindowUnavailable, IdentityChanged }

public sealed record ProcessStamp(
    int ProcessId,
    long CreationFileTimeUtc,
    uint SessionId,
    LaunchTargetIdentity Identity);

public sealed record ProcessScanResult(
    IReadOnlyList<ProcessStamp> Matches,
    bool IsComplete = true,
    int? NativeErrorCode = null);

public sealed record ProcessVerificationResult(
    ProcessVerificationStatus Status,
    ProcessStamp? Process = null,
    int? NativeErrorCode = null);

public sealed record WindowCandidate(
    nint Handle,
    ProcessStamp Process,
    bool IsVisible,
    bool IsCloaked,
    bool IsMinimized,
    bool IsOwned,
    bool IsToolWindow,
    bool IsForeground,
    bool IsMainWindow);

public sealed record WindowScanResult(
    IReadOnlyList<WindowCandidate> Candidates,
    bool IsComplete = true,
    int? NativeErrorCode = null);

public sealed record WindowActivationResult(
    WindowActivationStatus Status,
    int? NativeErrorCode = null);

public sealed record InstanceResult(
    InstanceOutcome Outcome,
    ProcessStamp? Process = null,
    nint? WindowHandle = null,
    TimeSpan ElapsedWait = default,
    int? NativeErrorCode = null);

public interface IInstanceClock
{
    long Timestamp { get; }
    TimeSpan ElapsedSince(long timestamp);
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemInstanceClock : IInstanceClock
{
    public long Timestamp => System.Diagnostics.Stopwatch.GetTimestamp();
    public TimeSpan ElapsedSince(long timestamp) => System.Diagnostics.Stopwatch.GetElapsedTime(timestamp);
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
