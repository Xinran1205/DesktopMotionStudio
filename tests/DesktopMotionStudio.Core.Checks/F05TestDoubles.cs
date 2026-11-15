using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using DesktopMotionStudio.Core.Instances;
using DesktopMotionStudio.Core.Launch;

internal sealed class FakeWindowPlatform : IWindowPlatform
{
    private readonly ConcurrentDictionary<int, ProcessStamp> _processes = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<WindowCandidate>> _windows = new();
    private int _activationCalls;
    private int _notificationCalls;

    public uint CurrentSessionId { get; init; } = 1;
    public WindowActivationStatus ActivationStatus { get; set; } = WindowActivationStatus.Activated;
    public ProcessVerificationStatus? ForcedVerificationStatus { get; set; }
    public bool ScanComplete { get; set; } = true;
    public int ActivationCalls => Volatile.Read(ref _activationCalls);
    public int NotificationCalls => Volatile.Read(ref _notificationCalls);
    public int ProcessCount => _processes.Count;

    public void AddProcess(ProcessStamp process, params WindowCandidate[] windows)
    {
        _processes[process.ProcessId] = process;
        _windows[process.ProcessId] = windows;
    }

    public void SetWindows(int processId, params WindowCandidate[] windows) => _windows[processId] = windows;
    public void RemoveProcess(int processId) { _processes.TryRemove(processId, out _); _windows.TryRemove(processId, out _); }

    public ProcessScanResult FindMatchingProcesses(LaunchTargetIdentity identity)
    {
        ProcessStamp[] matches = _processes.Values.Where(process => SameIdentity(process.Identity, identity)).ToArray();
        return new ProcessScanResult(matches, ScanComplete, ScanComplete ? null : 5);
    }

    public ProcessVerificationResult VerifyProcess(
        int processId,
        LaunchTargetIdentity identity,
        uint expectedSessionId,
        long? expectedCreationFileTimeUtc = null)
    {
        if (ForcedVerificationStatus is ProcessVerificationStatus forced)
            return new ProcessVerificationResult(forced);
        if (!_processes.TryGetValue(processId, out ProcessStamp? process))
            return new ProcessVerificationResult(ProcessVerificationStatus.Exited);
        if (!SameIdentity(process.Identity, identity) || process.SessionId != expectedSessionId)
            return new ProcessVerificationResult(ProcessVerificationStatus.IdentityChanged, process);
        if (expectedCreationFileTimeUtc is long expected && process.CreationFileTimeUtc != expected)
            return new ProcessVerificationResult(ProcessVerificationStatus.IdentityChanged, process);
        return new ProcessVerificationResult(ProcessVerificationStatus.Confirmed, process);
    }

    public WindowScanResult FindWindows(ProcessStamp process) =>
        _windows.TryGetValue(process.ProcessId, out IReadOnlyList<WindowCandidate>? windows)
            ? new WindowScanResult(windows, ScanComplete, ScanComplete ? null : 5)
            : new WindowScanResult([]);

    public WindowActivationResult Activate(WindowCandidate candidate)
    {
        Interlocked.Increment(ref _activationCalls);
        if (ActivationStatus == WindowActivationStatus.ForegroundDenied) Interlocked.Increment(ref _notificationCalls);
        return new WindowActivationResult(ActivationStatus);
    }

    public static bool SameIdentity(LaunchTargetIdentity left, LaunchTargetIdentity right) =>
        left.GetType() == right.GetType() && string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);
}

internal sealed class FakeInstanceClock : IInstanceClock
{
    private long _timestamp;
    public Func<TimeSpan, CancellationToken, Task>? DelayHandler { get; set; }
    public int DelayCount { get; private set; }
    public long Timestamp => Interlocked.Read(ref _timestamp);
    public TimeSpan ElapsedSince(long timestamp) => TimeSpan.FromTicks(Timestamp - timestamp);

    public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Add(ref _timestamp, delay.Ticks);
        DelayCount++;
        if (DelayHandler is not null) await DelayHandler(delay, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}

internal sealed class FileBackedWindowPlatform(string stateDirectory) : IWindowPlatform
{
    private readonly string _processMarker = Path.Combine(stateDirectory, "process.ready");
    private readonly string _windowMarker = Path.Combine(stateDirectory, "window.ready");
    private readonly string _imagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
    private const int ProcessId = 60_001;
    private const long CreationTime = 2_000_000;
    public uint CurrentSessionId => checked((uint)Process.GetCurrentProcess().SessionId);

    public ProcessScanResult FindMatchingProcesses(LaunchTargetIdentity identity) =>
        File.Exists(_processMarker) && SameIdentity(identity)
            ? new ProcessScanResult([CreateProcess()])
            : new ProcessScanResult([]);

    public ProcessVerificationResult VerifyProcess(int processId, LaunchTargetIdentity identity, uint expectedSessionId,
        long? expectedCreationFileTimeUtc = null)
    {
        if (!File.Exists(_processMarker)) return new ProcessVerificationResult(ProcessVerificationStatus.Exited);
        ProcessStamp process = CreateProcess();
        if (processId != ProcessId || expectedSessionId != CurrentSessionId || !SameIdentity(identity) ||
            expectedCreationFileTimeUtc is long creation && creation != CreationTime)
            return new ProcessVerificationResult(ProcessVerificationStatus.IdentityChanged, process);
        return new ProcessVerificationResult(ProcessVerificationStatus.Confirmed, process);
    }

    public WindowScanResult FindWindows(ProcessStamp process) =>
        File.Exists(_windowMarker)
            ? new WindowScanResult([new WindowCandidate((nint)90_001, process, true, false, false, false, false, false, true)])
            : new WindowScanResult([]);

    public WindowActivationResult Activate(WindowCandidate candidate) =>
        new(WindowActivationStatus.Activated);

    public LaunchResult DispatchOnce()
    {
        string dispatchPath = Path.Combine(stateDirectory, "dispatched.once");
        using (new FileStream(dispatchPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        File.WriteAllText(_processMarker, string.Create(CultureInfo.InvariantCulture, $"{ProcessId}:{CreationTime}"));
        Thread.Sleep(400);
        File.WriteAllText(_windowMarker, "ready");
        return new LaunchResult(LaunchStatus.Started, ProcessId, ProcessCreationFileTimeUtc: CreationTime);
    }

    private ProcessStamp CreateProcess() => new(ProcessId, CreationTime, CurrentSessionId,
        new ExecutableLaunchIdentity(Path.GetFullPath(_imagePath)));

    private bool SameIdentity(LaunchTargetIdentity identity) =>
        identity is ExecutableLaunchIdentity && string.Equals(identity.Value, Path.GetFullPath(_imagePath), StringComparison.OrdinalIgnoreCase);
}

internal static class NativeTestMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool ShowWindowAsync(nint window, int command);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool IsIconic(nint window);
}
