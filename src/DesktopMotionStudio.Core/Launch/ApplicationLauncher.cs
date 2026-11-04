using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopMotionStudio.Core.Launch;

public sealed class ApplicationLauncher
{
    private readonly Func<ProcessStartInfo, ProcessStartDetails?> _processStarter;
    private readonly LaunchTargetIdentityResolver _identityResolver;

    public ApplicationLauncher() : this(StartProcess, new LaunchTargetIdentityResolver(), true) { }

    public ApplicationLauncher(Func<ProcessStartInfo, int?> processStarter)
        : this(processStarter, new LaunchTargetIdentityResolver()) { }

    public ApplicationLauncher(Func<ProcessStartInfo, int?> processStarter, LaunchTargetIdentityResolver identityResolver)
        : this(startInfo =>
        {
            int? processId = processStarter(startInfo);
            return processId is int id ? new ProcessStartDetails(id, null) : null;
        }, identityResolver, true)
    { }

    private ApplicationLauncher(
        Func<ProcessStartInfo, ProcessStartDetails?> processStarter,
        LaunchTargetIdentityResolver identityResolver,
        bool _)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
    }

    public LaunchTargetResolution ResolveIdentity(LaunchTarget target) => _identityResolver.Resolve(target);

    public LaunchResult Launch(LaunchTarget target)
    {
        LaunchTargetResolution resolution = ResolveIdentity(target);
        if (!resolution.Succeeded || resolution.Target is null)
            return new LaunchResult(resolution.Status == LaunchTargetResolutionStatus.InvalidTarget
                ? LaunchStatus.InvalidTarget : LaunchStatus.StartFailed, NativeErrorCode: resolution.NativeErrorCode);

        return Launch(resolution.Target);
    }

    public LaunchResult Launch(ResolvedLaunchTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!OperatingSystem.IsWindows()) return new LaunchResult(LaunchStatus.InvalidTarget);

        LaunchTargetResolution validation = target switch
        {
            ResolvedExecutableLaunch executable => _identityResolver.Resolve(new ExecutableTarget(
                executable.ExecutableIdentity.Path, executable.Arguments, executable.WorkingDirectory)),
            ResolvedPackagedLaunch packaged => _identityResolver.Resolve(new PackagedAppTarget(packaged.PackagedIdentity.Aumid)),
            _ => new LaunchTargetResolution(null, LaunchTargetResolutionStatus.InvalidTarget)
        };
        if (!validation.Succeeded || validation.Target is null ||
            !SameIdentity(validation.Target.Identity, target.Identity))
            return new LaunchResult(LaunchStatus.InvalidTarget, NativeErrorCode: validation.NativeErrorCode);

        return validation.Target switch
        {
            ResolvedExecutableLaunch executable => LaunchExecutable(executable),
            ResolvedPackagedLaunch packaged => LaunchPackaged(packaged),
            _ => new LaunchResult(LaunchStatus.InvalidTarget)
        };
    }

    private static bool SameIdentity(LaunchTargetIdentity left, LaunchTargetIdentity right) =>
        left.GetType() == right.GetType() &&
        string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);

    private LaunchResult LaunchExecutable(ResolvedExecutableLaunch target)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = target.ExecutableIdentity.Path,
            WorkingDirectory = target.WorkingDirectory,
            UseShellExecute = false
        };
        foreach (string argument in target.Arguments) startInfo.ArgumentList.Add(argument);

        try
        {
            ProcessStartDetails? started = _processStarter(startInfo);
            return started is null
                ? new LaunchResult(LaunchStatus.StartFailed)
                : new LaunchResult(LaunchStatus.Started, started.ProcessId,
                    ProcessCreationFileTimeUtc: started.CreationFileTimeUtc);
        }
        catch (Win32Exception ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.NativeErrorCode); }
        catch (IOException ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.HResult); }
        catch (UnauthorizedAccessException ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.HResult); }
        catch (InvalidOperationException ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.HResult); }
    }

    private static LaunchResult LaunchPackaged(ResolvedPackagedLaunch target) => RunSta(() =>
    {
        object? manager = null;
        try
        {
            manager = new ApplicationActivationManagerCom();
            int hresult = ((IApplicationActivationManagerCom)manager)
                .ActivateApplication(target.PackagedIdentity.Aumid, null, 0, out uint processId);
            return hresult < 0
                ? new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: hresult)
                : new LaunchResult(LaunchStatus.Activated, processId <= int.MaxValue ? (int)processId : null,
                    ProcessCreationFileTimeUtc: processId <= int.MaxValue ? ReadProcessCreationTime((int)processId) : null);
        }
        catch (COMException ex) { return new LaunchResult(LaunchStatus.StartFailed, NativeErrorCode: ex.HResult); }
        finally
        {
            if (OperatingSystem.IsWindows() && manager is not null && Marshal.IsComObject(manager))
                Marshal.FinalReleaseComObject(manager);
        }
    });

    private static ProcessStartDetails? StartProcess(ProcessStartInfo startInfo)
    {
        using Process? process = Process.Start(startInfo);
        if (process is null) return null;
        return new ProcessStartDetails(process.Id, ReadProcessCreationTime(process));
    }

    private static long? ReadProcessCreationTime(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return ReadProcessCreationTime(process);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        { return null; }
    }

    private static long? ReadProcessCreationTime(Process process)
    {
        try { return process.StartTime.ToUniversalTime().ToFileTimeUtc(); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        { return null; }
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

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private sealed class ApplicationActivationManagerCom { }

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManagerCom
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            uint options,
            out uint processId);
    }

    private sealed record ProcessStartDetails(int ProcessId, long? CreationFileTimeUtc);
}
