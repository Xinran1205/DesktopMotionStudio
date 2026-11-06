using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopMotionStudio.Core.Launch;

internal sealed record ShellLinkTargetReadResult(
    string? ExecutablePath,
    IReadOnlyList<string>? Arguments,
    string? WorkingDirectory,
    LaunchTargetResolutionStatus Status,
    int? NativeErrorCode = null);

internal static class ShellLinkTargetReader
{
    private const int MaxArgumentCount = 256;

    public static ShellLinkTargetReadResult Read(string shortcutPath)
    {
        if (!LaunchTargetIdentityResolver.IsLocalPath(shortcutPath) || !File.Exists(shortcutPath) ||
            !string.Equals(Path.GetExtension(shortcutPath), ".lnk", StringComparison.OrdinalIgnoreCase))
            return Invalid();

        object? link = null;
        try
        {
            link = new ShellLinkCom();
            ((IPersistFileCom)link).Load(shortcutPath, 0);
            var shellLink = (IShellLinkWCom)link;
            var executablePath = new StringBuilder(32_768);
            var workingDirectory = new StringBuilder(32_768);
            var arguments = new StringBuilder(32_768);
            if (shellLink.GetPath(executablePath, executablePath.Capacity, IntPtr.Zero, 0) < 0 ||
                shellLink.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity) < 0 ||
                shellLink.GetArguments(arguments, arguments.Capacity) < 0)
                return Invalid();

            string[] parsedArguments = ParseWindowsArguments(arguments.ToString());
            return new ShellLinkTargetReadResult(
                executablePath.ToString(), parsedArguments,
                workingDirectory.Length == 0 ? null : workingDirectory.ToString(),
                LaunchTargetResolutionStatus.Resolved);
        }
        catch (COMException ex) { return Failed(ex.HResult); }
        catch (Win32Exception ex) { return Failed(ex.NativeErrorCode); }
        catch (ArgumentException) { return Invalid(); }
        catch (IOException ex) { return Failed(ex.HResult); }
        catch (UnauthorizedAccessException ex) { return Failed(ex.HResult); }
        finally
        {
            if (OperatingSystem.IsWindows() && link is not null && Marshal.IsComObject(link))
                Marshal.FinalReleaseComObject(link);
        }
    }

    private static string[] ParseWindowsArguments(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        IntPtr argv = CommandLineToArgvW(commandLine, out int argc);
        if (argv == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (argc > MaxArgumentCount) throw new ArgumentException("Shortcut contains too many arguments.");
            var result = new string[argc];
            for (int i = 0; i < argc; i++)
                result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? string.Empty;
            return result;
        }
        finally { LocalFree(argv); }
    }

    private static ShellLinkTargetReadResult Invalid(int? error = null) =>
        new(null, null, null, LaunchTargetResolutionStatus.InvalidTarget, error);

    private static ShellLinkTargetReadResult Failed(int? error) =>
        new(null, null, null, LaunchTargetResolutionStatus.ResolutionFailed, error);

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal sealed class ShellLinkCom { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkWCom
    {
        [PreserveSig] int GetPath(StringBuilder path, int maxPath, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr itemIdList);
        [PreserveSig] int SetIDList(IntPtr itemIdList);
        [PreserveSig] int GetDescription(StringBuilder name, int maxName);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetWorkingDirectory(StringBuilder directory, int maxPath);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        [PreserveSig] int GetArguments(StringBuilder arguments, int maxPath);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int showCommand);
        [PreserveSig] int SetShowCmd(int showCommand);
        [PreserveSig] int GetIconLocation(StringBuilder iconPath, int maxPath, out int iconIndex);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        [PreserveSig] int SetRelativePath(string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFileCom
    {
        [PreserveSig] int GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile(out IntPtr fileName);
    }
}
