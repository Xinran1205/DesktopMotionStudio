using System.ComponentModel;
using System.Runtime.Versioning;
using DesktopMotionStudio.Core.IO;
using Microsoft.Win32.SafeHandles;
using static DesktopMotionStudio.Core.Distribution.InstallCoordinator;

namespace DesktopMotionStudio.Core.Distribution;

[SupportedOSPlatform("windows")]
internal sealed class InstallUpgrade(WindowsFileScope root, InstallRegistration registration, InstallOwner owner)
{
    internal InstallOutcome Run(InstallPayload payload, Stream archive, Stream entry, InstalledSelection? active, InstalledSelection? pending,
        UpgradeIntent? intent, Action<InstallStep>? checkpoint)
    {
        using var versions = root.OpenDirectory(InstalledLayout.Versions, WindowsFileScope.OpenExisting);
        string version = payload.Version.ToString(3);
        InstalledSelection expected = payload.Describe(versions.Child(version));
        InstalledSelection? previous = ReadSelector(root, InstalledEntry.Previous);
        if (intent is null)
        {
            if (active is null || pending is not null || active.Version >= payload.Version ||
                previous is not null && previous.Version >= active.Version)
                throw new InvalidDataException("The installation is not ready for an upgrade.");
            CheckProgramLocks(versions, active.Version);
            registration.Ensure(owner, root.Path, active.Version);
            string entryHash = InstallStage.DescribeEntry(entry);
            EntryUpgradeIntent? change = entryHash == owner.EntryHash ? null : new(owner.EntryHash, entryHash);
            PrepareGeneration(versions, payload, archive, expected, owner, checkpoint);
            intent = new(1, owner.Id, active, expected, previous, change);
            InstallJournal.Write(root, InstallJournal.UpgradeName, intent);
        }
        else
        {
            if (intent.Schema != 1 || intent.Id != owner.Id || intent.From?.Version is null || intent.To != expected ||
                intent.From.Version >= expected.Version || intent.Obsolete is not null && intent.Obsolete.Version >= intent.From.Version)
                throw new InvalidDataException("The upgrade journal does not match the installation.");
            if (intent.Entry is { } change &&
                (change.FromHash is null || change.ToHash is null || !InstalledSelection.IsDigest(change.FromHash) ||
                 !InstalledSelection.IsDigest(change.ToHash) || change.FromHash == change.ToHash ||
                 (owner.EntryHash != change.FromHash && owner.EntryHash != change.ToHash) ||
                 InstallStage.DescribeEntry(entry) != change.ToHash))
                throw new InvalidDataException("The pending entry update does not match the installation.");
            using var generation = versions.OpenDirectory(version, WindowsFileScope.OpenExisting);
            payload.VerifyStaged(generation, expected);
            InstallStage.VerifyGeneration(versions, generation, payload, owner);
        }

        if (active == expected)
        {
            if (pending is not null || previous != intent.From) throw new InvalidDataException("The committed upgrade selector changed.");
        }
        else
        {
            if (active == intent.From)
            {
                if (previous is not null && previous != intent.Obsolete)
                    throw new InvalidDataException("The previous selector changed.");
            }
            else if (active is not null || previous != intent.From)
                throw new InvalidDataException("The active selector changed during upgrade.");

            using var dispatch = InstallGate.ForEntry(root.Path);
            if (!dispatch.Entered) return InstallOutcome.Busy;
            CheckProgramLocks(versions, intent.From.Version);
            registration.Ensure(owner, root.Path, intent.From.Version);
            if (pending is null) InstallJournal.WriteFile(root, InstallStage.Next, expected.Encode());
            else if (pending != expected) throw new InvalidDataException("The pending selector changed.");
            checkpoint?.Invoke(InstallStep.UpgradePrepared);
            try
            {
                if (active is not null)
                {
                    if (previous is not null)
                    {
                        using var retired = OpenVerified(InstalledEntry.Previous, previous);
                        WindowsFileScope.Delete(retired);
                    }
                    using var current = OpenVerified(InstalledEntry.Active, intent.From);
                    root.Publish(current, InstalledEntry.Previous);
                }
                checkpoint?.Invoke(InstallStep.UpgradeRetired);
                using (var generation = versions.OpenDirectory(version, WindowsFileScope.OpenExisting))
                {
                    InstallStage.VerifyGeneration(versions, generation, payload, owner);
                    payload.VerifyStaged(generation, expected);
                }
                InstallStage.Activate(root, expected);
            }
            catch
            {
                // A normal failure restores the primary. After abrupt termination the reader uses Previous.
                TryRestore(intent.From);
                throw;
            }
            checkpoint?.Invoke(InstallStep.UpgradeActivated);
        }

        registration.Ensure(owner, root.Path, expected.Version, previousVersion: intent.From.Version);
        if (intent.Entry is { } images)
        {
            using var dispatch = InstallGate.ForEntry(root.Path);
            if (!dispatch.Entered) return InstallOutcome.Busy;
            InstallEntryUpgrade.Complete(root, owner, images, entry, checkpoint);
        }
        InstallJournal.Remove(root, InstallJournal.UpgradeName, intent);
        PruneRetired(expected.Version);
        return InstallOutcome.Upgraded;
    }

    internal void PruneRetired(Version active)
    {
        try
        {
            InstalledSelection? rollback = ReadSelector(root, InstalledEntry.Previous);
            if (rollback is null || rollback.Version >= active) return;
            using var versions = root.OpenDirectory(InstalledLayout.Versions, WindowsFileScope.OpenExisting);
            using (var previous = versions.OpenDirectory(rollback.Version.ToString(3), WindowsFileScope.OpenExisting))
            {
                var proof = InstallJournal.Read<InstallGeneration>(versions, InstallStage.GenerationReceipt(rollback.Version));
                if (proof is not { Schema: 1, InventoryHash: not null } || proof.Id != owner.Id ||
                    previous.Identity != (proof.Volume, proof.Index)) return;
                InstallPayload inventory = InstallStage.ReadInventory(previous, proof.InventoryHash);
                if (inventory.Describe(previous.Path) != rollback) return;
            }
            const string suffix = ".receipt.json";
            foreach (string receipt in versions.ListNames(512))
            {
                if (!receipt.EndsWith(suffix, StringComparison.Ordinal) ||
                    InstalledSelection.ParseVersion(receipt[..^suffix.Length]) is not Version retired ||
                    retired >= rollback.Version) continue;
                try
                {
                    var proof = InstallJournal.Read<InstallGeneration>(versions, receipt);
                    if (proof is not { Schema: 1, InventoryHash: not null, Directories: not null } || proof.Id != owner.Id ||
                        !InstalledSelection.IsDigest(proof.InventoryHash)) continue;
                    CheckProgramLocks(versions, retired, allowMissing: true);
                    if (InstallStage.RemoveGeneration(versions, retired.ToString(3), proof))
                        InstallJournal.Remove(versions, receipt, proof);
                }
                catch (Exception ex) when (IsRetirementFailure(ex)) { }
            }
        }
        catch (Exception ex) when (IsRetirementFailure(ex)) { }
    }

    private static bool IsRetirementFailure(Exception ex) => ex is
        Win32Exception or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException;

    private static void PrepareGeneration(WindowsFileScope versions, InstallPayload payload, Stream archive, InstalledSelection expected,
        InstallOwner owner, Action<InstallStep>? checkpoint)
    {
        string name = expected.Version.ToString(3);
        WindowsFileScope? existing;
        try { existing = versions.OpenDirectory(name, WindowsFileScope.OpenExisting); }
        catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex)) { existing = null; }
        using (existing)
        {
            if (existing is not null)
            {
                payload.VerifyStaged(existing, expected);
                InstallStage.VerifyGeneration(versions, existing, payload, owner);
                return;
            }
        }
        InstallGeneration? pending = InstallJournal.Read<InstallGeneration>(versions, InstallStage.GenerationReceipt(payload.Version));
        if (pending is not null)
        {
            if (pending.StageName is not string recorded || !InstallStage.IsStageName(recorded))
                throw new InvalidDataException("The prepared generation has an invalid stage.");
            using var recovered = versions.OpenMovableDirectory(recorded);
            InstallStage.VerifyGeneration(versions, recovered.Scope, payload, owner);
            payload.VerifyStaged(recovered.Scope, expected);
            recovered.Publish(name);
            return;
        }
        string stageName = $".dms-stage-{Guid.NewGuid():N}";
        using var staged = versions.StageDirectory(stageName);
        if (InstallStage.WriteGeneration(staged.Scope, payload, archive) != expected)
            throw new InvalidDataException("The staged upgrade did not match its manifest.");
        InstallStage.RecordGeneration(versions, staged.Scope, payload, owner, stageName);
        staged.RetainForRecovery();
        checkpoint?.Invoke(InstallStep.UpgradeClaimed);
        staged.Publish(name);
    }

    internal static void CheckProgramLocks(WindowsFileScope versions, Version version, bool allowMissing = false)
    {
        try
        {
            using var generation = versions.OpenDirectory(version.ToString(3), WindowsFileScope.OpenExisting);
            VerifyUnlocked(generation, null, InstalledLayout.StudioName);
            VerifyUnlocked(generation, "Launcher", InstalledLayout.LauncherName);
            VerifyUnlocked(generation, "MotionHost", "DesktopMotionStudio.MotionHost.exe");
        }
        catch (Win32Exception ex) when (allowMissing && WindowsFileScope.IsMissing(ex)) { }

        void VerifyUnlocked(WindowsFileScope scope, string? folder, string name)
        {
            try
            {
                using var directory = folder is null ? null : scope.OpenDirectory(folder, WindowsFileScope.OpenExisting);
                using var file = (directory ?? scope).OpenChild(name, WindowsFileScope.ReadAccess | WindowsFileScope.WriteAccess, share: 0);
                if (!WindowsFileScope.ReadInfo(file).IsSingleRegularFile)
                    throw new InvalidDataException("An installed program file is not a regular file.");
            }
            catch (Win32Exception ex) when (allowMissing && WindowsFileScope.IsMissing(ex)) { }
        }
    }

    private SafeFileHandle OpenVerified(string name, InstalledSelection expected)
    {
        SafeFileHandle file = root.OpenChild(name, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess);
        try
        {
            if (InstalledSelection.Read(file) != expected) throw new InvalidDataException("A selector changed before publication.");
            return file;
        }
        catch { file.Dispose(); throw; }
    }

    private void TryRestore(InstalledSelection old)
    {
        try
        {
            if (ReadSelector(root, InstalledEntry.Active) is not null) return;
            using var previous = OpenVerified(InstalledEntry.Previous, old);
            root.Publish(previous, InstalledEntry.Active);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidDataException) { }
    }
}
