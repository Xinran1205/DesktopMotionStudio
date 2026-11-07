using System.ComponentModel;
using System.Runtime.Versioning;
using DesktopMotionStudio.Core.IO;
using DesktopMotionStudio.Core.Shortcuts;

namespace DesktopMotionStudio.Core.Distribution;

internal enum InstallOutcome { Installed, AlreadyInstalled, Upgraded, Busy, IncompatibleVersion, RemovalPending }
internal enum InstallStep
{
    DirectoryPublished, MenuPrepared, MenuPublished, RegistrationClaimed, Registered,
    UpgradeClaimed, UpgradePrepared, UpgradeRetired, UpgradeActivated,
    EntryStudioRetired, EntryLauncherRetired, EntryOwnerRetired
}

[SupportedOSPlatform("windows")]
internal sealed class InstallCoordinator(string programsPath, string menuPath, InstallRegistration registration)
{
    internal InstallOutcome Install(Stream manifest, Stream archive, Stream entry, Action<InstallStep>? checkpoint = null)
    {
        InstallPayload payload = InstallPayload.Read(manifest);
        string rootPath = Path.Combine(programsPath, InstalledLayout.ProductDirectory);
        using var gate = new InstallGate(rootPath);
        if (!gate.Entered) return InstallOutcome.Busy;

        using var programs = WindowsFileScope.Open(programsPath, create: true);
        if (InstallJournal.Read<RemoveIntent>(programs, InstallJournal.RemoveName) is not null) return InstallOutcome.RemovalPending;
        using var menu = WindowsShortcutPlatform.Open(menuPath);
        using (var root = TryOpenRoot(programs))
        {
            if (root is not null)
            {
                return Complete(root, menu, payload, archive, entry, checkpoint);
            }
        }
        using (var menuSlot = menu.Inspect(InstalledLayout.StartMenuName))
        {
            if (menuSlot.State != ShortcutFileState.Missing || registration.Exists())
                throw new InvalidDataException("An existing Windows registration or Start menu entry occupies the installation slot.");
        }

        using (InstallStage stage = InstallStage.Prepare(programs, payload, archive, entry)) stage.Publish();
        checkpoint?.Invoke(InstallStep.DirectoryPublished);
        using var installed = programs.OpenDirectory(InstalledLayout.ProductDirectory, WindowsFileScope.OpenExisting);
        return Complete(installed, menu, payload, archive, entry, checkpoint);
    }

    private InstallOutcome Complete(WindowsFileScope root, WindowsShortcutPlatform menu, InstallPayload payload, Stream archive, Stream entry,
        Action<InstallStep>? checkpoint)
    {
        InstalledSelection? active = ReadSelector(root, InstalledEntry.Active);
        InstalledSelection? pending = ReadSelector(root, InstallStage.Next);
        UpgradeIntent? upgrade = InstallJournal.Read<UpgradeIntent>(root, InstallJournal.UpgradeName);
        bool recoveringEntry = upgrade?.Entry is not null;
        InstallOwner owner = ReadOwnerRecord(root, allowRetired: recoveringEntry);
        using var launcher = OpenOwnedLauncher(root, owner);
        if (!recoveringEntry) VerifyEntries(root, launcher, owner);
        if (upgrade is not null || active is not null && active.Version < payload.Version)
        {
            if (upgrade is not null && upgrade.To?.Version != payload.Version) return InstallOutcome.IncompatibleVersion;
            EnsureMenu(root, menu, owner, mayCreate: false);
            return new InstallUpgrade(root, registration, owner).Run(payload, archive, entry, active, pending, upgrade, checkpoint);
        }
        InstalledSelection selected = active ?? pending ?? throw new InvalidDataException("The installation has no recoverable selector.");
        if (selected.Version != payload.Version) return InstallOutcome.IncompatibleVersion;
        using (var versions = root.OpenDirectory(InstalledLayout.Versions, WindowsFileScope.OpenExisting))
        using (var generation = versions.OpenDirectory(selected.Version.ToString(3), WindowsFileScope.OpenExisting))
        {
            InstallStage.VerifyGeneration(versions, generation, payload, owner);
            payload.VerifyStaged(generation, selected);
        }

        EnsureMenu(root, menu, owner, mayCreate: active is null, checkpoint);
        registration.Ensure(owner, root.Path, selected.Version, () => checkpoint?.Invoke(InstallStep.RegistrationClaimed));
        checkpoint?.Invoke(InstallStep.Registered);
        if (active is not null)
        {
            new InstallUpgrade(root, registration, owner).PruneRetired(active.Version);
            return InstallOutcome.AlreadyInstalled;
        }
        using (var versions = root.OpenDirectory(InstalledLayout.Versions, WindowsFileScope.OpenExisting))
        using (var generation = versions.OpenDirectory(selected.Version.ToString(3), WindowsFileScope.OpenExisting))
        {
            InstallStage.VerifyGeneration(versions, generation, payload, owner);
            payload.VerifyStaged(generation, selected);
        }
        InstallStage.Activate(root, selected);
        return InstallOutcome.Installed;
    }

    private static void EnsureMenu(WindowsFileScope root, WindowsShortcutPlatform menu, InstallOwner owner,
        bool mayCreate, Action<InstallStep>? checkpoint = null)
    {
        string studio = Path.Combine(root.Path, InstalledLayout.StudioName);
        InstallMenuIntent? link = InstallJournal.Read<InstallMenuIntent>(root, InstallJournal.MenuName);
        if (link is null)
        {
            if (!mayCreate) throw new InvalidDataException("The installed Start menu receipt is missing.");
            using var menuStage = menu.StageDirect(InstalledLayout.StartMenuName, studio);
            link = new(1, owner.Id, menuStage.TemporaryName, menuStage.Proof);
            InstallJournal.Write(root, InstallJournal.MenuName, link);
            menuStage.RetainForRecovery();
            checkpoint?.Invoke(InstallStep.MenuPrepared);
            menuStage.Publish();
        }
        else
        {
            if (link.Schema != 1 || link.Id != owner.Id || link.StageName is null || link.Proof is null ||
                !string.Equals(link.Proof.Path, Path.Combine(menu.Root, InstalledLayout.StartMenuName), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(link.Proof.LauncherPath, studio, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The Start menu receipt does not match the installation.");
            menu.CompleteDirectPublication(InstalledLayout.StartMenuName, link.StageName, link.Proof, allowMissing: !mayCreate);
        }
        checkpoint?.Invoke(InstallStep.MenuPublished);
    }

    internal static InstallOwner ReadOwner(WindowsFileScope root)
    {
        InstallOwner owner = ReadOwnerRecord(root);
        using var launcher = OpenOwnedLauncher(root, owner);
        VerifyEntries(root, launcher, owner);
        return owner;
    }

    internal static WindowsFileScope OpenOwnedLauncher(WindowsFileScope root, InstallOwner owner)
    {
        WindowsFileScope launcher = root.OpenDirectory("Launcher", WindowsFileScope.OpenExisting);
        try
        {
            if (launcher.Identity != (owner.LauncherVolume, owner.LauncherIndex))
                throw new InvalidDataException("The stable Launcher directory changed.");
            return launcher;
        }
        catch { launcher.Dispose(); throw; }
    }

    private static void VerifyEntries(WindowsFileScope root, WindowsFileScope launcherDir, InstallOwner owner)
    {
        using var studio = root.OpenChild(InstalledLayout.StudioName, WindowsFileScope.ReadAccess);
        using var launcher = launcherDir.OpenChild(InstalledLayout.LauncherName, WindowsFileScope.ReadAccess);
        if (!InstallJournal.Matches(studio, owner.EntryHash, InstallStage.MaxEntryBytes) ||
            !InstallJournal.Matches(launcher, owner.EntryHash, InstallStage.MaxEntryBytes))
            throw new InvalidDataException("The stable program entries changed.");
    }

    internal static InstallOwner ReadOwnerRecord(WindowsFileScope root, bool allowRetired = false)
    {
        InstallOwner? owner = InstallJournal.Read<InstallOwner>(root, InstallJournal.OwnerName);
        if (owner is null && allowRetired)
            owner = InstallJournal.Read<InstallOwner>(root, InstallEntryUpgrade.RetiredOwnerName);
        if (owner is not { Schema: 1, EntryHash: not null } || owner.Id == Guid.Empty || !InstalledSelection.IsDigest(owner.EntryHash) ||
            root.Identity != (owner.Volume, owner.Index))
            throw new InvalidDataException("The program root is not a recognized installation.");
        return owner;
    }

    internal static InstalledSelection? ReadSelector(WindowsFileScope root, string name)
    {
        try
        {
            using var file = root.OpenChild(name, WindowsFileScope.ReadAccess);
            return InstalledSelection.Read(file);
        }
        catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex)) { return null; }
    }

    private static WindowsFileScope? TryOpenRoot(WindowsFileScope programs)
    {
        try { return programs.OpenDirectory(InstalledLayout.ProductDirectory, WindowsFileScope.OpenExisting); }
        catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex)) { return null; }
    }
}
