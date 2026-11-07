using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using DesktopMotionStudio.Core.IO;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;
using Microsoft.Win32;

namespace DesktopMotionStudio.Core.Distribution;

internal enum RemoveStep { Requested, ProgramFiles, Registration, Shortcuts }
internal sealed record UninstallResult(bool Busy, int PreservedProgramItems, ShortcutCleanupResult Shortcuts);

[SupportedOSPlatform("windows")]
internal sealed class UninstallCoordinator(string programsPath, string menuPath, InstallRegistration registration,
    string receipts, Func<string> desktop)
{
    internal static Guid? PendingOwner()
    {
        using var programs = WindowsFileScope.Open(Path.GetDirectoryName(InstalledLayout.CurrentUserRoot())!);
        return InstallJournal.Read<RemoveIntent>(programs, InstallJournal.RemoveName)?.Owner?.Id;
    }

    internal static UninstallResult RunForCurrentUser(Guid owner, bool resumeOnly = false)
    {
        string programs = Path.GetDirectoryName(InstalledLayout.CurrentUserRoot())!;
        string menu = Environment.GetFolderPath(Environment.SpecialFolder.Programs, Environment.SpecialFolderOption.DoNotVerify);
        using RegistryKey registry = Registry.CurrentUser.CreateSubKey(InstalledLayout.UninstallRegistryPath);
        string receipts = Path.Combine(Path.GetDirectoryName(StudioStateRepository.CurrentUserPath())!, "managed-shortcuts");
        return new UninstallCoordinator(programs, menu, new InstallRegistration(registry), receipts,
            () => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify))
            .Run(expectedOwner: owner, resumeOnly: resumeOnly);
    }

    internal UninstallResult Run(Action<RemoveStep>? checkpoint = null, Guid? expectedOwner = null, bool resumeOnly = false)
    {
        string rootPath = Path.Combine(programsPath, InstalledLayout.ProductDirectory);
        using var gate = new InstallGate(rootPath);
        if (!gate.Entered) return new(true, 0, new(0, 0, false));
        using var programs = WindowsFileScope.Open(programsPath);
        RemoveIntent? intent = InstallJournal.Read<RemoveIntent>(programs, InstallJournal.RemoveName);
        using var root = OpenOptional(programs, InstalledLayout.ProductDirectory);
        if (resumeOnly && intent is null) throw new InvalidDataException("There is no matching pending removal.");
        if (intent is null && root is null) return new(false, 0, new(0, 0, false));

        if (intent is null)
        {
            using var dispatch = InstallGate.ForEntry(rootPath);
            if (!dispatch.Entered) return new(true, 0, new(0, 0, false));
            InstallOwner owner = InstallCoordinator.ReadOwnerRecord(root!);
            if (expectedOwner is Guid requested && requested != owner.Id)
                throw new InvalidDataException("The requested installation has changed.");
            if (InstallJournal.Read<UpgradeIntent>(root!, InstallJournal.UpgradeName) is not null ||
                InstallCoordinator.ReadSelector(root!, InstallStage.Next) is not null)
                throw new InvalidDataException("Finish the interrupted installation before removing it.");
            InstalledSelection active = InstallCoordinator.ReadSelector(root!, InstalledEntry.Active) ??
                throw new InvalidDataException("The installed version is unknown.");
            InstalledSelection? previous = InstallCoordinator.ReadSelector(root!, InstalledEntry.Previous);
            CheckRetainedPrograms(root!, owner);
            registration.VerifyRemovable(owner, rootPath, active.Version);
            intent = new(1, owner, active, previous);
            InstallJournal.Write(programs, InstallJournal.RemoveName, intent);
            checkpoint?.Invoke(RemoveStep.Requested);
        }
        else
        {
            if (intent.Schema != 1 || intent.Owner is not { Schema: 1 } ||
                intent.Active?.Version is null)
                throw new InvalidDataException("The uninstall intent is invalid.");
        }

        InstallOwner installedOwner = intent.Owner;
        if (installedOwner.Id == Guid.Empty || expectedOwner is Guid wanted && wanted != installedOwner.Id ||
            installedOwner.EntryHash is null || !InstalledSelection.IsDigest(installedOwner.EntryHash) ||
            root is not null && root.Identity != (installedOwner.Volume, installedOwner.Index))
            throw new InvalidDataException("The uninstall intent does not match the program root.");

        int preserved = 0;
        if (root is not null)
        {
            CheckRetainedPrograms(root, installedOwner);
            InstallOwner? local = InstallJournal.Read<InstallOwner>(root, InstallJournal.OwnerName);
            if (local is not null && local != installedOwner) throw new InvalidDataException("The installation owner changed.");
            preserved += RemoveGenerations(root, installedOwner);
            if (!RemoveLauncher(root, installedOwner)) preserved++;
        }
        checkpoint?.Invoke(RemoveStep.ProgramFiles);
        registration.Remove(installedOwner, rootPath, intent.Active.Version);
        checkpoint?.Invoke(RemoveStep.Registration);

        var shortcutResult = new ManagedShortcutCleanup(receipts, InstalledLayout.LauncherPath(rootPath), desktop).Run();
        checkpoint?.Invoke(RemoveStep.Shortcuts);
        if (root is not null)
        {
            preserved += FinishRoot(root, intent);
            int leftovers = root.ListNames(4096).Count;
            preserved = Math.Max(preserved, leftovers);
            var rootId = root.Identity;
            root.Dispose();
            if (leftovers == 0 && !programs.RemoveEmptyDirectory(InstalledLayout.ProductDirectory, rootId)) preserved++;
        }
        InstallJournal.Remove(programs, InstallJournal.RemoveName, intent);
        return new(false, preserved, shortcutResult);
    }

    private int FinishRoot(WindowsFileScope root, RemoveIntent intent)
    {
        int preserved = 0;
        try
        {
            InstallMenuIntent? menu = InstallJournal.Read<InstallMenuIntent>(root, InstallJournal.MenuName);
            if (menu is not null)
            {
                if (RemoveMenu(root, intent.Owner, menu))
                    InstallJournal.Remove(root, InstallJournal.MenuName, menu);
                else preserved++;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        { preserved++; }
        if (!RemoveSelector(root, InstalledEntry.Active, intent.Active)) preserved++;
        if (intent.Previous is not null && !RemoveSelector(root, InstalledEntry.Previous, intent.Previous)) preserved++;
        if (!InstallJournal.RemoveFile(root, InstalledLayout.StudioName, intent.Owner.EntryHash, 128 * 1024 * 1024)) preserved++;
        if (InstallJournal.Read<InstallOwner>(root, InstallJournal.OwnerName) is not null)
            InstallJournal.Remove(root, InstallJournal.OwnerName, intent.Owner);
        return preserved;
    }

    private static void CheckRetainedPrograms(WindowsFileScope root, InstallOwner owner)
    {
        using var versions = OpenOptional(root, InstalledLayout.Versions);
        if (versions is null) return;
        const string suffix = ".receipt.json";
        foreach (string name in versions.ListNames(512))
        {
            if (!name.EndsWith(suffix, StringComparison.Ordinal) ||
                InstalledSelection.ParseVersion(name[..^suffix.Length]) is not Version version) continue;
            InstallGeneration? proof;
            try { proof = InstallJournal.Read<InstallGeneration>(versions, name); }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { continue; }
            if (proof is not { Schema: 1 } || proof.Id != owner.Id) continue;
            using var generation = OpenOptional(versions, version.ToString(3));
            if (generation?.Identity == (proof.Volume, proof.Index))
                InstallUpgrade.CheckProgramLocks(versions, version, allowMissing: true);
        }
    }

    private bool RemoveMenu(WindowsFileScope root, InstallOwner owner, InstallMenuIntent receipt)
    {
        if (receipt.Schema != 1 || receipt.Id != owner.Id || receipt.Proof is null ||
            !string.Equals(receipt.Proof.Path, Path.Combine(menuPath, InstalledLayout.StartMenuName), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.Proof.LauncherPath, Path.Combine(root.Path, InstalledLayout.StudioName), StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            using var scope = WindowsShortcutPlatform.Open(menuPath);
            using var link = scope.Inspect(InstalledLayout.StartMenuName, receipt.Proof, forDelete: true);
            if (link.State == ShortcutFileState.Missing) return true;
            if (link.State != ShortcutFileState.Owned) return false;
            link.Delete();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidDataException) { return false; }
    }

    private static int RemoveGenerations(WindowsFileScope root, InstallOwner owner)
    {
        using var versions = OpenOptional(root, InstalledLayout.Versions);
        if (versions is null) return 0;
        const string suffix = ".receipt.json";
        int preserved = 0;
        foreach (string receipt in versions.ListNames(512))
        {
            if (!receipt.EndsWith(suffix, StringComparison.Ordinal) ||
                InstalledSelection.ParseVersion(receipt[..^suffix.Length]) is not Version version) continue;
            try
            {
                InstallGeneration? proof = InstallJournal.Read<InstallGeneration>(versions, receipt);
                if (proof is not { Schema: 1, InventoryHash: not null, Directories: not null } || proof.Id != owner.Id ||
                    !InstalledSelection.IsDigest(proof.InventoryHash))
                {
                    preserved++;
                    continue;
                }
                string name = version.ToString(3);
                if (RemoveGeneration(versions, name, proof)) InstallJournal.Remove(versions, receipt, proof);
                else preserved++;
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            { preserved++; }
        }
        var id = versions.Identity;
        versions.Dispose();
        if (!root.RemoveEmptyDirectory(InstalledLayout.Versions, id)) preserved++;
        return preserved;
    }

    private static bool RemoveGeneration(WindowsFileScope versions, string name, InstallGeneration proof)
    {
        WindowsFileScope? published = OpenOptional(versions, name);
        string? staged = published is null ? proof.StageName : null;
        if (staged is not null && !InstallStage.IsStageName(staged)) return false;
        string actualName = staged ?? name;
        using var generation = published ?? (staged is null ? null : OpenOptional(versions, staged));
        if (generation is null) return true;
        if (generation.Identity != (proof.Volume, proof.Index)) return false;
        if (generation.ListNames(4096).Count != 0)
        {
            InstallPayload payload;
            using (var inventory = generation.OpenChild(InstallStage.Inventory, WindowsFileScope.ReadAccess))
            {
                if (!InstallJournal.Matches(inventory, proof.InventoryHash, 512 * 1024)) return false;
                using var stream = new FileStream(inventory, FileAccess.Read);
                stream.Position = 0;
                payload = InstallPayload.Read(stream);
            }
            if (payload.Version.ToString(3) != name || payload.RemoveFiles(generation, proof.Directories) != 0 ||
                generation.ListNames(4096).Any(file => file != InstallStage.Inventory)) return false;
            if (!InstallJournal.RemoveFile(generation, InstallStage.Inventory, proof.InventoryHash, 512 * 1024)) return false;
        }
        var id = generation.Identity;
        generation.Dispose();
        return versions.RemoveEmptyDirectory(actualName, id);
    }

    private static bool RemoveLauncher(WindowsFileScope root, InstallOwner owner)
    {
        try
        {
            using var launcher = OpenOptional(root, "Launcher");
            if (launcher is null) return true;
            if (launcher.Identity != (owner.LauncherVolume, owner.LauncherIndex)) return false;
            if (!InstallJournal.RemoveFile(launcher, InstalledLayout.LauncherName, owner.EntryHash, 128 * 1024 * 1024)) return false;
            var id = launcher.Identity;
            launcher.Dispose();
            return root.RemoveEmptyDirectory("Launcher", id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidDataException) { return false; }
    }

    private static bool RemoveSelector(WindowsFileScope root, string name, InstalledSelection expected)
    {
        byte[] bytes = expected.Encode();
        return InstallJournal.RemoveFile(root, name, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
    }

    private static WindowsFileScope? OpenOptional(WindowsFileScope scope, string name)
    {
        try { return scope.OpenDirectory(name, WindowsFileScope.OpenExisting); }
        catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex)) { return null; }
    }
}
