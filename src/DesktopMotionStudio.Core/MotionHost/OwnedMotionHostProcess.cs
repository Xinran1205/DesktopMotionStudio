using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DesktopMotionStudio.Core.MotionHost;

// Windows-only ownership adapter. The native job exists before the renderer can execute.
internal sealed class OwnedMotionHostProcess : IDisposable
{
    private readonly SafeJobHandle _job;
    private int _disposed;
    internal Process Process { get; }

    private OwnedMotionHostProcess(SafeJobHandle job, Process process) { _job = job; Process = process; }

    internal bool IsInOwnedJob => ContainsProcess(Process.Id);

    internal bool ContainsProcess(int processId)
    {
        using Process process = Process.GetProcessById(processId);
        if (!IsProcessInJob(process.SafeHandle, _job, out bool member)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return member;
    }

    // The internal observer allows real pre-resume fault/parent-death acceptance. Production passes none.
    internal static OwnedMotionHostProcess Start(string executable, string pipeName, Guid requestId,
        Action<OwnedMotionHostProcess>? onSuspended = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("MotionHost requires Windows.");
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"') || requestId == Guid.Empty || pipeName != $"dms-motionhost-{requestId:N}")
            throw new ArgumentException("Invalid owned host start identity.");
        SafeJobHandle job = CreateJob();
        ProcessInformation native = default;
        Process? process = null;
        try
        {
            native = CreateSuspended(job, executable, pipeName, requestId);
            process = Process.GetProcessById(checked((int)native.ProcessId));
            _ = process.SafeHandle; // Secure a managed identity handle while the initial thread is suspended.
            var owned = new OwnedMotionHostProcess(job, process);
            onSuspended?.Invoke(owned);
            uint previousSuspendCount = ResumeThread(native.Thread);
            if (previousSuspendCount == uint.MaxValue) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (previousSuspendCount != 1) throw new InvalidOperationException("Unexpected renderer initial-thread suspension state.");
            return owned;
        }
        catch
        {
            job.Dispose(); // Includes any native child even if obtaining its managed wrapper failed.
            if (native.Process != 0) _ = WaitForSingleObject(native.Process, 1000);
            process?.Dispose();
            throw;
        }
        finally
        {
            if (native.Thread != 0) CloseHandle(native.Thread);
            if (native.Process != 0) CloseHandle(native.Process);
        }
    }

    internal void Terminate() => _job.Dispose(); // Idempotent, no PID lookup and no target authority.

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Terminate();
        Process.Dispose();
    }

    private static SafeJobHandle CreateJob()
    {
        SafeJobHandle job = CreateJobObjectW(0, null); // Unnamed, non-inherited, held only by this parent.
        if (job.IsInvalid) { int error = Marshal.GetLastPInvokeError(); job.Dispose(); throw new Win32Exception(error); }
        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(job, 9, in limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        { int error = Marshal.GetLastPInvokeError(); job.Dispose(); throw new Win32Exception(error); }
        return job;
    }

    private static ProcessInformation CreateSuspended(SafeJobHandle job, string executable, string pipeName, Guid requestId)
    {
        nuint bytes = 0;
        _ = InitializeProcThreadAttributeList(0, 1, 0, ref bytes);
        if (bytes is 0 or > 1024 * 1024) throw new Win32Exception(Marshal.GetLastPInvokeError());
        nint attributes = 0, jobValue = 0;
        bool initialized = false, jobReference = false;
        try
        {
            attributes = Marshal.AllocHGlobal(checked((int)bytes));
            jobValue = Marshal.AllocHGlobal(nint.Size);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            initialized = true;
            job.DangerousAddRef(ref jobReference);
            Marshal.WriteIntPtr(jobValue, job.DangerousGetHandle());
            const nuint jobListAttribute = 0x0002000D;
            if (!UpdateProcThreadAttribute(attributes, 0, jobListAttribute, jobValue, (nuint)nint.Size, 0, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var startup = new StartupInfoEx { Basic = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfoEx>() }, Attributes = attributes };
            // Explicit application path; all arguments are generated identifiers, never user command text.
            var command = new StringBuilder($"\"{executable}\" --pipe {pipeName} --request-id {requestId:D}");
            const uint extendedStartup = 0x00080000, noWindow = 0x08000000, suspended = 0x00000004;
            if (!CreateProcessW(executable, command, 0, 0, false, extendedStartup | noWindow | suspended, 0,
                    Path.GetDirectoryName(executable)!, ref startup, out ProcessInformation process))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            return process;
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (jobValue != 0) Marshal.FreeHGlobal(jobValue);
            if (attributes != 0) Marshal.FreeHGlobal(attributes);
            if (jobReference) job.DangerousRelease();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint LimitFlags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public nint Reserved, Desktop, Title;
        public uint X, Y, Width, Height, Columns, Rows, Fill, Flags;
        public ushort ShowWindow, ReservedBytes;
        public nint ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Basic; public nint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public nint Process, Thread; public uint ProcessId, ThreadId; }
    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeJobHandle CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeJobHandle job, int informationClass, in ExtendedLimits information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsProcessInJob(SafeProcessHandle process, SafeJobHandle job, [MarshalAs(UnmanagedType.Bool)] out bool inJob);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(nint list, uint count, uint flags, ref nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint bytes, nint previous, nint returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessW(string application, StringBuilder command, nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
