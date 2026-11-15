$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') {
    throw 'This probe requires an interactive Windows desktop.'
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$targetExe = Join-Path $repoRoot 'src\DesktopMotionStudio.WindowHost.Poc\bin\Release\net10.0-windows\win-x64\publish\DesktopMotionStudio.WindowHost.Poc.exe'
if (-not (Test-Path -LiteralPath $targetExe -PathType Leaf)) {
    throw "Build the WindowHost POC first: $targetExe"
}

$source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public delegate bool DmsEnumWindowsProc(IntPtr window, IntPtr data);

public sealed class DmsWindowProbeResult
{
    public IntPtr Handle { get; set; }
    public uint ProcessId { get; set; }
    public string ImagePath { get; set; }
    public bool Visible { get; set; }
    public bool CloakQuerySucceeded { get; set; }
    public uint Cloaked { get; set; }
    public bool WasMinimized { get; set; }
    public bool RestoreRequestAccepted { get; set; }
    public bool ForegroundRequestAccepted { get; set; }
    public bool BecameForeground { get; set; }
    public bool FlashWindowWasActiveBeforeCall { get; set; }
}

public sealed class DmsAumidWindowResult
{
    public long WindowHandle { get; set; }
    public uint TopLevelProcessId { get; set; }
    public string TopLevelProcessAumid { get; set; }
    public bool TargetProcessIsTopLevel { get; set; }
    public bool TargetProcessIsDescendant { get; set; }
    public bool Visible { get; set; }
    public uint Cloaked { get; set; }
}

[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"), ClassInterface(ClassInterfaceType.None)]
public class DmsApplicationActivationManager
{
}

[ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDmsApplicationActivationManager
{
    [PreserveSig]
    int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
}

public static class DmsWindowIdentityProbe
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int DwmwaCloaked = 14;
    private const int SwRestore = 9;
    private const int SwMinimize = 6;
    private const uint FlashwCaption = 0x1;
    private const uint FlashwTray = 0x2;
    private const uint FlashwTimerNoForeground = 0xC;

    public static int ActivateAumid(string appUserModelId, out uint processId)
    {
        var manager = (IDmsApplicationActivationManager)new DmsApplicationActivationManager();
        try
        {
            return manager.ActivateApplication(appUserModelId, null, 0, out processId);
        }
        finally
        {
            if (Marshal.IsComObject(manager)) Marshal.FinalReleaseComObject(manager);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(DmsEnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumChildWindows(IntPtr parent, DmsEnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder imageName, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out uint value, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetApplicationUserModelId")]
    private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder userModelId);

    public static List<DmsWindowProbeResult> FindByImagePath(string expectedPath)
    {
        var matches = new List<DmsWindowProbeResult>();
        string expected = System.IO.Path.GetFullPath(expectedPath);
        DmsEnumWindowsProc callback = delegate(IntPtr window, IntPtr data)
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            string imagePath = ReadImagePath(processId);
            if (imagePath != null && String.Equals(System.IO.Path.GetFullPath(imagePath), expected, StringComparison.OrdinalIgnoreCase))
            {
                uint cloaked;
                int result = DwmGetWindowAttribute(window, DwmwaCloaked, out cloaked, sizeof(uint));
                matches.Add(new DmsWindowProbeResult
                {
                    Handle = window,
                    ProcessId = processId,
                    ImagePath = imagePath,
                    Visible = IsWindowVisible(window),
                    CloakQuerySucceeded = result >= 0,
                    Cloaked = result >= 0 ? cloaked : UInt32.MaxValue
                });
            }
            return true;
        };

        if (!EnumWindows(callback, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "EnumWindows failed.");
        GC.KeepAlive(callback);
        return matches;
    }

    public static DmsWindowProbeResult RestoreAndActivate(DmsWindowProbeResult candidate)
    {
        candidate.WasMinimized = IsIconic(candidate.Handle);
        if (candidate.WasMinimized)
        {
            candidate.RestoreRequestAccepted = ShowWindowAsync(candidate.Handle, SwRestore);
            Thread.Sleep(150);
        }
        candidate.ForegroundRequestAccepted = SetForegroundWindow(candidate.Handle);
        candidate.BecameForeground = GetForegroundWindow() == candidate.Handle;
        if (!candidate.BecameForeground)
        {
            var info = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf(typeof(FlashInfo)),
                Window = candidate.Handle,
                Flags = FlashwCaption | FlashwTray,
                Count = 1,
                Timeout = 0
            };
            candidate.FlashWindowWasActiveBeforeCall = FlashWindowEx(ref info);
        }
        return candidate;
    }

    public static void MinimizeForTest(IntPtr window)
    {
        ShowWindowAsync(window, SwMinimize);
        Thread.Sleep(150);
    }

    public static DmsAumidWindowResult[] ProbeAumidProcess(uint processId)
    {
        string targetAumid = ReadAumid(processId);
        var matches = new List<DmsAumidWindowResult>();
        DmsEnumWindowsProc callback = delegate(IntPtr window, IntPtr data)
        {
            uint topProcessId;
            GetWindowThreadProcessId(window, out topProcessId);
            bool isTop = topProcessId == processId;
            bool isDescendant = !isTop && HasDescendantProcess(window, processId);
            if (!isTop && !isDescendant) return true;
            uint cloaked;
            int result = DwmGetWindowAttribute(window, DwmwaCloaked, out cloaked, sizeof(uint));
            matches.Add(new DmsAumidWindowResult
            {
                WindowHandle = window.ToInt64(),
                TopLevelProcessId = topProcessId,
                TopLevelProcessAumid = ReadAumid(topProcessId),
                TargetProcessIsTopLevel = isTop,
                TargetProcessIsDescendant = isDescendant,
                Visible = IsWindowVisible(window),
                Cloaked = result >= 0 ? cloaked : UInt32.MaxValue
            });
            return true;
        };
        EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return matches.ToArray();
    }

    private static bool HasDescendantProcess(IntPtr parent, uint targetProcessId)
    {
        bool found = false;
        DmsEnumWindowsProc callback = delegate(IntPtr child, IntPtr data)
        {
            uint childProcessId;
            GetWindowThreadProcessId(child, out childProcessId);
            if (childProcessId == targetProcessId)
            {
                found = true;
                return false;
            }
            return true;
        };
        EnumChildWindows(parent, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return found;
    }

    private static string ReadAumid(uint processId)
    {
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) return "open-error:" + Marshal.GetLastWin32Error();
        try
        {
            uint length = 0;
            int result = GetApplicationUserModelId(process, ref length, null);
            if (result != 122 || length == 0) return "query-error:" + result;
            var buffer = new StringBuilder((int)length);
            result = GetApplicationUserModelId(process, ref length, buffer);
            return result == 0 ? buffer.ToString() : "query-error:" + result;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string ReadImagePath(uint processId)
    {
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(32768);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
'@

Add-Type -TypeDefinition $source
$process = $null
try {
    $process = Start-Process -FilePath $targetExe -ArgumentList @('--mode', 'centered') -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    $matches = @()
    while ([DateTime]::UtcNow -lt $deadline -and $matches.Count -eq 0) {
        $matches = @([DmsWindowIdentityProbe]::FindByImagePath($targetExe).ToArray() | Where-Object {
            $_.Visible -and $_.CloakQuerySucceeded -and $_.Cloaked -eq 0
        })
        if ($matches.Count -eq 0) { Start-Sleep -Milliseconds 100 }
    }

    if ($matches.Count -ne 1) {
        throw "Expected exactly one exact-path window candidate; found $($matches.Count)."
    }

    [DmsWindowIdentityProbe]::MinimizeForTest($matches[0].Handle)
    $result = [DmsWindowIdentityProbe]::RestoreAndActivate($matches[0])
    [pscustomobject]@{
        WindowHandle = $result.Handle.ToInt64()
        ProcessId = $result.ProcessId
        ExactImagePathMatch = [string]::Equals([System.IO.Path]::GetFullPath($result.ImagePath), [System.IO.Path]::GetFullPath($targetExe), [StringComparison]::OrdinalIgnoreCase)
        Visible = $result.Visible
        DwmCloakQuerySucceeded = $result.CloakQuerySucceeded
        Cloaked = $result.Cloaked
        WasMinimized = $result.WasMinimized
        RestoreRequestAccepted = $result.RestoreRequestAccepted
        ForegroundRequestAccepted = $result.ForegroundRequestAccepted
        BecameForeground = $result.BecameForeground
        FlashWindowWasActiveBeforeCall = $result.FlashWindowWasActiveBeforeCall
    } | ConvertTo-Json -Compress
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(3000)) {
            $process.Kill()
            [void]$process.WaitForExit(3000)
        }
    }
}

$packageAumid = $env:DMS_TEST_AUMID
if (-not [string]::IsNullOrWhiteSpace($packageAumid)) {
    $processId = [uint32]0
    $hresult = [DmsWindowIdentityProbe]::ActivateAumid($packageAumid, [ref]$processId)
    if ($hresult -lt 0) { throw "ActivateApplication failed with HRESULT $hresult." }
    Start-Sleep -Milliseconds 500
    [pscustomobject]@{
        PackagedActivationHResult = $hresult
        ProcessId = $processId
        ProcessAumidAndWindowAssociations = [DmsWindowIdentityProbe]::ProbeAumidProcess($processId)
    } | ConvertTo-Json -Depth 4 -Compress
}
