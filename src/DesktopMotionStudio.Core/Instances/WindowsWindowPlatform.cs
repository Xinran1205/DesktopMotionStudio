using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DesktopMotionStudio.Core.Launch;

namespace DesktopMotionStudio.Core.Instances;

public sealed class WindowsWindowPlatform : IWindowPlatform
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int DwmwaCloaked = 14;
    private const int GwOwner = 4;
    private const int GwExstyle = -20;
    private const long WsExToolwindow = 0x00000080L;
    private const int SwRestore = 9;
    private const uint FlashwTray = 0x00000002;

    public uint CurrentSessionId => OperatingSystem.IsWindows()
        ? checked((uint)Process.GetCurrentProcess().SessionId)
        : 0;

    public ProcessScanResult FindMatchingProcesses(LaunchTargetIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!OperatingSystem.IsWindows()) return new ProcessScanResult([], false);

        uint sessionId;
        try { sessionId = checked((uint)Process.GetCurrentProcess().SessionId); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        { return new ProcessScanResult([], false, ex.HResult); }

        var matches = new List<ProcessStamp>();
        try
        {
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != sessionId) continue;
                        ProcessVerificationResult result = ReadAndMatch(process.Id, identity, sessionId);
                        if (result.Status == ProcessVerificationStatus.Confirmed && result.Process is not null)
                            matches.Add(result.Process);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ArgumentException)
                    {
                        // The process may have exited during enumeration. It is not a confirmed identity match.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        { return new ProcessScanResult(matches, false, ex.HResult); }

        return new ProcessScanResult(matches);
    }

    public ProcessVerificationResult VerifyProcess(
        int processId,
        LaunchTargetIdentity identity,
        uint expectedSessionId,
        long? expectedCreationFileTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!OperatingSystem.IsWindows() || processId <= 0)
            return new ProcessVerificationResult(ProcessVerificationStatus.Unavailable);

        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return new ProcessVerificationResult(ProcessVerificationStatus.Exited); }
        catch (InvalidOperationException) { return new ProcessVerificationResult(ProcessVerificationStatus.Exited); }
        using (process)
        {
            try
            {
                if ((uint)process.SessionId != expectedSessionId)
                    return new ProcessVerificationResult(ProcessVerificationStatus.IdentityChanged);
            }
            catch (InvalidOperationException) { return new ProcessVerificationResult(ProcessVerificationStatus.Exited); }
            catch (Win32Exception ex) { return new ProcessVerificationResult(ProcessVerificationStatus.Unavailable, NativeErrorCode: ex.NativeErrorCode); }

            ProcessVerificationResult result = ReadAndMatch(processId, identity, expectedSessionId);
            if (result.Status != ProcessVerificationStatus.Confirmed || result.Process is null) return result;
            if (expectedCreationFileTimeUtc is long expected && result.Process.CreationFileTimeUtc != expected)
                return new ProcessVerificationResult(ProcessVerificationStatus.IdentityChanged, result.Process);
            return result;
        }
    }

    public WindowScanResult FindWindows(ProcessStamp process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!OperatingSystem.IsWindows()) return new WindowScanResult([], false);

        ProcessVerificationResult verification = VerifyProcess(
            process.ProcessId, process.Identity, process.SessionId, process.CreationFileTimeUtc);
        if (verification.Status != ProcessVerificationStatus.Confirmed) return new WindowScanResult([], false, verification.NativeErrorCode);

        nint mainWindow = 0;
        try
        {
            using Process target = Process.GetProcessById(process.ProcessId);
            // MainWindowHandle uses visible/unowned top-level structure; never access MainWindowTitle.
            mainWindow = target.MainWindowHandle;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { }

        var candidates = new List<WindowCandidate>();
        EnumWindowsCallback callback = (handle, parameter) =>
        {
            if (!IsWindow(handle)) return true;
            _ = GetWindowThreadProcessId(handle, out uint ownerProcessId);
            if (ownerProcessId != (uint)process.ProcessId) return true;

            int cloakStatus = DwmGetWindowAttribute(handle, DwmwaCloaked, out int cloaked, sizeof(int));
            long extendedStyle = GetWindowLongPtr(handle, GwExstyle).ToInt64();
            candidates.Add(new WindowCandidate(
                handle,
                process,
                IsWindowVisible(handle),
                cloakStatus < 0 || cloaked != 0,
                IsIconic(handle),
                GetWindow(handle, GwOwner) != 0,
                (extendedStyle & WsExToolwindow) != 0,
                GetForegroundWindow() == handle,
                mainWindow == handle));
            return true;
        };

        if (!EnumWindows(callback, IntPtr.Zero))
            return new WindowScanResult(candidates, false, Marshal.GetLastWin32Error());
        GC.KeepAlive(callback);
        return new WindowScanResult(candidates);
    }

    public WindowActivationResult Activate(WindowCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!OperatingSystem.IsWindows() || candidate.Handle == 0 || !IsWindow(candidate.Handle))
            return new WindowActivationResult(WindowActivationStatus.WindowUnavailable);

        ProcessVerificationResult verification = VerifyProcess(
            candidate.Process.ProcessId, candidate.Process.Identity, candidate.Process.SessionId,
            candidate.Process.CreationFileTimeUtc);
        if (verification.Status == ProcessVerificationStatus.IdentityChanged)
            return new WindowActivationResult(WindowActivationStatus.IdentityChanged);
        if (verification.Status != ProcessVerificationStatus.Confirmed)
            return new WindowActivationResult(WindowActivationStatus.WindowUnavailable, verification.NativeErrorCode);

        _ = GetWindowThreadProcessId(candidate.Handle, out uint ownerProcessId);
        if (ownerProcessId != (uint)candidate.Process.ProcessId)
            return new WindowActivationResult(WindowActivationStatus.WindowUnavailable);
        int cloakStatus = DwmGetWindowAttribute(candidate.Handle, DwmwaCloaked, out int cloaked, sizeof(int));
        long extendedStyle = GetWindowLongPtr(candidate.Handle, GwExstyle).ToInt64();
        if (!IsWindowVisible(candidate.Handle) || cloakStatus < 0 || cloaked != 0 ||
            GetWindow(candidate.Handle, GwOwner) != 0 || (extendedStyle & WsExToolwindow) != 0)
            return new WindowActivationResult(WindowActivationStatus.WindowUnavailable);

        if (IsIconic(candidate.Handle)) _ = ShowWindowAsync(candidate.Handle, SwRestore);
        bool accepted = SetForegroundWindow(candidate.Handle);
        if (accepted || GetForegroundWindow() == candidate.Handle)
            return new WindowActivationResult(WindowActivationStatus.Activated);

        var flash = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Window = candidate.Handle,
            Flags = FlashwTray,
            Count = 3,
            Timeout = 0
        };
        _ = FlashWindowEx(ref flash); // Return value is the prior active state, not success/failure.
        return new WindowActivationResult(WindowActivationStatus.ForegroundDenied);
    }

    private static ProcessVerificationResult ReadAndMatch(int processId, LaunchTargetIdentity identity, uint sessionId)
    {
        nint handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == 0)
        {
            int error = Marshal.GetLastWin32Error();
            return new ProcessVerificationResult(
                error == 87 ? ProcessVerificationStatus.Exited : ProcessVerificationStatus.Unavailable,
                NativeErrorCode: error);
        }

        try
        {
            if (!TryGetCreationTime(handle, out long creation))
                return new ProcessVerificationResult(ProcessVerificationStatus.Unavailable, NativeErrorCode: Marshal.GetLastWin32Error());

            string? actualIdentity;
            int identityError;
            if (identity is ExecutableLaunchIdentity)
                actualIdentity = QueryImagePath(handle, out identityError);
            else if (identity is PackagedAppLaunchIdentity)
                actualIdentity = QueryAumid(handle, out identityError);
            else
                return new ProcessVerificationResult(ProcessVerificationStatus.Unavailable);
            if (actualIdentity is null)
                return new ProcessVerificationResult(ProcessVerificationStatus.Unavailable, NativeErrorCode: identityError);

            string expected = identity.Value;
            string normalizedActual = identity is ExecutableLaunchIdentity
                ? NormalizeImagePath(actualIdentity)
                : actualIdentity;
            StringComparison comparison = identity is ExecutableLaunchIdentity
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.OrdinalIgnoreCase;
            if (!string.Equals(normalizedActual, expected, comparison))
                return new ProcessVerificationResult(ProcessVerificationStatus.IdentityChanged);

            return new ProcessVerificationResult(ProcessVerificationStatus.Confirmed,
                new ProcessStamp(processId, creation, sessionId, identity));
        }
        finally { _ = CloseHandle(handle); }
    }

    private static string NormalizeImagePath(string imagePath)
    {
        try { return Path.GetFullPath(imagePath); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return imagePath; }
    }

    private static string? QueryImagePath(nint process, out int errorCode)
    {
        var path = new StringBuilder(32_768);
        uint length = (uint)path.Capacity;
        bool succeeded = QueryFullProcessImageName(process, 0, path, ref length);
        errorCode = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded ? path.ToString() : null;
    }

    private static string? QueryAumid(nint process, out int errorCode)
    {
        uint length = 0;
        int first = GetApplicationUserModelId(process, ref length, null);
        if (first != 122 || length is 0 or > LaunchTargetIdentityResolver.MaxAumidLength)
        {
            errorCode = first;
            return null;
        }
        var aumid = new StringBuilder((int)length);
        int result = GetApplicationUserModelId(process, ref length, aumid);
        errorCode = result;
        return result == 0 ? aumid.ToString() : null;
    }

    private static bool TryGetCreationTime(nint process, out long creationFileTimeUtc)
    {
        bool success = GetProcessTimes(process, out FileTime creation, out _, out _, out _);
        creationFileTimeUtc = success ? ((long)creation.High << 32) | creation.Low : 0;
        return success;
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder imageName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetProcessTimes")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(nint process, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetApplicationUserModelId")]
    private static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder? appUserModelId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);
}
