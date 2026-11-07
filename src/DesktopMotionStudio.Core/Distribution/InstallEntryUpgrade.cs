using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using DesktopMotionStudio.Core.IO;
using Microsoft.Win32.SafeHandles;

namespace DesktopMotionStudio.Core.Distribution;

[SupportedOSPlatform("windows")]
internal static class InstallEntryUpgrade
{
    private const string PreviousSuffix = ".previous";
    private const string NextSuffix = ".next";
    private const int MaxOwnerBytes = 512 * 1024;
    internal const string RetiredOwnerName = InstallJournal.OwnerName + PreviousSuffix;

    internal static void Complete(WindowsFileScope root, InstallOwner owner, EntryUpgradeIntent images,
        Stream entry, Action<InstallStep>? checkpoint)
    {
        using var launcher = InstallCoordinator.OpenOwnedLauncher(root, owner);

        Swap(root, InstalledLayout.StudioName, images.FromHash, images.ToHash, InstallStage.MaxEntryBytes,
            entry, InstallStep.EntryStudioRetired, checkpoint);
        Swap(launcher, InstalledLayout.LauncherName, images.FromHash, images.ToHash, InstallStage.MaxEntryBytes,
            entry, InstallStep.EntryLauncherRetired, checkpoint);

        byte[] oldOwner = InstallJournal.Encode(owner with { EntryHash = images.FromHash });
        InstallOwner updated = owner with { EntryHash = images.ToHash };
        byte[] newOwner = InstallJournal.Encode(updated);
        string oldOwnerHash = Convert.ToHexString(SHA256.HashData(oldOwner));
        string newOwnerHash = Convert.ToHexString(SHA256.HashData(newOwner));
        using var ownerBytes = new MemoryStream(newOwner, writable: false);
        Swap(root, InstallJournal.OwnerName, oldOwnerHash, newOwnerHash, MaxOwnerBytes, ownerBytes,
            InstallStep.EntryOwnerRetired, checkpoint);

        if (InstallCoordinator.ReadOwner(root) != updated ||
            !InstallJournal.RemoveFile(root, InstalledLayout.StudioName + PreviousSuffix, images.FromHash, InstallStage.MaxEntryBytes) ||
            !InstallJournal.RemoveFile(launcher, InstalledLayout.LauncherName + PreviousSuffix, images.FromHash, InstallStage.MaxEntryBytes) ||
            !InstallJournal.RemoveFile(root, RetiredOwnerName, oldOwnerHash, MaxOwnerBytes))
            throw new InvalidDataException("The updated entry ownership could not be finalized.");
    }

    private static void Swap(WindowsFileScope scope, string name, string oldHash, string newHash, long maximum,
        Stream source, InstallStep retired, Action<InstallStep>? checkpoint)
    {
        string previousName = name + PreviousSuffix;
        string nextName = name + NextSuffix;
        using var current = OpenOptional(scope, name);
        using var previous = OpenOptional(scope, previousName);
        using var next = OpenOptional(scope, nextName);
        if (previous is not null && !InstallJournal.Matches(previous, oldHash, maximum) ||
            next is not null && !InstallJournal.Matches(next, newHash, maximum))
            throw new InvalidDataException("A stable entry transition file changed.");
        if (current is not null && InstallJournal.Matches(current, newHash, maximum))
        {
            if (next is not null) throw new InvalidDataException("The stable entry stage is occupied.");
            return;
        }
        if (current is not null && (previous is not null || !InstallJournal.Matches(current, oldHash, maximum)) ||
            current is null && previous is null)
            throw new InvalidDataException("The stable entry being updated changed.");

        if (next is null) Prepare(scope, nextName, source, newHash, maximum);
        using var prepared = next is null ? OpenOptional(scope, nextName) : null;
        SafeFileHandle stage = next ?? prepared ?? throw new IOException("The entry stage disappeared.");
        if (!InstallJournal.Matches(stage, newHash, maximum))
            throw new InvalidDataException("The entry stage changed before promotion.");

        if (current is not null) scope.Publish(current, previousName);
        try
        {
            checkpoint?.Invoke(retired);
            scope.Publish(stage, name);
        }
        catch
        {
            // Retained handles identify the original file. No replacement is allowed if another file arrived.
            try { scope.Publish(current ?? previous!, name); }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidDataException) { }
            throw;
        }
    }

    private static void Prepare(WindowsFileScope scope, string name, Stream source, string hash, long maximum)
    {
        if (!source.CanSeek || source.Length is <= 0 || source.Length > maximum)
            throw new InvalidDataException("The entry update source is unavailable.");
        using var file = scope.OpenChild($".entry-write-{Guid.NewGuid():N}", WindowsFileScope.ReadAccess |
            WindowsFileScope.WriteAccess | WindowsFileScope.DeleteAccess, disposition: WindowsFileScope.CreateNew);
        using var output = new FileStream(file, FileAccess.ReadWrite);
        try
        {
            source.Position = 0;
            source.CopyTo(output);
            output.Flush(flushToDisk: true);
            output.Position = 0;
            if (Convert.ToHexString(SHA256.HashData(output)) != hash)
                throw new InvalidDataException("The stable entry stage did not verify.");
            scope.Publish(file, name);
        }
        catch
        {
            try { WindowsFileScope.Delete(file); }
            catch (Win32Exception) { }
            throw;
        }
    }

    private static SafeFileHandle? OpenOptional(WindowsFileScope scope, string name)
    {
        try { return scope.OpenChild(name, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess); }
        catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex)) { return null; }
    }
}
