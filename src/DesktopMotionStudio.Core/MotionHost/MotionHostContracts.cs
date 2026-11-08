using DesktopMotionStudio.Core.Scenes;
using System.Text.Json.Serialization;

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
    Shutdown,
    Completed
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
    RendererFailed,
    UnsupportedPlayback
}

public enum MotionHostOutcome { NotStarted, HandedOff, Skipped, TimedOut, Failed, Exited, Completed }
public enum MotionHostMode { Launch, Preview }

public sealed class MotionHostProtocolException(MotionHostError error) : IOException("MotionHost protocol request failed validation.")
{
    public MotionHostError Error { get; } = error;
}

public enum MotionHostPeer { Launcher, Host }

public enum MotionHostProtocolState { AwaitingReady, AwaitingStart, Starting, Visible, HandoffRequested, Closing, Closed }

public sealed record MotionHostStartRequest(
    Guid RequestId,
    string SceneId,
    string SceneVersion,
    SceneDisplayMode DisplayMode,
    int MaximumVisibleMilliseconds = 10_000,
    MotionHostMode Mode = MotionHostMode.Launch,
    ScenePlaybackDefinition? Playback = null)
{
    public bool IsValid => RequestId != Guid.Empty &&
        new MotionHostStartPayload(SceneId, SceneVersion, DisplayMode, MaximumVisibleMilliseconds, Mode, Playback).IsValid;

    public MotionHostFrame ToStartFrame() => new(
        MotionHostFrame.CurrentProtocolVersion,
        RequestId,
        MotionHostMessageKind.Start,
        new MotionHostStartPayload(SceneId, SceneVersion, DisplayMode, MaximumVisibleMilliseconds, Mode, Playback));
}

public sealed record MotionHostStartPayload(
    [property: JsonRequired] string SceneId,
    [property: JsonRequired] string SceneVersion,
    [property: JsonRequired] SceneDisplayMode DisplayMode,
    [property: JsonRequired] int MaximumVisibleMilliseconds,
    [property: JsonRequired] MotionHostMode Mode = MotionHostMode.Launch,
    ScenePlaybackDefinition? Playback = null)
{
    [JsonIgnore]
    public bool IsValid => SceneManifestReader.IsValidSceneId(SceneId) && SceneManifestReader.IsValidSceneVersion(SceneVersion) &&
        DisplayMode is SceneDisplayMode.Full or SceneDisplayMode.Centered && Enum.IsDefined(Mode) && MaximumVisibleMilliseconds is > 0 and <= 10_000 &&
        (Playback is null ? Mode == MotionHostMode.Launch : Playback.IsValid && Playback.SceneId == SceneId && Playback.SceneVersion == SceneVersion &&
            (Mode == MotionHostMode.Launch || MaximumVisibleMilliseconds >= Playback.DurationMilliseconds + 500));
}

public sealed record MotionHostFrame(
    [property: JsonRequired] int ProtocolVersion,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] MotionHostMessageKind Kind,
    MotionHostStartPayload? Start = null,
    MotionHostError? Error = null,
    int? NativeErrorCode = null)
{
    public const int CurrentProtocolVersion = 3;
}

public interface IMotionHostSession : IAsyncDisposable
{
    MotionHostError? FailureReason { get; }
    int? NativeErrorCode { get; }
    int? HostProcessId { get; }
    Task<MotionHostOutcome?> Started { get; }
    Task<MotionHostOutcome> Completion { get; }
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
            if (!frame.Start.IsValid)
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
        out MotionHostProtocolState next,
        MotionHostMode mode = MotionHostMode.Launch,
        bool finiteLaunch = false)
    {
        next = current;
        if (!Enum.IsDefined(current) || !Enum.IsDefined(sender) || !Enum.IsDefined(kind) || !Enum.IsDefined(mode)) return false;
        if (kind == MotionHostMessageKind.Completed)
        {
            bool closingNormally = current is MotionHostProtocolState.Visible or MotionHostProtocolState.Closing ||
                finiteLaunch && current == MotionHostProtocolState.HandoffRequested;
            if (sender != MotionHostPeer.Host || !closingNormally || (mode == MotionHostMode.Launch && !finiteLaunch)) return false;
            next = MotionHostProtocolState.Closed;
            return true;
        }
        if (mode == MotionHostMode.Preview && kind == MotionHostMessageKind.Handoff) return false;

        if (finiteLaunch && mode == MotionHostMode.Launch && sender == MotionHostPeer.Host &&
            (current == MotionHostProtocolState.Closing &&
                kind is MotionHostMessageKind.SkipRequested or MotionHostMessageKind.TimedOut or MotionHostMessageKind.Failed ||
             current == MotionHostProtocolState.HandoffRequested && kind == MotionHostMessageKind.TimedOut))
        {
            next = MotionHostProtocolState.Closing;
            return true;
        }

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
