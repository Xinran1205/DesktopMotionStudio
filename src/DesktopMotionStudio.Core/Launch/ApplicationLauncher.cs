using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopMotionStudio.Core.Launch;

public sealed class ApplicationLauncher
{
    private const int MaxArgumentCount = 256;
    private const int MaxCommandLineLength = 30_000;
    private const int MaxAumidLength = 129; // APPLICATION_USER_MODEL_ID_MAX_LENGTH includes the null terminator.
    private readonly Func<ProcessStartInfo, int?> _processStarter;

    public ApplicationLauncher() : this(StartProcess) { }

    public ApplicationLauncher(Func<ProcessStartInfo, int?> processStarter) =>
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));

    public LaunchResult Launch(LaunchTarget target)
    {
        if (!OperatingSystem.IsWindows()) return new LaunchResult(LaunchStatus.InvalidTarget);
        return target switch
        {
            ExecutableTarget executable => LaunchExecutable(executable),
            ShortcutTarget shortcut => LaunchShortcut(shortcut),
            PackagedAppTarget packaged => LaunchPackaged(packaged),
            _ => new LaunchResult(LaunchStatus.InvalidTarget)
        };
    }

    private LaunchResult LaunchExecutable(ExecutableTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Path) || !Path.IsPathFullyQualified(target.Path) || !string.Equals(Path.GetExtension(target.Path), ".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(target.Path) || target.Arguments is null || target.Arguments.Count > MaxArgumentCount ||
            target.Arguments.Any(argument => argument is null) || target.Arguments.Sum(argument => (long)argument.Length + 1) > MaxCommandLineLength)
            return new LaunchResult(LaunchStatus.InvalidTarget);

        string workingDirectory = target.WorkingDirectory ?? Path.GetDirectoryName(target.Path)!;
        if (!Path.IsPathFullyQualified(workingDirectory) || !Directory.Exists(workingDirectory))
            return new LaunchResult(LaunchStatus.InvalidTarget);

        var startInfo = new ProcessStartInfo
        {
            FileName = target.Path,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        foreach (string argument in target.Arguments) startInfo.ArgumentList.Add(argument);

        try
        {
            int? processId = _processStarter(startInfo);
            return processId is null
                ? new LaunchResult(LaunchStatus.StartFailed)
                : new LaunchResult(LaunchStatus.Started, processId);
        }
        catch (Win32Exception ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.NativeErrorCode); }
        catch (IOException ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.HResult); }
        catch (UnauthorizedAccessException ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.HResult); }
        catch (InvalidOperationException ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.HResult); }
    }

    private LaunchResult LaunchShortcut(ShortcutTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Path) || !Path.IsPathFullyQualified(target.Path) || !File.Exists(target.Path) ||
            !string.Equals(Path.GetExtension(target.Path), ".lnk", StringComparison.OrdinalIgnoreCase))
            return new LaunchResult(LaunchStatus.InvalidTarget);

        object? link = null;
        try
        {
            link = new ShellLinkCom();
            ((IPersistFileCom)link).Load(target.Path, 0);
            var shellLink = (IShellLinkWCom)link;
            int resolveResult = shellLink.Resolve(IntPtr.Zero, 1); // SLR_NO_UI
            if (resolveResult < 0) return new LaunchResult(LaunchStatus.InvalidTarget, NativeErrorCode: resolveResult);

            var executablePath = new System.Text.StringBuilder(32_768);
            var workingDirectory = new System.Text.StringBuilder(32_768);
            var arguments = new System.Text.StringBuilder(32_768);
            if (shellLink.GetPath(executablePath, executablePath.Capacity, IntPtr.Zero, 0) < 0 ||
                shellLink.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity) < 0 ||
                shellLink.GetArguments(arguments, arguments.Capacity) < 0)
                return new LaunchResult(LaunchStatus.InvalidTarget);

            if (!string.Equals(Path.GetExtension(executablePath.ToString()), ".exe", StringComparison.OrdinalIgnoreCase))
                return new LaunchResult(LaunchStatus.InvalidTarget);

            string[] parsedArguments = ParseWindowsArguments(arguments.ToString());
            string? directory = workingDirectory.Length == 0 ? null : workingDirectory.ToString();
            return LaunchExecutable(new ExecutableTarget(executablePath.ToString(), parsedArguments, directory));
        }
        catch (COMException ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.HResult); }
        catch (Win32Exception ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.NativeErrorCode); }
        catch (ArgumentException) { return new LaunchResult(LaunchStatus.InvalidTarget); }
        catch (IOException ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.HResult); }
        catch (UnauthorizedAccessException ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.HResult); }
        finally
        {
            if (OperatingSystem.IsWindows() && link is not null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
        }
    }

    private static int? StartProcess(ProcessStartInfo startInfo)
    {
        using Process? process = Process.Start(startInfo);
        return process?.Id;
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

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static LaunchResult LaunchPackaged(PackagedAppTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Aumid) || target.Aumid.Length > MaxAumidLength || target.Aumid.Any(char.IsControl) ||
            target.Aumid.IndexOf('!') <= 0 || target.Aumid.IndexOf('!') == target.Aumid.Length - 1 ||
            target.Aumid.IndexOf('!') != target.Aumid.LastIndexOf('!'))
            return new LaunchResult(LaunchStatus.InvalidTarget);

        return RunSta(() =>
        {
            object? manager = null;
            try
            {
                manager = new ApplicationActivationManagerCom();
                int hresult = ((IApplicationActivationManagerCom)manager)
                    .ActivateApplication(target.Aumid, null, 0, out uint processId);
                return hresult < 0
                    ? new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: hresult)
                    : new LaunchResult(LaunchStatus.Activated, processId <= int.MaxValue ? (int)processId : null);
            }
            catch (COMException ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.HResult); }
            finally
            {
                if (OperatingSystem.IsWindows() && manager is not null && Marshal.IsComObject(manager)) Marshal.FinalReleaseComObject(manager);
            }
        });
    }

    private static LaunchResult RunSta(Func<LaunchResult> action)
    {
        if (!OperatingSystem.IsWindows()) return new LaunchResult(LaunchStatus.InvalidTarget);
        LaunchResult? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return failure is null && result is not null
            ? result
            : new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: failure?.HResult);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLinkCom { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkWCom
    {
        [PreserveSig] int GetPath(System.Text.StringBuilder path, int maxPath, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr itemIdList);
        [PreserveSig] int SetIDList(IntPtr itemIdList);
        [PreserveSig] int GetDescription(System.Text.StringBuilder name, int maxName);
        [PreserveSig] int SetDescription(string name);
        [PreserveSig] int GetWorkingDirectory(System.Text.StringBuilder directory, int maxPath);
        [PreserveSig] int SetWorkingDirectory(string directory);
        [PreserveSig] int GetArguments(System.Text.StringBuilder arguments, int maxPath);
        [PreserveSig] int SetArguments(string arguments);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int showCommand);
        [PreserveSig] int SetShowCmd(int showCommand);
        [PreserveSig] int GetIconLocation(System.Text.StringBuilder iconPath, int maxPath, out int iconIndex);
        [PreserveSig] int SetIconLocation(string iconPath, int iconIndex);
        [PreserveSig] int SetRelativePath(string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr window, uint flags);
        [PreserveSig] int SetPath(string path);
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFileCom
    {
        [PreserveSig] int GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([System.Runtime.InteropServices.MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([System.Runtime.InteropServices.MarshalAs(UnmanagedType.LPWStr)] string fileName, bool remember);
        void SaveCompleted([System.Runtime.InteropServices.MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile(out System.IntPtr fileName);
    }

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private sealed class ApplicationActivationManagerCom { }

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManagerCom
    {
        [PreserveSig]
        int ActivateApplication(
            [System.Runtime.InteropServices.MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [System.Runtime.InteropServices.MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            uint options,
            out uint processId);
    }
}
