using System.Diagnostics;
using System.ComponentModel;
using DesktopMotionStudio.Core.Distribution;
using DesktopMotionStudio.Core.IO;

namespace DesktopMotionStudio.Entry;

internal static class UninstallWorker
{
    private const string Image = "DesktopMotionStudio.Entry.exe";

    internal static void Start(string root)
    {
        using var scope = WindowsFileScope.Open(root);
        InstallOwner owner = InstallCoordinator.ReadOwnerRecord(scope);
        using var image = scope.OpenChild(InstalledLayout.StudioName, WindowsFileScope.ReadAccess);
        if (!InstallJournal.Matches(image, owner.EntryHash, 128 * 1024 * 1024))
            throw new InvalidDataException("The uninstaller source changed.");
        string tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        using var temp = WindowsFileScope.Open(tempRoot);
        using var directory = temp.OpenDirectory($"dms-remove-{owner.EntryHash}", WindowsFileScope.OpenIf);
        try
        {
            using var existing = directory.OpenChild(Image, WindowsFileScope.ReadAccess);
            if (!InstallJournal.Matches(existing, owner.EntryHash, 128 * 1024 * 1024))
                throw new InvalidDataException("The uninstaller work slot is occupied.");
        }
        catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex))
        {
            using var source = new FileStream(image, FileAccess.Read);
            using var handle = directory.OpenChild($".copy-{Guid.NewGuid():N}", WindowsFileScope.ReadAccess |
                WindowsFileScope.WriteAccess | WindowsFileScope.DeleteAccess, disposition: WindowsFileScope.CreateNew);
            using var output = new FileStream(handle, FileAccess.ReadWrite);
            try
            {
                source.Position = 0;
                source.CopyTo(output);
                output.Flush(flushToDisk: true);
                if (!InstallJournal.Matches(handle, owner.EntryHash, 128 * 1024 * 1024))
                    throw new InvalidDataException("The uninstaller copy did not verify.");
                directory.Publish(handle, Image);
            }
            catch
            {
                try { WindowsFileScope.Delete(handle); }
                catch (Win32Exception) { }
                throw;
            }
        }
        using var verified = directory.OpenChild(Image, WindowsFileScope.ReadAccess);
        if (!InstallJournal.Matches(verified, owner.EntryHash, 128 * 1024 * 1024))
            throw new InvalidDataException("The uninstaller copy changed.");
        var start = new ProcessStartInfo(directory.Child(Image)) { UseShellExecute = false, WorkingDirectory = directory.Path };
        start.ArgumentList.Add("--uninstall-worker");
        start.ArgumentList.Add(owner.Id.ToString("N"));
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var process = Process.Start(start) ?? throw new IOException("Could not start the uninstall worker.");
    }

    internal static int Run(string root, string id, string parent)
    {
        if (!Guid.TryParseExact(id, "N", out Guid ownerId) || !int.TryParse(parent, out int pid) || pid <= 0)
            return 2;
        string process = Environment.ProcessPath ?? "";
        string? directory = Path.GetDirectoryName(process);
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (directory is null || Path.GetFileName(process) != Image ||
            !string.Equals(Path.GetDirectoryName(directory), temp, StringComparison.OrdinalIgnoreCase))
            return 2;
        using (var scope = WindowsFileScope.Open(root))
        using (var work = WindowsFileScope.Open(directory))
        using (var executable = work.OpenChild(Image, WindowsFileScope.ReadAccess))
        {
            InstallOwner owner = InstallCoordinator.ReadOwnerRecord(scope);
            if (owner.Id != ownerId || Path.GetFileName(directory) != $"dms-remove-{owner.EntryHash}" ||
                !InstallJournal.Matches(executable, owner.EntryHash, 128 * 1024 * 1024))
                return 2;
        }
        try
        {
            using Process previous = Process.GetProcessById(pid);
            if (!previous.HasExited &&
                string.Equals(previous.MainModule?.FileName, Path.Combine(root, InstalledLayout.StudioName), StringComparison.OrdinalIgnoreCase) &&
                !previous.WaitForExit(5000))
                throw new IOException("The installed uninstaller is still running.");
        }
        catch (ArgumentException) { }
        return Program.ReportRemoval(UninstallCoordinator.RunForCurrentUser(ownerId));
    }
}
