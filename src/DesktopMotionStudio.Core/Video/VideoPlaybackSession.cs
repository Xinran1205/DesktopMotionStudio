using System.IO.Pipes;
using DesktopMotionStudio.Core.Instances;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Core.Video;

public sealed class VideoPlaybackSessionFactory(string workerPath, MotionDisplayPreference? display = null)
{
    public IPreviewSession Start(Guid requestId, VideoClip clip, SceneDisplayMode recommended, bool preview)
    {
        var request = new VideoPlaybackRequest(VideoPlaybackRequest.CurrentVersion, requestId, clip,
            display?.Resolve(recommended) ?? recommended, preview);
        if (!request.IsValid) throw new ArgumentException("Invalid video request.", nameof(clip));
        if (!File.Exists(workerPath)) throw new FileNotFoundException("Video worker is unavailable.");

        string pipeName = $"dms-motionhost-{requestId:N}";
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        OwnedMotionHostProcess? worker = null;
        try
        {
            worker = OwnedMotionHostProcess.Start(workerPath, pipeName, requestId, english: ProductText.Language == ProductText.English);
            return new Session(pipe, worker, request);
        }
        catch
        {
            worker?.Dispose();
            pipe.Dispose();
            throw;
        }
    }

    public async Task<MotionHostLifecycleResult> OpenAfterPlaybackAsync(ApplicationInstanceCoordinator instances,
        string appId, LaunchTarget target, VideoClip clip, SceneDisplayMode displayMode, CancellationToken cancellationToken = default)
    {
        MotionHostOutcome outcome = MotionHostOutcome.NotStarted;
        MotionHostError? failure = null;
        async Task PrepareAsync()
        {
            if (cancellationToken.IsCancellationRequested) return;
            IPreviewSession session;
            try { session = Start(Guid.NewGuid(), clip, displayMode, preview: false); }
            catch (Exception)
            {
                outcome = MotionHostOutcome.Failed;
                failure = MotionHostError.StartFailed;
                return;
            }
            try
            {
                outcome = await session.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
                failure = session.FailureReason;
            }
            finally
            {
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch (Exception) { failure = MotionHostError.RendererFailed; }
            }
        }
        var result = await instances.OpenAfterPreparationAsync(appId, target, cancellationToken, PrepareAsync).ConfigureAwait(false);
        return new(result, outcome, failure);
    }

    private sealed class Session : IPreviewSession
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource<MotionHostOutcome?> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Exception? _cleanupError;
        private int _disposed;

        internal Session(NamedPipeServerStream pipe, OwnedMotionHostProcess worker, VideoPlaybackRequest request) =>
            Completion = RunAsync(pipe, worker, request);

        public Task<MotionHostOutcome?> Started => _started.Task;
        public Task<MotionHostOutcome> Completion { get; }
        public MotionHostError? FailureReason { get; private set; }

        private async Task<MotionHostOutcome> RunAsync(NamedPipeServerStream pipe, OwnedMotionHostProcess worker, VideoPlaybackRequest request)
        {
            MotionHostOutcome outcome = MotionHostOutcome.Failed;
            try
            {
                using var startup = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                startup.CancelAfter(VideoPlaybackRequest.StartupMilliseconds);
                await pipe.WaitForConnectionAsync(startup.Token).ConfigureAwait(false);
                await VideoPlaybackWire.WriteAsync(pipe, request, startup.Token).ConfigureAwait(false);
                VideoEventKind kind = await ReadEventAsync(pipe, request, startup.Token).ConfigureAwait(false);
                if (kind == VideoEventKind.Visible)
                {
                    _started.TrySetResult(null);
                    using var end = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    end.CancelAfter(request.Clip.DurationMilliseconds + VideoPlaybackRequest.FinishMilliseconds);
                    kind = await ReadEventAsync(pipe, request, end.Token).ConfigureAwait(false);
                    outcome = kind switch
                    {
                        VideoEventKind.Completed => MotionHostOutcome.Completed,
                        VideoEventKind.Skipped => MotionHostOutcome.Skipped,
                        _ => MotionHostOutcome.Failed
                    };
                }
                if (outcome == MotionHostOutcome.Failed)
                    FailureReason = kind == VideoEventKind.InvalidMedia ? MotionHostError.UnsupportedPlayback : MotionHostError.RendererFailed;
            }
            catch (OperationCanceledException)
            {
                outcome = _stop.IsCancellationRequested ? MotionHostOutcome.Exited : MotionHostOutcome.TimedOut;
                if (outcome == MotionHostOutcome.TimedOut) FailureReason = MotionHostError.TimedOut;
            }
            catch (Exception error)
            {
                FailureReason = error is InvalidDataException ? MotionHostError.InvalidMessage : MotionHostError.RendererFailed;
            }
            finally
            {
                worker.Terminate();
                try
                {
                    await worker.Process.WaitForExitAsync().WaitAsync(
                        TimeSpan.FromMilliseconds(VideoPlaybackRequest.CleanupMilliseconds)).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    _cleanupError = error;
                    FailureReason = MotionHostError.RendererFailed;
                }
                worker.Dispose();
                pipe.Dispose();
                _started.TrySetResult(outcome);
            }
            return outcome;
        }

        private static async Task<VideoEventKind> ReadEventAsync(Stream pipe, VideoPlaybackRequest request, CancellationToken cancellationToken)
        {
            var message = await VideoPlaybackWire.ReadAsync<VideoPlaybackEvent>(pipe, cancellationToken).ConfigureAwait(false);
            return message.Matches(request.RequestId) ? message.Kind : throw new InvalidDataException("Unexpected video worker event.");
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try { _stop.Cancel(); await Completion.ConfigureAwait(false); }
                finally { _stop.Dispose(); }
            }
            else await Completion.ConfigureAwait(false);
            if (_cleanupError is { } error) throw new InvalidOperationException("The video worker did not exit on time.", error);
        }
    }
}
