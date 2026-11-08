using System.Diagnostics;
using System.IO.Pipes;
using DesktopMotionStudio.Core.Localization;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;

namespace DesktopMotionStudio.Core.MotionHost;

public sealed class WindowsMotionHostProcessFactory : IMotionHostSessionFactory
{
    private readonly string _hostExecutablePath;
    private readonly bool _english;
    private readonly MotionDisplayPreference? _display;

    public WindowsMotionHostProcessFactory(string hostExecutablePath, string language = ProductText.DefaultLanguage,
        MotionDisplayPreference? display = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostExecutablePath);
        if (!Path.IsPathFullyQualified(hostExecutablePath))
            throw new ArgumentException("MotionHost executable path must be absolute.", nameof(hostExecutablePath));
        _hostExecutablePath = Path.GetFullPath(hostExecutablePath);
        _english = language == ProductText.English;
        _display = display;
    }

    public IMotionHostSession Start(MotionHostStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("MotionHost is Windows-only.");
        if (!request.IsValid) throw new ArgumentException("MotionHost request is invalid.", nameof(request));
        if (!File.Exists(_hostExecutablePath)) throw new FileNotFoundException("MotionHost executable is unavailable.", _hostExecutablePath);
        if (_display is not null) request = request with { DisplayMode = _display.Resolve(request.DisplayMode) };

        string pipeName = $"dms-motionhost-{request.RequestId:N}";
        var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            MotionHostFrameCodec.MaximumFrameBytes + MotionHostFrameCodec.HeaderSize,
            MotionHostFrameCodec.MaximumFrameBytes + MotionHostFrameCodec.HeaderSize);

        OwnedMotionHostProcess? owned = null;
        try
        {
            owned = OwnedMotionHostProcess.Start(_hostExecutablePath, pipeName, request.RequestId, english: _english);
            return new WindowsMotionHostSession(pipe, owned, request);
        }
        catch
        {
            owned?.Dispose();
            pipe.Dispose();
            throw;
        }
    }

    private sealed class WindowsMotionHostSession : IMotionHostSession
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly Process _process;
        private readonly OwnedMotionHostProcess _owned;
        private readonly MotionHostStartRequest _request;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly object _stateLock = new();
        private readonly TaskCompletionSource<MotionHostOutcome?> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<MotionHostOutcome> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _protocolTask;
        private readonly CancellationTokenSource _deadlineCancellation = new();
        private Task _deadlineTask = Task.CompletedTask;
        private Task? _stopDeadlineTask;
        private MotionHostProtocolState _state = MotionHostProtocolState.AwaitingReady;
        private MotionHostOutcome? _terminalOutcome;
        private MotionHostOutcome? _requestedOutcome;
        private MotionHostError? _failureReason;
        private int? _nativeErrorCode;
        private int _disposed;

        public WindowsMotionHostSession(
            NamedPipeServerStream pipe,
            OwnedMotionHostProcess owned,
            MotionHostStartRequest request)
        {
            _pipe = pipe;
            _owned = owned;
            _process = owned.Process;
            _request = request;
            _protocolTask = RunProtocolAsync();
        }

        public Task<MotionHostOutcome?> Started => _started.Task;
        public Task<MotionHostOutcome> Completion => _completion.Task;
        public int? HostProcessId => _process.Id;

        private bool FiniteLaunch => _request.Mode == MotionHostMode.Launch && _request.Playback?.IsTechFrame == true;

        public MotionHostError? FailureReason
        {
            get { lock (_stateLock) return _failureReason; }
        }

        public int? NativeErrorCode
        {
            get { lock (_stateLock) return _nativeErrorCode; }
        }

        public async Task<MotionHostOutcome> HandoffAsync(CancellationToken cancellationToken = default)
        {
            if (_request.Mode == MotionHostMode.Preview) throw new InvalidOperationException("Preview has no target handoff authority.");
            cancellationToken.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                if (FiniteLaunch && _state is MotionHostProtocolState.AwaitingReady or MotionHostProtocolState.AwaitingStart or MotionHostProtocolState.Starting)
                    Complete(MotionHostOutcome.NotStarted);
            }
            MotionHostOutcome? started = await _started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (started is MotionHostOutcome earlyOutcome) return earlyOutcome;
            if (_completion.Task.IsCompleted) return await _completion.Task.ConfigureAwait(false);

            bool shouldSendHandoff;
            lock (_stateLock)
            {
                shouldSendHandoff = _state == MotionHostProtocolState.Visible;
                if (shouldSendHandoff) _requestedOutcome = MotionHostOutcome.HandedOff;
            }

            if (shouldSendHandoff)
            {
                try { await SendCommandAsync(MotionHostMessageKind.Handoff, cancellationToken).ConfigureAwait(false); }
                catch (InvalidDataException) when (IsClosingOrCompleted()) { }
                catch (IOException) when (IsClosingOrCompleted()) { }
            }
            return await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<MotionHostOutcome> ShutdownAsync(CancellationToken cancellationToken = default)
        {
            if (_completion.Task.IsCompleted) return await _completion.Task.ConfigureAwait(false);
            lock (_stateLock)
            {
                if (_state is MotionHostProtocolState.AwaitingReady or MotionHostProtocolState.AwaitingStart or MotionHostProtocolState.Starting)
                    Complete(MotionHostOutcome.Exited); // The child cannot read Stop until Started; close early ownership directly.
                else if (_terminalOutcome is null) _stopDeadlineTask ??= EnforceStopDeadlineAsync();
            }
            if (_completion.Task.IsCompleted) return await _completion.Task.ConfigureAwait(false);
            if (_pipe.IsConnected)
            {
                bool shouldSendShutdown;
                lock (_stateLock)
                {
                    shouldSendShutdown = _state is MotionHostProtocolState.AwaitingReady or MotionHostProtocolState.AwaitingStart or
                        MotionHostProtocolState.Starting or MotionHostProtocolState.Visible;
                    if (shouldSendShutdown) _requestedOutcome = MotionHostOutcome.Exited;
                }

                if (shouldSendShutdown)
                {
                    try { await SendCommandAsync(MotionHostMessageKind.Shutdown, cancellationToken).WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false); }
                    catch (InvalidDataException) when (IsClosingOrCompleted()) { }
                    catch (IOException) when (IsClosingOrCompleted()) { }
                    catch (TimeoutException) { Complete(MotionHostOutcome.Exited); }
                }
            }
            else Complete(MotionHostOutcome.Exited);
            return await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try
            {
                if (!_completion.Task.IsCompleted)
                    await ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (Exception) { }

            try { _pipe.Dispose(); }
            catch (IOException) { }
            Complete(MotionHostOutcome.Exited);
            try { await Task.WhenAll(_protocolTask, _deadlineTask, _stopDeadlineTask ?? Task.CompletedTask, _process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception) { }
            _pipe.Dispose();
            _owned.Dispose();
            _deadlineCancellation.Dispose();
            _writeLock.Dispose();
        }

        private async Task RunProtocolAsync()
        {
            try
            {
                await _pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                MotionHostFrame? first = await MotionHostFrameCodec.ReadAsync(_pipe)
                    .WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                if (first is null || !AcceptIncoming(first, MotionHostMessageKind.Ready))
                    throw new InvalidDataException("MotionHost did not complete the expected handshake.");

                await SendCommandAsync(MotionHostMessageKind.Start, CancellationToken.None, _request.ToStartFrame()).ConfigureAwait(false);
                bool waitingForStarted = true;
                while (true)
                {
                    Task<MotionHostFrame?> read = MotionHostFrameCodec.ReadAsync(_pipe);
                    MotionHostFrame? frame = waitingForStarted
                        ? await read.WaitAsync(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false)
                        : await read.ConfigureAwait(false);
                    if (frame is null) throw new EndOfStreamException("MotionHost disconnected before completion.");
                    lock (_stateLock)
                    {
                        if (_terminalOutcome is not null) return;
                        if (!AcceptIncoming(frame, frame.Kind))
                            throw new InvalidDataException("MotionHost sent an invalid protocol transition.");

                        switch (frame.Kind)
                        {
                            case MotionHostMessageKind.Completed:
                                Complete(FiniteLaunch ? MotionHostOutcome.Completed : _requestedOutcome ?? MotionHostOutcome.Completed);
                                return;
                            case MotionHostMessageKind.Started:
                                waitingForStarted = false;
                                _deadlineTask = EnforceDeadlineAsync();
                                _started.TrySetResult(null);
                                break;
                            case MotionHostMessageKind.SkipRequested:
                                Complete(MotionHostOutcome.Skipped);
                                return;
                            case MotionHostMessageKind.TimedOut:
                                Complete(MotionHostOutcome.TimedOut, MotionHostError.TimedOut);
                                return;
                            case MotionHostMessageKind.Failed:
                                Complete(MotionHostOutcome.Failed, frame.Error ?? MotionHostError.RendererFailed, frame.NativeErrorCode);
                                return;
                            case MotionHostMessageKind.Finished:
                                Complete(_requestedOutcome ?? MotionHostOutcome.Exited);
                                return;
                        }
                    }
                }
            }
            catch (TimeoutException)
            {
                Complete(MotionHostOutcome.Failed, MotionHostError.ConnectionTimeout);
            }
            catch (MotionHostProtocolException ex)
            {
                Complete(MotionHostOutcome.Failed, ex.Error);
            }
            catch (InvalidDataException)
            {
                Complete(MotionHostOutcome.Failed, MotionHostError.InvalidMessage);
            }
            catch (Exception)
            {
                Complete(MotionHostOutcome.Failed, MotionHostError.RendererFailed);
            }
        }

        private async Task EnforceDeadlineAsync()
        {
            try { await Task.Delay(_request.MaximumVisibleMilliseconds, _deadlineCancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            // Thread-pool timer, independent of either UI dispatcher and the host's ability to read Stop.
            Complete(MotionHostOutcome.TimedOut, MotionHostError.TimedOut);
        }

        private async Task EnforceStopDeadlineAsync()
        {
            try { await Task.Delay(TimeSpan.FromSeconds(1), _deadlineCancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            Complete(MotionHostOutcome.Exited); // Stop cannot depend on another caller later disposing the session.
        }

        private bool AcceptIncoming(MotionHostFrame frame, MotionHostMessageKind expectedKind)
        {
            if (frame.Kind != expectedKind ||
                MotionHostFrameValidator.ValidateEnvelope(frame, _request.RequestId) != MotionHostError.None)
                return false;

            lock (_stateLock)
            {
                if (!MotionHostFrameValidator.TryAdvance(_state, MotionHostPeer.Host, frame.Kind, out MotionHostProtocolState next, _request.Mode, FiniteLaunch))
                    return false;
                _state = next;
                return true;
            }
        }

        private async Task SendCommandAsync(
            MotionHostMessageKind kind,
            CancellationToken cancellationToken,
            MotionHostFrame? supplied = null)
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_stateLock)
                {
                    if (!MotionHostFrameValidator.TryAdvance(_state, MotionHostPeer.Launcher, kind,
                            out MotionHostProtocolState next, _request.Mode))
                        throw new InvalidDataException("MotionHost command is not valid in the current state.");
                    _state = next;
                }

                MotionHostFrame frame = supplied ?? new MotionHostFrame(
                    MotionHostFrame.CurrentProtocolVersion, _request.RequestId, kind);
                if (MotionHostFrameValidator.ValidateEnvelope(frame, _request.RequestId) != MotionHostError.None)
                    throw new InvalidDataException("MotionHost command frame is invalid.");
                await MotionHostFrameCodec.WriteAsync(_pipe, frame, cancellationToken).ConfigureAwait(false);
            }
            finally { _writeLock.Release(); }
        }

        private void Complete(MotionHostOutcome outcome, MotionHostError? error = null, int? nativeErrorCode = null)
        {
            lock (_stateLock)
            {
                if (_terminalOutcome is not null) return;
                _terminalOutcome = outcome;
                _failureReason ??= error;
                _nativeErrorCode ??= nativeErrorCode;
                _state = MotionHostProtocolState.Closed;
                _deadlineCancellation.Cancel();
                _owned.Terminate(); // Close ownership before signalling any terminal observer.
                _started.TrySetResult(outcome);
                _completion.TrySetResult(outcome);
            }
        }

        private bool IsClosingOrCompleted()
        {
            if (_completion.Task.IsCompleted) return true;
            lock (_stateLock) return _state is MotionHostProtocolState.Closing or MotionHostProtocolState.Closed;
        }
    }
}
