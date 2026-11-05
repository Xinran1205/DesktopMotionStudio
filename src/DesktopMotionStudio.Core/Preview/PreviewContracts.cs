using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Core.Preview;

public sealed record PreviewRequest(Guid RequestId, ScenePlaybackDefinition Playback, SceneDisplayMode DisplayMode)
{
    public bool IsValid => RequestId != Guid.Empty && Playback is { IsValid: true } && DisplayMode is SceneDisplayMode.Centered or SceneDisplayMode.Full;
}

public enum PreviewOutcome { Completed, Skipped, Stopped, Superseded, Unavailable, Failed, OwnerClosed }
public enum PreviewPhase { Idle, Starting, Playing, Stopping, Completed, Skipped, Stopped, Failed, Closed }
public enum PreviewError { None, InvalidRequest, StartFailed, StartupTimedOut, PlaybackTimedOut, HostFailed, CleanupFailed }
public sealed record PreviewResult(Guid RequestId, string? SceneId, PreviewOutcome Outcome, PreviewError Error = PreviewError.None, MotionHostError? HostError = null);
public sealed record PreviewSnapshot(long Generation, Guid? RequestId, string? SceneId, PreviewPhase Phase, PreviewError Error = PreviewError.None, MotionHostError? HostError = null);

public interface IPreviewSession : IAsyncDisposable
{
    Task<MotionHostOutcome?> Started { get; }
    Task<MotionHostOutcome> Completion { get; }
    MotionHostError? FailureReason { get; }
}

public interface IPreviewSessionFactory
{
    IPreviewSession Start(PreviewRequest request);
}

public sealed class MotionHostPreviewSessionFactory(IMotionHostSessionFactory hostFactory) : IPreviewSessionFactory
{
    private readonly IMotionHostSessionFactory _hostFactory = hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));

    public IPreviewSession Start(PreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.IsValid) throw new ArgumentException("Invalid preview request.", nameof(request));
        var start = new MotionHostStartRequest(request.RequestId, request.Playback.SceneId, request.Playback.SceneVersion,
            request.DisplayMode, request.Playback.DurationMilliseconds + 500, MotionHostMode.Preview, request.Playback);
        return new Session(_hostFactory.Start(start));
    }

    private sealed class Session(IMotionHostSession host) : IPreviewSession
    {
        public Task<MotionHostOutcome?> Started => host.Started;
        public Task<MotionHostOutcome> Completion => host.Completion;
        public MotionHostError? FailureReason => host.FailureReason;
        public ValueTask DisposeAsync() => host.DisposeAsync();
    }
}
