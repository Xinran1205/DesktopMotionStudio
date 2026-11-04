using DesktopMotionStudio.Core.MotionHost;

internal sealed class FakeMotionHostSessionFactory : IMotionHostSessionFactory
{
    public Func<MotionHostStartRequest, IMotionHostSession>? StartHandler { get; set; }
    public int StartCount { get; private set; }

    public IMotionHostSession Start(MotionHostStartRequest request)
    {
        StartCount++;
        return StartHandler?.Invoke(request) ?? new FakeMotionHostSession();
    }
}

internal sealed class FakeMotionHostSession : IMotionHostSession
{
    public MotionHostOutcome HandoffOutcome { get; set; } = MotionHostOutcome.HandedOff;
    public MotionHostOutcome ShutdownOutcome { get; set; } = MotionHostOutcome.Exited;
    public int HandoffCount { get; private set; }
    public int ShutdownCount { get; private set; }
    public bool Disposed { get; private set; }
    public MotionHostError? FailureReason { get; set; }
    public int? NativeErrorCode { get; set; }

    public Task<MotionHostOutcome> HandoffAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HandoffCount++;
        return Task.FromResult(HandoffOutcome);
    }

    public Task<MotionHostOutcome> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ShutdownCount++;
        return Task.FromResult(ShutdownOutcome);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
