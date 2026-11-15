using System.Security.Cryptography;
using System.Text;
using DesktopMotionStudio.Core.Launch;

namespace DesktopMotionStudio.Core.Instances;

public sealed class ApplicationInstanceCoordinator
{
    public static readonly TimeSpan DefaultWindowWait = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan DefaultGateWait = TimeSpan.FromSeconds(30);

    private readonly IWindowPlatform _platform;
    private readonly LaunchTargetIdentityResolver _identityResolver;
    private readonly Func<ResolvedLaunchTarget, LaunchResult> _launch;
    private readonly IInstanceClock _clock;
    private readonly TimeSpan _windowWait;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _gateWait;

    public ApplicationInstanceCoordinator(
        IWindowPlatform platform,
        Func<ResolvedLaunchTarget, LaunchResult> launch,
        LaunchTargetIdentityResolver? identityResolver = null,
        IInstanceClock? clock = null,
        TimeSpan? windowWait = null,
        TimeSpan? pollInterval = null,
        TimeSpan? gateWait = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
        _identityResolver = identityResolver ?? new LaunchTargetIdentityResolver();
        _clock = clock ?? new SystemInstanceClock();
        _windowWait = windowWait ?? DefaultWindowWait;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _gateWait = gateWait ?? DefaultGateWait;
        if (_windowWait <= TimeSpan.Zero || _windowWait > DefaultWindowWait)
            throw new ArgumentOutOfRangeException(nameof(windowWait), "Window discovery cannot exceed 15 seconds.");
        if (_pollInterval <= TimeSpan.Zero || _gateWait <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
    }

    public Task<InstanceResult> OpenAsync(
        string appId,
        LaunchTarget target,
        CancellationToken cancellationToken = default,
        Action? beforeDispatch = null) =>
        OpenAsync(appId, target, cancellationToken, beforeDispatch is null ? null : () =>
        {
            beforeDispatch();
            return Task.CompletedTask;
        }, recheckAfterPreparation: false);

    internal Task<InstanceResult> OpenAfterPreparationAsync(
        string appId, LaunchTarget target, CancellationToken cancellationToken, Func<Task> beforeDispatch) =>
        OpenAsync(appId, target, cancellationToken, beforeDispatch, recheckAfterPreparation: true);

    private async Task<InstanceResult> OpenAsync(string appId, LaunchTarget target,
        CancellationToken cancellationToken, Func<Task>? beforeDispatch, bool recheckAfterPreparation)
    {
        long requestedAt = _clock.Timestamp;
        if (!IsValidAppId(appId)) return new InstanceResult(InstanceOutcome.InvalidTarget);
        LaunchTargetResolution resolution = _identityResolver.Resolve(target);
        if (!resolution.Succeeded || resolution.Target is null)
            return new InstanceResult(InstanceOutcome.InvalidTarget, NativeErrorCode: resolution.NativeErrorCode);
        if (!OperatingSystem.IsWindows()) return new InstanceResult(InstanceOutcome.InvalidTarget);
        if (!Environment.UserInteractive || _platform.CurrentSessionId == 0)
            return new InstanceResult(InstanceOutcome.IdentityUnavailable);

        return await Task.Run(() => OpenWithGate(appId, resolution.Target, requestedAt, cancellationToken, beforeDispatch, recheckAfterPreparation))
            .ConfigureAwait(false);
    }

    private InstanceResult OpenWithGate(string appId, ResolvedLaunchTarget target, long requestedAt,
        CancellationToken cancellationToken, Func<Task>? beforeDispatch, bool recheckAfterPreparation)
    {
        if (cancellationToken.IsCancellationRequested) return new InstanceResult(InstanceOutcome.Canceled);
        Mutex gate;
        try { gate = CreateGate(appId); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        { return new InstanceResult(InstanceOutcome.IdentityUnavailable, NativeErrorCode: ex.HResult); }
        using (gate)
        {
            int gateResult;
            try { gateResult = WaitHandle.WaitAny([gate, cancellationToken.WaitHandle], _gateWait); }
            catch (AbandonedMutexException ex) when (ex.MutexIndex == 0) { gateResult = 0; }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
            { return new InstanceResult(InstanceOutcome.IdentityUnavailable, NativeErrorCode: ex.HResult); }

            if (gateResult == WaitHandle.WaitTimeout)
                return new InstanceResult(InstanceOutcome.GateTimedOut, ElapsedWait: _clock.ElapsedSince(requestedAt));
            if (gateResult == 1) return new InstanceResult(InstanceOutcome.Canceled, ElapsedWait: _clock.ElapsedSince(requestedAt));
            if (gateResult != 0) return new InstanceResult(InstanceOutcome.IdentityUnavailable);

            try
            {
                if (cancellationToken.IsCancellationRequested) return new InstanceResult(InstanceOutcome.Canceled);
                // A mutex belongs to this worker thread. Keep it here while the asynchronous
                // request completes on other threads, including after abandoned ownership.
                return OpenInsideGateAsync(target, requestedAt, cancellationToken, beforeDispatch, recheckAfterPreparation).GetAwaiter().GetResult();
            }
            finally { gate.ReleaseMutex(); }
        }
    }

    private InstanceResult? CheckExisting(ResolvedLaunchTarget target, long requestedAt)
    {
        ProcessScanResult processScan = _platform.FindMatchingProcesses(target.Identity);
        if (processScan.NativeErrorCode is not null && !processScan.IsComplete)
            return new InstanceResult(InstanceOutcome.IdentityUnavailable, NativeErrorCode: processScan.NativeErrorCode);

        if (processScan.Matches.Count > 0)
        {
            CandidateSelection existing = SelectWindow(processScan.Matches);
            if (existing.Outcome is InstanceOutcome.IdentityUnavailable or InstanceOutcome.AmbiguousWindow)
                return Result(existing, elapsed: _clock.ElapsedSince(requestedAt));
            if (existing.Candidate is null)
                return new InstanceResult(InstanceOutcome.NoUsableWindow, processScan.Matches[0],
                    ElapsedWait: _clock.ElapsedSince(requestedAt));

            WindowActivationResult activation = _platform.Activate(existing.Candidate);
            InstanceOutcome activationOutcome = activation.Status switch
            {
                WindowActivationStatus.Activated => InstanceOutcome.ActivatedExisting,
                WindowActivationStatus.ForegroundDenied => InstanceOutcome.ForegroundDenied,
                WindowActivationStatus.IdentityChanged => InstanceOutcome.IdentityChanged,
                _ => InstanceOutcome.NoUsableWindow
            };
            return new InstanceResult(activationOutcome, existing.Candidate.Process, existing.Candidate.Handle,
                _clock.ElapsedSince(requestedAt), activation.NativeErrorCode);
        }

        return processScan.IsComplete ? null : new InstanceResult(InstanceOutcome.IdentityUnavailable, NativeErrorCode: processScan.NativeErrorCode);
    }

    private async Task<InstanceResult> OpenInsideGateAsync(ResolvedLaunchTarget target, long requestedAt,
        CancellationToken cancellationToken, Func<Task>? beforeDispatch, bool recheckAfterPreparation)
    {
        if (CheckExisting(target, requestedAt) is { } existing) return existing;
        int? preparationErrorCode = null;
        try
        {
            if (beforeDispatch is not null) await beforeDispatch().ConfigureAwait(false);
        }
        catch (Exception ex) { preparationErrorCode = ex.HResult; }

        if (cancellationToken.IsCancellationRequested) return new InstanceResult(InstanceOutcome.Canceled);
        if (recheckAfterPreparation)
        {
            InstanceResult? openedDuringPreparation = CheckExisting(target, requestedAt);
            if (cancellationToken.IsCancellationRequested) return new InstanceResult(InstanceOutcome.Canceled);
            if (openedDuringPreparation is not null)
                return openedDuringPreparation with { LaunchPreparationErrorCode = preparationErrorCode };
        }
        LaunchResult launch = _launch(target);
        if (!launch.Succeeded)
            return new InstanceResult(InstanceOutcome.LaunchFailed, NativeErrorCode: launch.NativeErrorCode,
                LaunchPreparationErrorCode: preparationErrorCode);
        if (launch.ProcessId is not int processId || processId <= 0)
            return new InstanceResult(InstanceOutcome.IdentityUnavailable, NativeErrorCode: launch.NativeErrorCode,
                LaunchPreparationErrorCode: preparationErrorCode);
        if (launch.ProcessCreationFileTimeUtc is not long dispatchCreationTime)
            return new InstanceResult(InstanceOutcome.IdentityUnavailable, NativeErrorCode: launch.NativeErrorCode,
                LaunchPreparationErrorCode: preparationErrorCode);

        long waitStarted = _clock.Timestamp; // The 15-second deadline begins only after F04 dispatch returns.
        ProcessVerificationResult verification = _platform.VerifyProcess(
            processId, target.Identity, _platform.CurrentSessionId, dispatchCreationTime);
        if (verification.Status != ProcessVerificationStatus.Confirmed || verification.Process is null)
            return new InstanceResult(MapVerification(verification.Status), NativeErrorCode: verification.NativeErrorCode,
                LaunchPreparationErrorCode: preparationErrorCode);
        ProcessStamp process = verification.Process;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
                return new InstanceResult(InstanceOutcome.Canceled, process, ElapsedWait: _clock.ElapsedSince(waitStarted),
                    LaunchPreparationErrorCode: preparationErrorCode);

            verification = _platform.VerifyProcess(process.ProcessId, target.Identity, process.SessionId, process.CreationFileTimeUtc);
            if (verification.Status != ProcessVerificationStatus.Confirmed)
                return new InstanceResult(MapVerification(verification.Status), process,
                    ElapsedWait: _clock.ElapsedSince(waitStarted), NativeErrorCode: verification.NativeErrorCode,
                    LaunchPreparationErrorCode: preparationErrorCode);

            WindowScanResult windows = _platform.FindWindows(process);
            if (!windows.IsComplete)
                return new InstanceResult(InstanceOutcome.IdentityUnavailable, process,
                    ElapsedWait: _clock.ElapsedSince(waitStarted), NativeErrorCode: windows.NativeErrorCode,
                    LaunchPreparationErrorCode: preparationErrorCode);

            CandidateSelection selection = SelectWindow([process], windows.Candidates);
            if (selection.Outcome == InstanceOutcome.AmbiguousWindow)
                return Result(selection, process, _clock.ElapsedSince(waitStarted)) with
                { LaunchPreparationErrorCode = preparationErrorCode };
            if (selection.Candidate is not null)
                return new InstanceResult(InstanceOutcome.WindowReady, selection.Candidate.Process, selection.Candidate.Handle,
                    _clock.ElapsedSince(waitStarted), LaunchPreparationErrorCode: preparationErrorCode);

            TimeSpan elapsed = _clock.ElapsedSince(waitStarted);
            if (elapsed >= _windowWait)
                return new InstanceResult(InstanceOutcome.TimedOut, process, ElapsedWait: elapsed,
                    LaunchPreparationErrorCode: preparationErrorCode);

            TimeSpan remaining = _windowWait - elapsed;
            TimeSpan delay = remaining < _pollInterval ? remaining : _pollInterval;
            try { await _clock.DelayAsync(delay, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                return new InstanceResult(InstanceOutcome.Canceled, process, ElapsedWait: _clock.ElapsedSince(waitStarted),
                    LaunchPreparationErrorCode: preparationErrorCode);
            }
        }
    }

    private CandidateSelection SelectWindow(IReadOnlyList<ProcessStamp> processes, IReadOnlyList<WindowCandidate>? supplied = null)
    {
        var candidates = new List<WindowCandidate>();
        foreach (ProcessStamp process in processes)
        {
            WindowScanResult scan = supplied is null ? _platform.FindWindows(process) : new WindowScanResult(supplied);
            if (!scan.IsComplete)
                return new CandidateSelection(InstanceOutcome.IdentityUnavailable, null, scan.NativeErrorCode);
            candidates.AddRange(scan.Candidates.Where(candidate =>
                candidate.Handle != 0 && SameProcess(candidate.Process, process) &&
                candidate.IsVisible && !candidate.IsCloaked && !candidate.IsOwned && !candidate.IsToolWindow &&
                (candidate.IsMainWindow || candidate.IsForeground)));
        }

        if (candidates.Count == 0) return new CandidateSelection(InstanceOutcome.NoUsableWindow, null);
        int highestScore = candidates.Max(Score);
        WindowCandidate[] best = candidates.Where(candidate => Score(candidate) == highestScore).ToArray();
        return best.Length == 1
            ? new CandidateSelection(InstanceOutcome.WindowReady, best[0])
            : new CandidateSelection(InstanceOutcome.AmbiguousWindow, null);
    }

    private static int Score(WindowCandidate candidate) =>
        (candidate.IsForeground ? 100 : 0) + (candidate.IsMainWindow ? 50 : 0) + (!candidate.IsMinimized ? 5 : 0);

    private static bool SameProcess(ProcessStamp left, ProcessStamp right) =>
        left.ProcessId == right.ProcessId && left.CreationFileTimeUtc == right.CreationFileTimeUtc &&
        left.SessionId == right.SessionId && left.Identity.GetType() == right.Identity.GetType() &&
        string.Equals(left.Identity.Value, right.Identity.Value, StringComparison.OrdinalIgnoreCase);

    private static InstanceOutcome MapVerification(ProcessVerificationStatus status) => status switch
    {
        ProcessVerificationStatus.Exited => InstanceOutcome.ProcessExited,
        ProcessVerificationStatus.IdentityChanged => InstanceOutcome.IdentityChanged,
        _ => InstanceOutcome.IdentityUnavailable
    };

    private static InstanceResult Result(CandidateSelection selection, ProcessStamp? process = null, TimeSpan elapsed = default) =>
        new(selection.Outcome, selection.Candidate?.Process ?? process, selection.Candidate?.Handle, elapsed, selection.NativeErrorCode);

    private static bool IsValidAppId(string appId) =>
        !string.IsNullOrWhiteSpace(appId) && appId.Length <= 128 && !appId.Any(char.IsControl);

    internal static Mutex CreateGate(string appId)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(appId));
        string name = $"Local\\DesktopMotionStudio.InstanceMutex.{Convert.ToHexString(digest)}";
        return new Mutex(initiallyOwned: false, name);
    }

    private sealed record CandidateSelection(InstanceOutcome Outcome, WindowCandidate? Candidate, int? NativeErrorCode = null);
}
