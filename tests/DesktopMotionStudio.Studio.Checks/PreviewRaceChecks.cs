using System.Collections.Immutable;
using System.IO;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Preview;
using DesktopMotionStudio.Core.Scenes;

internal static class PreviewRaceChecks
{
    internal static void Run(ref int failures)
    {
        int count = ChecksAsync().GetAwaiter().GetResult(); failures += count;
    }

    private static async Task<int> ChecksAsync()
    {
        int failures = 0;
        void Check(string name, bool passed) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")} preview {name}"); if (!passed) failures++; }
        var definition = new ScenePlaybackDefinition(1, "studio-pulse", "1.0.0", "vector-orbit", 1, 1000, "#618EFF", "M", ImmutableArray.Create(new SceneKeyframe(0, 1, 1, 0), new SceneKeyframe(1000, 1, 1, 90)));
        PreviewRequest Request() => new(Guid.NewGuid(), definition, SceneDisplayMode.Centered);
        var hostFactory = new CapturingHostFactory();
        var adapter = new MotionHostPreviewSessionFactory(hostFactory);
        await using (IPreviewSession adapted = adapter.Start(Request()))
        {
            Check("host adapter grants only Preview with immutable snapshot and finite watchdog", hostFactory.Request is { Mode: MotionHostMode.Preview, MaximumVisibleMilliseconds: 1500 } && ReferenceEquals(hostFactory.Request.Playback, definition));
        }
        Check("host adapter disposal closes only its wrapped session", hostFactory.Session.Disposals == 1);
        var factory = new ControlledFactory();
        await using var coordinator = new PreviewCoordinator(factory);
        coordinator.StateChanged += () => throw new InvalidOperationException("Observer fixture failure");
        Task<PreviewResult> first = coordinator.PlayAsync(Request());
        ControlledSession a = await factory.Next();
        a.Start.TrySetResult(null);
        await Wait(() => coordinator.Current.Phase == PreviewPhase.Playing);
        a.End.TrySetResult(MotionHostOutcome.Completed);
        Check("natural completion is precise and disposes ownership despite observer failure", (await first).Outcome == PreviewOutcome.Completed && a.Disposals == 1 && coordinator.Current.Phase == PreviewPhase.Completed);

        first = coordinator.PlayAsync(Request()); a = await factory.Next(); // Cancel during startup.
        await coordinator.StopAsync();
        Check("Stop during startup disposes and returns Stopped", (await first).Outcome == PreviewOutcome.Stopped && a.Disposals == 1 && coordinator.Current.Phase == PreviewPhase.Stopped);

        first = coordinator.PlayAsync(Request()); a = await factory.Next(); a.GateDisposal = true;
        Task<PreviewResult> second = coordinator.PlayAsync(Request());
        await a.DisposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<PreviewResult> third = coordinator.PlayAsync(Request());
        Check("replacement cannot Start before old disposal completes", factory.Starts == 3);
        a.AllowDisposal.TrySetResult();
        ControlledSession c = await factory.Next();
        c.Start.TrySetResult(null);
        await Wait(() => coordinator.Current.Phase == PreviewPhase.Playing);
        a.Start.TrySetResult(null); a.End.TrySetResult(MotionHostOutcome.Failed);
        Check("queued obsolete request never starts and late old completion cannot replace current", (await first).Outcome == PreviewOutcome.Superseded && (await second).Outcome == PreviewOutcome.Superseded && factory.Starts == 4 && coordinator.Current.RequestId == c.RequestId && coordinator.Current.Phase == PreviewPhase.Playing);

        PreviewSnapshot current = coordinator.Current;
        PreviewResult invalid = await coordinator.PlayAsync(Request() with { Playback = definition with { RuntimeVersion = 99 } });
        PreviewResult cancelled = await coordinator.PlayAsync(Request(), new CancellationToken(true));
        Check("invalid and pre-cancelled request preserve active preview", invalid.Outcome == PreviewOutcome.Unavailable && cancelled.Outcome == PreviewOutcome.Stopped && coordinator.Current == current && factory.Starts == 4);
        c.End.TrySetResult(MotionHostOutcome.Skipped);
        Check("Skip is not reported as natural completion", (await third).Outcome == PreviewOutcome.Skipped && c.Disposals == 1);

        using var cancellation = new CancellationTokenSource();
        first = coordinator.PlayAsync(Request(), cancellation.Token); a = await factory.Next();
        cancellation.Cancel();
        Check("request cancellation cleans startup session", (await first).Outcome == PreviewOutcome.Stopped && a.Disposals == 1);

        first = coordinator.PlayAsync(Request()); a = await factory.Next(); a.FailureReason = MotionHostError.UnsupportedPlayback; a.Start.TrySetResult(MotionHostOutcome.Failed);
        Check("early host failure retains structured compatibility reason", (await first) is { Outcome: PreviewOutcome.Failed, HostError: MotionHostError.UnsupportedPlayback } && a.Disposals == 1);

        factory.ThrowOnStart = true;
        Check("factory failure has no leaked slot", (await coordinator.PlayAsync(Request())) is { Outcome: PreviewOutcome.Failed, Error: PreviewError.StartFailed });
        factory.ThrowOnStart = false;
        first = coordinator.PlayAsync(Request()); a = await factory.Next(); a.FailDisposal = true; a.Start.TrySetResult(null); a.End.TrySetResult(MotionHostOutcome.Completed);
        PreviewResult cleanup = await first;
        int starts = factory.Starts;
        Check("cleanup failure is visible and blocks another host", cleanup.Error == PreviewError.CleanupFailed && (await coordinator.PlayAsync(Request())).Error == PreviewError.CleanupFailed && factory.Starts == starts);

        var closingFactory = new ControlledFactory();
        var closing = new PreviewCoordinator(closingFactory);
        first = closing.PlayAsync(Request()); a = await closingFactory.Next();
        await closing.DisposeAsync(); await closing.DisposeAsync();
        Check("owner close is idempotent and rejects later work", (await first).Outcome == PreviewOutcome.OwnerClosed && a.Disposals == 1 && closing.Current.Phase == PreviewPhase.Closed && (await closing.PlayAsync(Request())).Outcome == PreviewOutcome.OwnerClosed && closingFactory.Starts == 1);

        closingFactory = new ControlledFactory(); closing = new PreviewCoordinator(closingFactory);
        first = closing.PlayAsync(Request()); a = await closingFactory.Next(); a.GateDisposal = true;
        second = closing.PlayAsync(Request());
        await a.DisposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task ownerClose = closing.DisposeAsync().AsTask();
        Check("owner close waits for an in-flight replacement cleanup", !ownerClose.IsCompleted && closingFactory.Starts == 1);
        a.AllowDisposal.TrySetResult(); await ownerClose;
        Check("owner close drains queued replacement without a new host", (await second).Outcome == PreviewOutcome.OwnerClosed && (await first).Outcome == PreviewOutcome.Superseded && closingFactory.Starts == 1 && closing.Current.Phase == PreviewPhase.Closed);

        var queuedFactory = new ControlledFactory();
        await using var queued = new PreviewCoordinator(queuedFactory);
        first = queued.PlayAsync(Request()); a = await queuedFactory.Next(); a.GateDisposal = true;
        second = queued.PlayAsync(Request()); await a.DisposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var queuedCancellation = new CancellationTokenSource();
        third = queued.PlayAsync(Request(), queuedCancellation.Token); queuedCancellation.Cancel();
        a.AllowDisposal.TrySetResult();
        PreviewResult queuedCancelled = await third;
        Check("latest queued cancellation settles Current instead of leaving stale Starting", queuedCancelled.Outcome == PreviewOutcome.Stopped && (await first).Outcome == PreviewOutcome.Superseded && (await second).Outcome == PreviewOutcome.Superseded && queuedFactory.Starts == 1 && queued.Current.Phase == PreviewPhase.Stopped && queued.Current.RequestId == queuedCancelled.RequestId);

        var deadlineFactory = new ControlledFactory();
        await using var deadlines = new PreviewCoordinator(deadlineFactory);
        first = deadlines.PlayAsync(Request()); a = await deadlineFactory.Next();
        Check("coordinator enforces a stalled startup deadline and owned disposal", (await first.WaitAsync(TimeSpan.FromSeconds(4))).Error == PreviewError.StartupTimedOut && a.Disposals == 1 && deadlines.Current.Phase == PreviewPhase.Failed);
        first = deadlines.PlayAsync(Request()); a = await deadlineFactory.Next(); a.GateDisposal = true; a.Start.TrySetResult(null); a.End.TrySetResult(MotionHostOutcome.Completed);
        PreviewResult stalledCleanup = await first.WaitAsync(TimeSpan.FromSeconds(3));
        Check("unresponsive disposal is bounded and cannot overlap a new host", stalledCleanup.Error == PreviewError.CleanupFailed && (await deadlines.PlayAsync(Request())).Error == PreviewError.CleanupFailed && deadlineFactory.Starts == 2);
        a.AllowDisposal.TrySetResult();
        return failures;
    }

    private static async Task Wait(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(5, timeout.Token);
    }

    private sealed class ControlledFactory : IPreviewSessionFactory
    {
        private readonly System.Threading.Channels.Channel<ControlledSession> _created = System.Threading.Channels.Channel.CreateUnbounded<ControlledSession>();
        internal int Starts;
        internal bool ThrowOnStart;
        public IPreviewSession Start(PreviewRequest request)
        {
            if (ThrowOnStart) throw new IOException("Injected fixture start failure");
            Interlocked.Increment(ref Starts);
            var session = new ControlledSession(request.RequestId); _created.Writer.TryWrite(session); return session;
        }
        internal Task<ControlledSession> Next() => _created.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }

    private sealed class ControlledSession(Guid requestId) : IPreviewSession
    {
        internal Guid RequestId = requestId;
        internal readonly TaskCompletionSource<MotionHostOutcome?> Start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<MotionHostOutcome> End = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource DisposalEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource AllowDisposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool GateDisposal, FailDisposal;
        internal int Disposals;
        public Task<MotionHostOutcome?> Started => Start.Task;
        public Task<MotionHostOutcome> Completion => End.Task;
        public MotionHostError? FailureReason { get; set; }
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Disposals); DisposalEntered.TrySetResult();
            if (GateDisposal) await AllowDisposal.Task;
            if (FailDisposal) throw new IOException("Injected fixture cleanup failure");
        }
    }

    private sealed class CapturingHostFactory : IMotionHostSessionFactory
    {
        internal MotionHostStartRequest? Request;
        internal readonly HostSession Session = new();
        public IMotionHostSession Start(MotionHostStartRequest request) { Request = request; return Session; }
    }

    private sealed class HostSession : IMotionHostSession
    {
        internal int Disposals;
        public MotionHostError? FailureReason => null;
        public int? NativeErrorCode => null;
        public int? HostProcessId => null;
        public Task<MotionHostOutcome?> Started => Task.FromResult<MotionHostOutcome?>(null);
        public Task<MotionHostOutcome> Completion => Task.FromResult(MotionHostOutcome.Completed);
        public Task<MotionHostOutcome> HandoffAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Preview cannot hand off a target");
        public Task<MotionHostOutcome> ShutdownAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Adapter uses owned disposal");
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
