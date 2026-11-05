using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SharedPlaybackPoc;

// Isolated risk proof only. The production parent adapter must implement the same ownership contract.
internal static partial class OwnedJobPoc
{
    internal static int Parent()
    {
        using SafeJobHandle job = CreateOwnedJob();
        using Process worker = Start("--job-worker");
        if (!AssignOrKill(job, worker, out int error)) { Console.Error.WriteLine($"ASSIGN_FAILED:{error}"); return 4; }
        if (!IsProcessInJob(worker.SafeHandle, job, out bool childAssigned) || !childAssigned) return 5;
        Console.WriteLine($"READY:{worker.Id}:{worker.StartTime.ToUniversalTime().Ticks}");
        Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    internal static int Checks()
    {
        int failures = 0;
        void Check(string name, bool passed) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}"); if (!passed) failures++; }
        Process? parent = null, hung = null, assigned = null, other = null, rejected = null;
        try
        {
            parent = Start("--job-parent");
            string? ready = parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            string[] parts = ready?.Split(':') ?? [];
            if (parts.Length != 3 || parts[0] != "READY") throw new InvalidOperationException("Job parent did not become ready.");
            int childPid = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            long created = long.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            hung = Process.GetProcessById(childPid);
            if (hung.StartTime.ToUniversalTime().Ticks != created) throw new InvalidOperationException("Worker identity changed.");
            var clock = Stopwatch.StartNew();
            int parentPid = parent.Id;
            parent.Kill();
            bool parentExited = parent.WaitForExit(1000), childExited = hung.WaitForExit(2000);
            Console.WriteLine($"  parent PID={parentPid}, hung worker PID={childPid}, cleanupMs={clock.ElapsedMilliseconds}");
            Check("abrupt parent exit terminates its hung owned worker", parentExited && childExited && clock.Elapsed < TimeSpan.FromMilliseconds(2500));

            parent.Dispose(); hung.Dispose();
            parent = Start("--job-parent-atomic");
            ready = parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            parts = ready?.Split(':') ?? [];
            if (parts.Length != 3 || parts[0] != "READY") throw new InvalidOperationException("Atomic job parent did not become ready.");
            childPid = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            created = long.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            hung = Process.GetProcessById(childPid);
            if (hung.StartTime.ToUniversalTime().Ticks != created) throw new InvalidOperationException("Atomic worker identity changed.");
            clock.Restart(); parentPid = parent.Id; parent.Kill();
            parentExited = parent.WaitForExit(1000); childExited = hung.WaitForExit(2000);
            Console.WriteLine($"  atomic parent PID={parentPid}, suspended worker PID={childPid}, cleanupMs={clock.ElapsedMilliseconds}");
            Check("creation-time job association survives parent death before worker thread resume", parentExited && childExited && clock.Elapsed < TimeSpan.FromMilliseconds(2500));

            using (SafeJobHandle job = CreateOwnedJob())
            {
                assigned = Start("--job-worker"); other = Start("--job-worker");
                if (!AssignOrKill(job, assigned, out int error)) throw new Win32Exception(error);
                using Process current = Process.GetCurrentProcess();
                bool scoped = IsProcessInJob(assigned.SafeHandle, job, out bool a) && a &&
                    IsProcessInJob(other.SafeHandle, job, out bool b) && !b &&
                    IsProcessInJob(current.SafeHandle, job, out bool c) && !c;
                Check("job contains only the assigned renderer fixture, not parent or other process", scoped);
                job.Dispose();
                Check("closing the owned job leaves unassigned fixture alive", assigned.WaitForExit(2000) && !other.HasExited);
            }

            rejected = Start("--job-worker");
            using var invalid = new SafeJobHandle();
            bool accepted = AssignOrKill(invalid, rejected, out int assignmentError);
            Check("failed job assignment reports native failure and cleans only the owned child", !accepted && assignmentError != 0 && rejected.WaitForExit(2000) && other is { HasExited: false });
            bool atomicRejected = false;
            try { CreateSuspendedInJob(invalid); }
            catch (Win32Exception ex) { atomicRejected = ex.NativeErrorCode != 0; }
            Check("invalid creation-time job association rejects before returning an owned worker", atomicRejected && other is { HasExited: false });
        }
        catch (Exception ex) { Console.WriteLine($"FAIL owned-job POC: {ex.GetType().Name}: {ex.Message}"); failures++; }
        finally
        {
            foreach (Process? process in new[] { parent, hung, assigned, other, rejected })
            {
                if (process is null) continue;
                try { if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); } }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { Console.WriteLine($"FAIL fixture cleanup: {ex.GetType().Name}"); failures++; }
                finally { process.Dispose(); }
            }
        }
        return failures == 0 ? 0 : 1;
    }

    private static Process Start(string mode)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(mode);
        return Process.Start(start) ?? throw new InvalidOperationException("Owned worker did not start.");
    }

    private static SafeJobHandle CreateOwnedJob()
    {
        SafeJobHandle job = CreateJobObjectW(0, null); // No inherited handle and no globally named ownership.
        if (job.IsInvalid) { int error = Marshal.GetLastPInvokeError(); job.Dispose(); throw new Win32Exception(error); }
        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
        if (!SetInformationJobObject(job, 9, in limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            int error = Marshal.GetLastPInvokeError(); job.Dispose(); throw new Win32Exception(error);
        }
        return job;
    }

    private static bool AssignOrKill(SafeJobHandle job, Process worker, out int nativeError)
    {
        if (AssignProcessToJobObject(job, worker.SafeHandle)) { nativeError = 0; return true; }
        nativeError = Marshal.GetLastPInvokeError();
        if (!worker.HasExited) worker.Kill();
        if (!worker.WaitForExit(2000)) throw new TimeoutException("Unassigned owned worker failed cleanup.");
        return false;
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
    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeJobHandle CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeJobHandle job, int informationClass, in ExtendedLimits information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsProcessInJob(SafeProcessHandle process, SafeJobHandle job, [MarshalAs(UnmanagedType.Bool)] out bool inJob);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
