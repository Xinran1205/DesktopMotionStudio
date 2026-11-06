using System.Security.Cryptography;
using DesktopMotionStudio.Core.IO;

namespace DesktopMotionStudio.Core.Distribution;

internal sealed class InstallStage : IDisposable
{
    internal const string Inventory = "install.inventory.json";
    internal const string Next = "active.next";
    private const long MaxEntryBytes = 128 * 1024 * 1024;
    private readonly WindowsFileScope.StagedDirectory _directory;

    private InstallStage(WindowsFileScope.StagedDirectory directory, InstalledSelection selection, string entryHash)
    {
        _directory = directory;
        Selection = selection;
        EntryHash = entryHash;
    }

    internal InstalledSelection Selection { get; }
    internal string EntryHash { get; }

    internal static InstallStage Prepare(WindowsFileScope programs, Stream manifest, Stream archive, Stream entry)
        => Prepare(programs, InstallPayload.Read(manifest), archive, entry);

    internal static InstallStage Prepare(WindowsFileScope programs, InstallPayload payload, Stream archive, Stream entry)
    {
        if (!entry.CanSeek || entry.Position != 0 || entry.Length is < 2 or > MaxEntryBytes ||
            entry.ReadByte() != 'M' || entry.ReadByte() != 'Z')
            throw new InvalidDataException("The stable entry image is unavailable.");
        entry.Position = 0;
        string entryHash = Convert.ToHexString(SHA256.HashData(entry));
        var staged = programs.StageDirectory($".dms-stage-{Guid.NewGuid():N}");
        try
        {
            WindowsFileScope scope = staged.Scope;
            var rootId = scope.Identity;
            var owner = new InstallOwner(1, Guid.NewGuid(), entryHash, rootId.Volume, rootId.Index, 0, 0);
            InstalledSelection selection;
            using (var versions = scope.OpenDirectory(InstalledLayout.Versions, WindowsFileScope.CreateNew))
            using (var generation = versions.OpenDirectory(payload.Version.ToString(3), WindowsFileScope.CreateNew))
            {
                selection = WriteGeneration(generation, payload, archive);
                RecordGeneration(versions, generation, payload, owner);
            }
            CopyEntry(scope, InstalledLayout.StudioName, entry, entryHash);
            using (var launcher = scope.OpenDirectory("Launcher", WindowsFileScope.CreateNew))
            {
                CopyEntry(launcher, InstalledLayout.LauncherName, entry, entryHash);
                var id = launcher.Identity;
                owner = owner with { LauncherVolume = id.Volume, LauncherIndex = id.Index };
            }
            using (var next = WriteNew(scope, Next))
            {
                next.Write(selection.Encode());
                next.Flush(flushToDisk: true);
            }
            InstallJournal.Write(scope, InstallJournal.OwnerName, owner);
            return new(staged, selection, entryHash);
        }
        catch
        {
            staged.Dispose();
            throw;
        }
    }

    // Publishing the complete directory does not activate it. Registration is a separate step.
    internal void Publish() => _directory.Publish(InstalledLayout.ProductDirectory);

    internal static void Activate(WindowsFileScope root, InstalledSelection expected)
    {
        using var file = root.OpenChild(Next, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess);
        if (InstalledSelection.Read(file) != expected) throw new InvalidDataException("The prepared selector changed.");
        root.Publish(file, InstalledEntry.Active);
    }

    internal static InstalledSelection WriteGeneration(WindowsFileScope generation, InstallPayload payload, Stream archive)
    {
        InstalledSelection selection = payload.Stage(generation, archive);
        using var inventory = WriteNew(generation, Inventory);
        inventory.Write(payload.InventoryBytes.Span);
        inventory.Flush(flushToDisk: true);
        return selection;
    }

    internal static string GenerationReceipt(Version version) => $"{version}.receipt.json";

    internal static bool IsStageName(string name) => name.Length == 43 &&
        name.StartsWith(".dms-stage-", StringComparison.Ordinal) && Guid.TryParseExact(name.AsSpan(11), "N", out _);

    internal static void RecordGeneration(WindowsFileScope versions, WindowsFileScope generation, InstallPayload payload,
        InstallOwner owner, string? stageName = null)
    {
        string name = GenerationReceipt(payload.Version);
        var id = generation.Identity;
        var expected = new InstallGeneration(1, owner.Id, Convert.ToHexString(SHA256.HashData(payload.InventoryBytes.Span)),
            id.Volume, id.Index, payload.VerifyLayout(generation), stageName);
        InstallJournal.Write(versions, name, expected);
    }

    internal static InstallGeneration VerifyGeneration(WindowsFileScope versions, WindowsFileScope generation,
        InstallPayload payload, InstallOwner owner)
    {
        InstallGeneration? proof = InstallJournal.Read<InstallGeneration>(versions, GenerationReceipt(payload.Version));
        if (proof is not { Schema: 1, InventoryHash: not null, Directories: not null } || proof.Id != owner.Id ||
            generation.Identity != (proof.Volume, proof.Index) ||
            Convert.ToHexString(SHA256.HashData(payload.InventoryBytes.Span)) != proof.InventoryHash ||
            !payload.VerifyLayout(generation).SequenceEqual(proof.Directories))
            throw new InvalidDataException("The generation has no matching ownership receipt.");
        return proof;
    }

    private static void CopyEntry(WindowsFileScope directory, string name, Stream source, string hash)
    {
        using var destination = WriteNew(directory, name);
        source.Position = 0;
        source.CopyTo(destination);
        destination.Position = 0;
        if (Convert.ToHexString(SHA256.HashData(destination)) != hash)
            throw new InvalidDataException("A stable entry image failed verification.");
        destination.Flush(flushToDisk: true);
    }

    private static FileStream WriteNew(WindowsFileScope scope, string name) =>
        new(scope.OpenChild(name, WindowsFileScope.ReadAccess | WindowsFileScope.WriteAccess,
            disposition: WindowsFileScope.CreateNew), FileAccess.ReadWrite);

    public void Dispose() => _directory.Dispose();
}
