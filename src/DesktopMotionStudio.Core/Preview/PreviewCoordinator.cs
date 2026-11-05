using System.Threading.Channels;
using DesktopMotionStudio.Core.MotionHost;

namespace DesktopMotionStudio.Core.Preview;

// A single reader owns all session mutations. Public commands never wait for a UI dispatcher.
public sealed class PreviewCoordinator : IAsyncDisposable
{
    private readonly IPreviewSessionFactory _factory;
    private readonly Channel<Command> _commands = Channel.CreateUnbounded<Command>(new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly Task _worker;
    private PreviewSnapshot _current = new(0, null, null, PreviewPhase.Idle);
    private Active? _active;
    private long _latest;
    private int _closed;
    private bool _cleanupBlocked;

    public PreviewCoordinator(IPreviewSessionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _worker = RunAsync();
    }

    public PreviewSnapshot Current => Volatile.Read(ref _current);
    public event Action? StateChanged;

    public Task<PreviewResult> PlayAsync(PreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Volatile.Read(ref _closed) != 0) return Task.FromResult(Result(request, PreviewOutcome.OwnerClosed));
        if (!request.IsValid) return Task.FromResult(Result(request, PreviewOutcome.Unavailable, PreviewError.InvalidRequest));
        if (cancellationToken.IsCancellationRequested) return Task.FromResult(Result(request, PreviewOutcome.Stopped));
        var play = new Play(Interlocked.Increment(ref _latest), request, cancellationToken);
        play.Registration = cancellationToken.Register(() => _commands.Writer.TryWrite(new Cancel(play.Ticket)));
        if (!_commands.Writer.TryWrite(play)) FinishQueued(play, PreviewOutcome.OwnerClosed);
        return play.Result.Task;
    }

    public Task StopAsync()
    {
        if (Volatile.Read(ref _closed) != 0) return _worker;
        var stop = new Stop(Interlocked.Increment(ref _latest));
        return _commands.Writer.TryWrite(stop) ? stop.Done.Task : _worker;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
            _commands.Writer.TryWrite(new Close(Interlocked.Increment(ref _latest)));
        return new ValueTask(_worker);
    }

    private async Task RunAsync()
    {
        Task<Command> command = _commands.Reader.ReadAsync().AsTask();
        try
        {
            while (true)
            {
                if (_active is not null && !command.IsCompleted)
                {
                    try
                    {
                        Task observation = _active.Playing ? _active.Session.Completion : _active.Session.Started;
                        _ = await Task.WhenAny(command, observation, _active.Deadline).ConfigureAwait(false);
                        if (!command.IsCompleted)
                        {
                            await ObserveAsync(observation).ConfigureAwait(false);
                            continue;
                        }
                    }
                    catch (Exception) { await FinishActiveAsync(PreviewOutcome.Failed, PreviewError.HostFailed, MotionHostError.RendererFailed).ConfigureAwait(false); continue; }
                }
                Command next = await command.ConfigureAwait(false);
                if (!await HandleAsync(next).ConfigureAwait(false)) break;
                command = _commands.Reader.ReadAsync().AsTask();
            }
        }
        finally
        {
            if (_active is not null) await FinishActiveAsync(PreviewOutcome.OwnerClosed).ConfigureAwait(false);
            _commands.Writer.TryComplete();
            while (_commands.Reader.TryRead(out Command? remaining))
            {
                if (remaining is Play play) FinishQueued(play, PreviewOutcome.OwnerClosed);
                if (remaining is Stop stop) stop.Done.TrySetResult();
            }
        }
    }

    private async Task<bool> HandleAsync(Command command)
    {
        switch (command)
        {
            case Play play:
                if (Volatile.Read(ref _closed) != 0) { FinishQueued(play, PreviewOutcome.OwnerClosed); break; }
                if (play.Ticket != Volatile.Read(ref _latest)) { FinishQueued(play, PreviewOutcome.Superseded); break; }
                Publish(new(play.Ticket, play.Request.RequestId, play.Request.Playback.SceneId, PreviewPhase.Starting));
                if (_active is not null) await FinishActiveAsync(PreviewOutcome.Superseded).ConfigureAwait(false);
                if (_cleanupBlocked)
                {
                    Publish(new(play.Ticket, play.Request.RequestId, play.Request.Playback.SceneId, PreviewPhase.Failed, PreviewError.CleanupFailed));
                    FinishQueued(play, PreviewOutcome.Failed, PreviewError.CleanupFailed);
                    break;
                }
                if (Volatile.Read(ref _closed) != 0) { FinishQueued(play, PreviewOutcome.OwnerClosed); break; }
                if (play.Ticket != Volatile.Read(ref _latest)) { FinishQueued(play, PreviewOutcome.Superseded); break; }
                if (play.Token.IsCancellationRequested) { Publish(new(play.Ticket, play.Request.RequestId, play.Request.Playback.SceneId, PreviewPhase.Stopped)); FinishQueued(play, PreviewOutcome.Stopped); break; }
                try { _active = new Active(play, _factory.Start(play.Request)); }
                catch (Exception)
                {
                    Publish(new(play.Ticket, play.Request.RequestId, play.Request.Playback.SceneId, PreviewPhase.Failed, PreviewError.StartFailed));
                    FinishQueued(play, PreviewOutcome.Failed, PreviewError.StartFailed);
                }
                break;
            case Cancel cancel when _active?.Play.Ticket == cancel.Ticket:
                await FinishActiveAsync(PreviewOutcome.Stopped).ConfigureAwait(false);
                break;
            case Stop stop:
                if (_active is not null && _active.Play.Ticket <= stop.Ticket)
                {
                    Publish(new(stop.Ticket, _active.Play.Request.RequestId, _active.Play.Request.Playback.SceneId, PreviewPhase.Stopping));
                    await FinishActiveAsync(PreviewOutcome.Stopped).ConfigureAwait(false);
                }
                Publish(new(stop.Ticket, Current.RequestId, Current.SceneId, _cleanupBlocked ? PreviewPhase.Failed : PreviewPhase.Stopped, _cleanupBlocked ? PreviewError.CleanupFailed : PreviewError.None));
                stop.Done.TrySetResult();
                break;
            case Close close:
                if (_active is not null) await FinishActiveAsync(PreviewOutcome.OwnerClosed).ConfigureAwait(false);
                Publish(new(close.Ticket, Current.RequestId, Current.SceneId, PreviewPhase.Closed, _cleanupBlocked ? PreviewError.CleanupFailed : PreviewError.None), final: true);
                return false;
        }
        return true;
    }

    private async Task ObserveAsync(Task observation)
    {
        Active active = _active!;
        if (active.Deadline.IsCompleted && !observation.IsCompleted)
        {
            await FinishActiveAsync(PreviewOutcome.Failed, active.Playing ? PreviewError.PlaybackTimedOut : PreviewError.StartupTimedOut, MotionHostError.TimedOut).ConfigureAwait(false);
            return;
        }
        if (!active.Playing)
        {
            MotionHostOutcome? started = await active.Session.Started.ConfigureAwait(false);
            if (started is not null)
                await FinishActiveAsync(PreviewOutcome.Failed, PreviewError.HostFailed, active.Session.FailureReason ?? MotionHostError.RendererFailed).ConfigureAwait(false);
            else
            {
                active.Playing = true; active.SetDeadline(TimeSpan.FromSeconds(10));
                Publish(new(active.Play.Ticket, active.Play.Request.RequestId, active.Play.Request.Playback.SceneId, PreviewPhase.Playing));
            }
            return;
        }
        MotionHostOutcome outcome = await active.Session.Completion.ConfigureAwait(false);
        await FinishActiveAsync(outcome switch
        {
            MotionHostOutcome.Completed => PreviewOutcome.Completed,
            MotionHostOutcome.Skipped => PreviewOutcome.Skipped,
            MotionHostOutcome.Exited => PreviewOutcome.Stopped,
            _ => PreviewOutcome.Failed
        }, outcome == MotionHostOutcome.TimedOut ? PreviewError.PlaybackTimedOut : outcome is MotionHostOutcome.Completed or MotionHostOutcome.Skipped or MotionHostOutcome.Exited ? PreviewError.None : PreviewError.HostFailed,
            outcome == MotionHostOutcome.TimedOut ? MotionHostError.TimedOut : active.Session.FailureReason).ConfigureAwait(false);
    }

    private async Task FinishActiveAsync(PreviewOutcome outcome, PreviewError error = PreviewError.None, MotionHostError? hostError = null)
    {
        Active active = _active!; _active = null;
        active.DeadlineSource.Cancel(); active.DeadlineSource.Dispose();
        try { await active.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (Exception) { _cleanupBlocked = true; outcome = PreviewOutcome.Failed; error = PreviewError.CleanupFailed; }
        active.Play.Registration.Dispose();
        PreviewPhase phase = outcome switch
        {
            PreviewOutcome.Completed => PreviewPhase.Completed,
            PreviewOutcome.Skipped => PreviewPhase.Skipped,
            PreviewOutcome.Stopped or PreviewOutcome.Superseded => PreviewPhase.Stopped,
            PreviewOutcome.OwnerClosed => PreviewPhase.Closed,
            _ => PreviewPhase.Failed
        };
        Publish(new(active.Play.Ticket, active.Play.Request.RequestId, active.Play.Request.Playback.SceneId, phase, error, hostError));
        active.Play.Result.TrySetResult(Result(active.Play.Request, outcome, error, hostError));
    }

    private void Publish(PreviewSnapshot snapshot, bool final = false)
    {
        if (!final && snapshot.Generation != Volatile.Read(ref _latest)) return;
        Volatile.Write(ref _current, snapshot);
        foreach (Delegate observer in StateChanged?.GetInvocationList() ?? [])
        {
            try { ((Action)observer)(); }
            catch (Exception) { /* An observer never owns session cleanup. */ }
        }
    }

    private static PreviewResult Result(PreviewRequest request, PreviewOutcome outcome, PreviewError error = PreviewError.None, MotionHostError? hostError = null) =>
        new(request.RequestId, request.Playback?.SceneId, outcome, error, hostError);

    private static void FinishQueued(Play play, PreviewOutcome outcome, PreviewError error = PreviewError.None)
    {
        play.Registration.Dispose(); play.Result.TrySetResult(Result(play.Request, outcome, error));
    }

    private abstract record Command;
    private sealed record Play(long Ticket, PreviewRequest Request, CancellationToken Token) : Command
    {
        internal CancellationTokenRegistration Registration;
        internal readonly TaskCompletionSource<PreviewResult> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record Cancel(long Ticket) : Command;
    private sealed record Stop(long Ticket) : Command { internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    private sealed record Close(long Ticket) : Command;
    private sealed class Active
    {
        internal readonly Play Play;
        internal readonly IPreviewSession Session;
        internal bool Playing;
        internal CancellationTokenSource DeadlineSource = new();
        internal Task Deadline;
        internal Active(Play play, IPreviewSession session)
        {
            Play = play; Session = session;
            Deadline = Task.Delay(TimeSpan.FromSeconds(3), DeadlineSource.Token);
        }
        internal void SetDeadline(TimeSpan timeout)
        {
            DeadlineSource.Cancel(); DeadlineSource.Dispose(); DeadlineSource = new();
            Deadline = Task.Delay(timeout, DeadlineSource.Token);
        }
    }
}
