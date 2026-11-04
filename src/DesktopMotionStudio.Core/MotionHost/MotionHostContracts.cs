using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Core.MotionHost;

public enum MotionHostMessageKind
{
    Ready,
    Start,
    Started,
    SkipRequested,
    Handoff,
    Finished,
    TimedOut,
    Failed,
    Shutdown
}

public enum MotionHostError
{
    None,
    InvalidArguments,
    UnsupportedProtocol,
    RequestMismatch,
    InvalidMessage,
    InvalidState,
    FrameTooLarge,
    ConnectionTimeout,
    ParentDisconnected,
    StartFailed,
    HostInitializationFailed,
    TimedOut,
    RendererFailed
}

public enum MotionHostOutcome { NotStarted, HandedOff, Skipped, TimedOut, Failed, Exited }

public enum MotionHostPeer { Launcher, Host }

public enum MotionHostProtocolState { AwaitingReady, AwaitingStart, Starting, Visible, HandoffRequested, Closing, Closed }

public sealed record MotionHostStartRequest(
    Guid RequestId,
    string SceneId,
    string SceneVersion,
    SceneDisplayMode DisplayMode,
    int MaximumVisibleMilliseconds = 15_000)
{
    public bool IsValid => RequestId != Guid.Empty &&
        SceneManifestReader.IsValidSceneId(SceneId) &&
        SceneManifestReader.IsValidSceneVersion(SceneVersion) &&
        DisplayMode is SceneDisplayMode.Full or SceneDisplayMode.Centered &&
        MaximumVisibleMilliseconds is > 0 and <= 15_000;

    public MotionHostFrame ToStartFrame() => new(
        MotionHostFrame.CurrentProtocolVersion,
        RequestId,
        MotionHostMessageKind.Start,
        new MotionHostStartPayload(SceneId, SceneVersion, DisplayMode, MaximumVisibleMilliseconds));
}

public sealed record MotionHostStartPayload(
    string SceneId,
    string SceneVersion,
    SceneDisplayMode DisplayMode,
    int MaximumVisibleMilliseconds);

public sealed record MotionHostFrame(
    int ProtocolVersion,
    Guid RequestId,
    MotionHostMessageKind Kind,
    MotionHostStartPayload? Start = null,
    MotionHostError? Error = null,
    int? NativeErrorCode = null)
{
    public const int CurrentProtocolVersion = 1;
}

public interface IMotionHostSession : IAsyncDisposable
{
    MotionHostError? FailureReason { get; }
    int? NativeErrorCode { get; }
    Task<MotionHostOutcome> HandoffAsync(CancellationToken cancellationToken = default);
    Task<MotionHostOutcome> ShutdownAsync(CancellationToken cancellationToken = default);
}

public interface IMotionHostSessionFactory
{
    IMotionHostSession Start(MotionHostStartRequest request);
}

public sealed record MotionHostLifecycleResult(
    DesktopMotionStudio.Core.Instances.InstanceResult TargetResult,
    MotionHostOutcome HostOutcome,
    MotionHostError? HostError = null,
    int? NativeErrorCode = null);

public static class MotionHostFrameValidator
{
    public static MotionHostError ValidateEnvelope(MotionHostFrame frame, Guid expectedRequestId)
    {
        if (frame.ProtocolVersion != MotionHostFrame.CurrentProtocolVersion)
            return MotionHostError.UnsupportedProtocol;
        if (expectedRequestId == Guid.Empty || frame.RequestId == Guid.Empty)
            return MotionHostError.InvalidMessage;
        if (frame.RequestId != expectedRequestId)
            return MotionHostError.RequestMismatch;
        if (!Enum.IsDefined(frame.Kind))
            return MotionHostError.InvalidMessage;
        if (frame.Kind == MotionHostMessageKind.Start)
        {
            if (frame.Start is null || frame.Error is not null || frame.NativeErrorCode is not null)
                return MotionHostError.InvalidMessage;
            MotionHostStartPayload payload = frame.Start;
            if (!SceneManifestReader.IsValidSceneId(payload.SceneId) ||
                !SceneManifestReader.IsValidSceneVersion(payload.SceneVersion) ||
                payload.DisplayMode is not (SceneDisplayMode.Full or SceneDisplayMode.Centered) ||
                payload.MaximumVisibleMilliseconds is <= 0 or > 15_000)
                return MotionHostError.InvalidMessage;
        }
        else if (frame.Start is not null)
        {
            return MotionHostError.InvalidMessage;
        }

        if (frame.Kind == MotionHostMessageKind.Failed)
        {
            if (frame.Error is null || frame.Error == MotionHostError.None || !Enum.IsDefined(frame.Error.Value))
                return MotionHostError.InvalidMessage;
        }
        else if (frame.Error is not null || frame.NativeErrorCode is not null)
        {
            return MotionHostError.InvalidMessage;
        }

        return MotionHostError.None;
    }

    public static bool TryAdvance(
        MotionHostProtocolState current,
        MotionHostPeer sender,
        MotionHostMessageKind kind,
        out MotionHostProtocolState next)
    {
        next = current;
        if (!Enum.IsDefined(current) || !Enum.IsDefined(sender) || !Enum.IsDefined(kind)) return false;

        if (kind == MotionHostMessageKind.Failed && current is not (MotionHostProtocolState.Closed or MotionHostProtocolState.Closing))
        {
            next = MotionHostProtocolState.Closing;
            return true;
        }

        (MotionHostPeer Sender, MotionHostMessageKind Kind, MotionHostProtocolState From, MotionHostProtocolState To)[] transitions =
        [
            (MotionHostPeer.Host, MotionHostMessageKind.Ready, MotionHostProtocolState.AwaitingReady, MotionHostProtocolState.AwaitingStart),
            (MotionHostPeer.Launcher, MotionHostMessageKind.Start, MotionHostProtocolState.AwaitingStart, MotionHostProtocolState.Starting),
            (MotionHostPeer.Host, MotionHostMessageKind.Started, MotionHostProtocolState.Starting, MotionHostProtocolState.Visible),
            (MotionHostPeer.Host, MotionHostMessageKind.SkipRequested, MotionHostProtocolState.Visible, MotionHostProtocolState.Closing),
            (MotionHostPeer.Host, MotionHostMessageKind.SkipRequested, MotionHostProtocolState.HandoffRequested, MotionHostProtocolState.Closing),
            (MotionHostPeer.Host, MotionHostMessageKind.TimedOut, MotionHostProtocolState.Visible, MotionHostProtocolState.Closing),
            (MotionHostPeer.Launcher, MotionHostMessageKind.Handoff, MotionHostProtocolState.Visible, MotionHostProtocolState.HandoffRequested),
            (MotionHostPeer.Launcher, MotionHostMessageKind.Shutdown, MotionHostProtocolState.AwaitingReady, MotionHostProtocolState.Closing),
            (MotionHostPeer.Launcher, MotionHostMessageKind.Shutdown, MotionHostProtocolState.AwaitingStart, MotionHostProtocolState.Closing),
            (MotionHostPeer.Launcher, MotionHostMessageKind.Shutdown, MotionHostProtocolState.Starting, MotionHostProtocolState.Closing),
            (MotionHostPeer.Launcher, MotionHostMessageKind.Shutdown, MotionHostProtocolState.Visible, MotionHostProtocolState.Closing),
            (MotionHostPeer.Host, MotionHostMessageKind.Finished, MotionHostProtocolState.HandoffRequested, MotionHostProtocolState.Closed),
            (MotionHostPeer.Host, MotionHostMessageKind.Finished, MotionHostProtocolState.Closing, MotionHostProtocolState.Closed)
        ];

        foreach (var transition in transitions)
        {
            if (transition.Sender != sender || transition.Kind != kind || transition.From != current) continue;
            next = transition.To;
            return true;
        }
        return false;
    }
}
