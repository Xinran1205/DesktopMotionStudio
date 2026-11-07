using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using DesktopMotionStudio.Core.IO;
using DesktopMotionStudio.Core.Shortcuts;

namespace DesktopMotionStudio.Core.Distribution;

internal static class InstalledEntry
{
    internal const string Active = "active";
    internal const string Previous = "active.previous";

    internal static bool AcceptsArguments(bool launcher, string[] args) =>
        launcher ? ManagedLaunchRequest.ParseArguments(args) is not null : args.Length == 0;

    internal static Process Start(string root, bool launcher, string[] args)
    {
        if (!AcceptsArguments(launcher, args)) throw new ArgumentException("Invalid installed entry arguments.", nameof(args));
        using var gate = InstallGate.ForEntry(root);
        if (!gate.Entered) throw new IOException("The installation is busy.");

        using var programs = WindowsFileScope.Open(Path.GetDirectoryName(root)!);
        if (InstallJournal.Read<RemoveIntent>(programs, InstallJournal.RemoveName) is not null)
            throw new InvalidDataException("The installation is being removed.");
        using var install = WindowsFileScope.Open(root);
        InstalledSelection selection = ReadSelection(install);
        string generation = Path.Combine(root, InstalledLayout.Versions, selection.Version.ToString(3));
        string directory = launcher ? Path.Combine(generation, "Launcher") : generation;
        string name = launcher ? InstalledLayout.LauncherName : InstalledLayout.StudioName;
        using var scope = WindowsFileScope.Open(directory);
        using var file = scope.OpenChild(name, WindowsFileScope.ReadAccess);
        var info = WindowsFileScope.ReadInfo(file);
        if (!info.IsSingleRegularFile || info.Length is 0 or > 16 * 1024 * 1024)
            throw new InvalidDataException("Invalid installed application image.");
        using var content = new FileStream(file, FileAccess.Read);
        string expected = launcher ? selection.LauncherHash : selection.StudioHash;
        if (Convert.ToHexString(SHA256.HashData(content)) != expected)
            throw new InvalidDataException("The installed application image changed.");

        // Keep the verified image and directory handles pinned through CreateProcess.
        var start = new ProcessStartInfo(scope.Child(name)) { UseShellExecute = false, WorkingDirectory = directory };
        foreach (string argument in args) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("The installed application could not start.");
    }

    internal static InstalledSelection ReadSelection(WindowsFileScope root)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                try { return Read(Active); }
                catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex)) { return Read(Previous); }
            }
            catch (Win32Exception ex) when (attempt < 4 && WindowsFileScope.IsSharing(ex))
            {
                Thread.Sleep(15);
            }
        }

        InstalledSelection Read(string name)
        {
            // Allow the writer to rename an old selector while this reader uses its retained handle.
            using var handle = root.OpenChild(name, WindowsFileScope.ReadAccess, share: 5);
            return InstalledSelection.Read(handle);
        }
    }
}
