using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SharedPlaybackPoc;

internal static partial class OwnedJobPoc
{
    internal static int AtomicParent()
    {
        using SafeJobHandle job = CreateOwnedJob();
        (int pid, long created) = CreateSuspendedInJob(job);
        Console.WriteLine($"READY:{pid}:{created}"); Console.Out.Flush();
        // No ResumeThread or post-create Assign: the worker never executes its initial thread.
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    private static (int Pid, long CreatedTicks) CreateSuspendedInJob(SafeJobHandle job)
    {
        nuint bytes = 0;
        _ = InitializeProcThreadAttributeList(0, 1, 0, ref bytes);
        if (bytes is 0 or > 1024 * 1024) throw new Win32Exception(Marshal.GetLastPInvokeError());
        nint attributes = Marshal.AllocHGlobal(checked((int)bytes));
        nint jobValue = Marshal.AllocHGlobal(nint.Size);
        bool initialized = false, jobReference = false;
        ProcessInformation process = default;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            initialized = true;
            job.DangerousAddRef(ref jobReference);
            Marshal.WriteIntPtr(jobValue, job.DangerousGetHandle());
            const nuint jobListAttribute = 0x0002000D;
            if (!UpdateProcThreadAttribute(attributes, 0, jobListAttribute, jobValue, (nuint)nint.Size, 0, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            string executable = Environment.ProcessPath!;
            string command = $"\"{executable}\"";
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) command += $" \"{typeof(Program).Assembly.Location}\"";
            command += " --job-worker"; // Fixed owned fixture only, never user command text.
            var startup = new StartupInfoEx { Basic = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfoEx>() }, Attributes = attributes };
            const uint extendedStartup = 0x00080000, noWindow = 0x08000000, suspended = 0x00000004;
            if (!CreateProcessW(executable, new StringBuilder(command), 0, 0, false, extendedStartup | noWindow | suspended, 0, Path.GetDirectoryName(executable)!, ref startup, out process))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (!GetProcessTimes(process.Process, out long created, out _, out _, out _)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            return (checked((int)process.ProcessId), DateTime.FromFileTimeUtc(created).Ticks);
        }
        finally
        {
            if (process.Thread != 0) CloseHandle(process.Thread);
            if (process.Process != 0) CloseHandle(process.Process);
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(jobValue); Marshal.FreeHGlobal(attributes);
            if (jobReference) job.DangerousRelease();
        }
    }

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
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(nint list, uint count, uint flags, ref nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint bytes, nint previous, nint returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessW(string application, StringBuilder command, nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(nint process, out long created, out long exited, out long kernel, out long user);
}
