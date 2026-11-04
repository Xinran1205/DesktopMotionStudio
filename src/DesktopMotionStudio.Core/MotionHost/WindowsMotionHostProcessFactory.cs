using System.Diagnostics;
using System.IO.Pipes;
using DesktopMotionStudio.Core.Scenes;

namespace DesktopMotionStudio.Core.MotionHost;

public sealed class WindowsMotionHostProcessFactory : IMotionHostSessionFactory
{
    private readonly string _hostExecutablePath;

    public WindowsMotionHostProcessFactory(string hostExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostExecutablePath);
        if (!Path.IsPathFullyQualified(hostExecutablePath))
            throw new ArgumentException("MotionHost executable path must be absolute.", nameof(hostExecutablePath));
        _hostExecutablePath = Path.GetFullPath(hostExecutablePath);
    }

    public IMotionHostSession Start(MotionHostStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("MotionHost is Windows-only.");
        if (!request.IsValid) throw new ArgumentException("MotionHost request is invalid.", nameof(request));
        if (!File.Exists(_hostExecutablePath)) throw new FileNotFoundException("MotionHost executable is unavailable.", _hostExecutablePath);

        string pipeName = $"dms-motionhost-{request.RequestId:N}";
        var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            MotionHostFrameCodec.MaximumFrameBytes + MotionHostFrameCodec.HeaderSize,
            MotionHostFrameCodec.MaximumFrameBytes + MotionHostFrameCodec.HeaderSize);

        var startInfo = new ProcessStartInfo
        {
            FileName = _hostExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(_hostExecutablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--request-id");
        startInfo.ArgumentList.Add(request.RequestId.ToString("D"));

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("MotionHost process could not be started.");
            return new WindowsMotionHostSession(pipe, process, request);
        }
        catch
        {
            process.Dispose();
            pipe.Dispose();
            throw;
        }
    }

    private sealed class WindowsMotionHostSession : IMotionHostSession
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly Process _process;
        private readonly MotionHostStartRequest _request;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly object _stateLock = new();
        private readonly TaskCompletionSource<MotionHostOutcome?> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<MotionHostOutcome> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _protocolTask;
        private MotionHostProtocolState _state = MotionHostProtocolState.AwaitingReady;
        private MotionHostOutcome? _terminalOutcome;
        private MotionHostOutcome? _requestedOutcome;
        private MotionHostError? _failureReason;
        private int? _nativeErrorCode;
        private int _disposed;

        public WindowsMotionHostSession(
            NamedPipeServerStream pipe,
            Process process,
            MotionHostStartRequest request)
        {
            _pipe = pipe;
            _process = process;
            _request = request;
            _protocolTask = RunProtocolAsync();
        }

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
            MotionHostOutcome? started;
            try { started = await _started.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }

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
            }
            return await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<MotionHostOutcome> ShutdownAsync(CancellationToken cancellationToken = default)
        {
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
                    try { await SendCommandAsync(MotionHostMessageKind.Shutdown, cancellationToken).ConfigureAwait(false); }
                    catch (InvalidDataException) when (IsClosingOrCompleted()) { }
                }
            }
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return _completion.Task.IsCompleted
                ? await _completion.Task.ConfigureAwait(false)
                : MotionHostOutcome.Exited;
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

            try
            {
                if (!_process.HasExited)
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                try
                {
                    if (!_process.HasExited) _process.Kill(entireProcessTree: false);
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }
                catch (Exception) { }
            }
            catch (Exception) { }

            try { await _protocolTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception) { }
            _pipe.Dispose();
            _process.Dispose();
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

                while (true)
                {
                    MotionHostFrame? frame = await MotionHostFrameCodec.ReadAsync(_pipe).ConfigureAwait(false);
                    if (frame is null) throw new EndOfStreamException("MotionHost disconnected before completion.");
                    if (!AcceptIncoming(frame, frame.Kind))
                        throw new InvalidDataException("MotionHost sent an invalid protocol transition.");

                    switch (frame.Kind)
                    {
                        case MotionHostMessageKind.Started:
                            _started.TrySetResult(null);
                            break;
                        case MotionHostMessageKind.SkipRequested:
                            Complete(MotionHostOutcome.Skipped);
                            break;
                        case MotionHostMessageKind.TimedOut:
                            Complete(MotionHostOutcome.TimedOut);
                            break;
                        case MotionHostMessageKind.Failed:
                            lock (_stateLock)
                            {
                                _failureReason = frame.Error ?? MotionHostError.RendererFailed;
                                _nativeErrorCode = frame.NativeErrorCode;
                            }
                            Complete(MotionHostOutcome.Failed);
                            break;
                        case MotionHostMessageKind.Finished:
                            MotionHostOutcome finishedOutcome;
                            lock (_stateLock) finishedOutcome = _terminalOutcome ?? _requestedOutcome ?? MotionHostOutcome.Exited;
                            Complete(finishedOutcome);
                            return;
                    }
                }
            }
            catch (TimeoutException)
            {
                SetFailure(MotionHostError.ConnectionTimeout);
                Complete(MotionHostOutcome.Failed);
            }
            catch (InvalidDataException)
            {
                SetFailure(MotionHostError.InvalidMessage);
                Complete(MotionHostOutcome.Failed);
            }
            catch (Exception)
            {
                SetFailure(MotionHostError.RendererFailed);
                Complete(MotionHostOutcome.Failed);
            }
        }

        private void SetFailure(MotionHostError reason, int? nativeErrorCode = null)
        {
            lock (_stateLock)
            {
                _failureReason ??= reason;
                _nativeErrorCode ??= nativeErrorCode;
            }
        }

        private bool AcceptIncoming(MotionHostFrame frame, MotionHostMessageKind expectedKind)
        {
            if (frame.Kind != expectedKind ||
                MotionHostFrameValidator.ValidateEnvelope(frame, _request.RequestId) != MotionHostError.None)
                return false;

            lock (_stateLock)
            {
                if (!MotionHostFrameValidator.TryAdvance(_state, MotionHostPeer.Host, frame.Kind, out MotionHostProtocolState next))
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
                            out MotionHostProtocolState next))
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

        private void Complete(MotionHostOutcome outcome)
        {
            lock (_stateLock) _terminalOutcome ??= outcome;
            _started.TrySetResult(outcome);
            _completion.TrySetResult(_terminalOutcome.Value);
        }

        private bool IsClosingOrCompleted()
        {
            if (_completion.Task.IsCompleted) return true;
            lock (_stateLock) return _state is MotionHostProtocolState.Closing or MotionHostProtocolState.Closed;
        }
    }
}
