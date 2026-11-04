using System.IO.Pipes;
using DesktopMotionStudio.Core.MotionHost;

namespace DesktopMotionStudio.MotionHost;

internal sealed class HostProtocolClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly Guid _requestId;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _stateLock = new();
    private MotionHostProtocolState _state = MotionHostProtocolState.AwaitingReady;
    private Task? _listenerTask;
    private int _disposed;

    private HostProtocolClient(NamedPipeClientStream pipe, Guid requestId)
    {
        _pipe = pipe;
        _requestId = requestId;
    }

    public MotionHostStartPayload StartPayload { get; private set; } = null!;

    public static async Task<HostProtocolClient> ConnectAndStartAsync(HostArguments arguments)
    {
        var pipe = new NamedPipeClientStream(
            ".", arguments.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var client = new HostProtocolClient(pipe, arguments.RequestId);
        try
        {
            await pipe.ConnectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            await client.SendHostFrameAsync(MotionHostMessageKind.Ready).ConfigureAwait(false);

            MotionHostFrame? frame = await MotionHostFrameCodec.ReadAsync(pipe)
                .WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            if (frame is null || MotionHostFrameValidator.ValidateEnvelope(frame, arguments.RequestId) != MotionHostError.None ||
                frame.Kind != MotionHostMessageKind.Start || frame.Start is null)
                throw new InvalidDataException("Launcher did not send a valid MotionHost start frame.");
            if (!client.TryAdvance(MotionHostPeer.Launcher, frame.Kind))
                throw new InvalidDataException("MotionHost start frame is out of sequence.");

            var request = new MotionHostStartRequest(arguments.RequestId, frame.Start.SceneId,
                frame.Start.SceneVersion, frame.Start.DisplayMode, frame.Start.MaximumVisibleMilliseconds);
            if (!request.IsValid) throw new InvalidDataException("MotionHost start payload is invalid.");
            client.StartPayload = frame.Start;
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task SendStartedAsync() => SendHostFrameAsync(MotionHostMessageKind.Started);

    public void StartListener(MotionHostWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_listenerTask is not null)
            throw new InvalidOperationException("MotionHost listener has already started.");
        _listenerTask = ListenAsync(window);
    }

    public async Task ReportExitAsync(MotionHostExitReason reason)
    {
        MotionHostProtocolState state;
        try
        {
            switch (reason)
            {
                case MotionHostExitReason.SkipRequested:
                    await SendHostFrameAsync(MotionHostMessageKind.SkipRequested).ConfigureAwait(false);
                    break;
                case MotionHostExitReason.TimedOut:
                    await SendHostFrameAsync(MotionHostMessageKind.TimedOut).ConfigureAwait(false);
                    break;
                case MotionHostExitReason.Failed:
                    await ReportFailureAsync(MotionHostError.RendererFailed).ConfigureAwait(false);
                    break;
                case MotionHostExitReason.HandedOff:
                case MotionHostExitReason.Shutdown:
                case MotionHostExitReason.ParentDisconnected:
                    break;
                default:
                    return;
            }
        }
        catch (Exception)
        {
            // A failed return channel cannot affect the already-closed overlay or target application.
        }

        lock (_stateLock) state = _state;
        if (state is MotionHostProtocolState.HandoffRequested or MotionHostProtocolState.Closing)
        {
            try { await SendHostFrameAsync(MotionHostMessageKind.Finished).ConfigureAwait(false); }
            catch (Exception) { }
        }
    }

    public async Task ReportFailureAsync(MotionHostError error)
    {
        try
        {
            var frame = new MotionHostFrame(MotionHostFrame.CurrentProtocolVersion, _requestId,
                MotionHostMessageKind.Failed, Error: error);
            await SendHostFrameAsync(frame).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _pipe.Dispose(); }
        catch (IOException) { }
        if (_listenerTask is not null)
        {
            try { await _listenerTask.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false); }
            catch (Exception) { }
        }
        _writeLock.Dispose();
    }

    private async Task ListenAsync(MotionHostWindow window)
    {
        try
        {
            while (true)
            {
                MotionHostFrame? frame = await MotionHostFrameCodec.ReadAsync(_pipe).ConfigureAwait(false);
                if (frame is null)
                {
                    window.RequestImmediateExit(MotionHostExitReason.ParentDisconnected);
                    return;
                }

                if (MotionHostFrameValidator.ValidateEnvelope(frame, _requestId) != MotionHostError.None ||
                    !TryAdvance(MotionHostPeer.Launcher, frame.Kind))
                {
                    window.RequestImmediateExit(MotionHostExitReason.Failed);
                    return;
                }

                switch (frame.Kind)
                {
                    case MotionHostMessageKind.Handoff:
                        window.RequestHandoff();
                        return;
                    case MotionHostMessageKind.Shutdown:
                        window.RequestImmediateExit(MotionHostExitReason.Shutdown);
                        return;
                    case MotionHostMessageKind.Failed:
                        window.RequestImmediateExit(MotionHostExitReason.Failed);
                        return;
                    default:
                        window.RequestImmediateExit(MotionHostExitReason.Failed);
                        return;
                }
            }
        }
        catch (Exception)
        {
            window.RequestImmediateExit(MotionHostExitReason.ParentDisconnected);
        }
    }

    private async Task SendHostFrameAsync(MotionHostMessageKind kind) =>
        await SendHostFrameAsync(new MotionHostFrame(MotionHostFrame.CurrentProtocolVersion, _requestId, kind))
            .ConfigureAwait(false);

    private async Task SendHostFrameAsync(MotionHostFrame frame)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (MotionHostFrameValidator.ValidateEnvelope(frame, _requestId) != MotionHostError.None ||
                !TryAdvance(MotionHostPeer.Host, frame.Kind))
                throw new InvalidDataException("MotionHost event is invalid in the current protocol state.");
            await MotionHostFrameCodec.WriteAsync(_pipe, frame).ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }
    }

    private bool TryAdvance(MotionHostPeer peer, MotionHostMessageKind kind)
    {
        lock (_stateLock)
        {
            if (!MotionHostFrameValidator.TryAdvance(_state, peer, kind, out MotionHostProtocolState next))
                return false;
            _state = next;
            return true;
        }
    }
}
