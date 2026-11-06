using System.ComponentModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopMotionStudio.Core.IO;

namespace DesktopMotionStudio.Core.Distribution;

internal sealed class InstallPayload
{
    private const int MaxManifestBytes = 512 * 1024;
    private const int MaxFiles = 2000;
    private const long MaxArchiveBytes = 512 * 1024 * 1024;
    private const long MaxFileBytes = 128 * 1024 * 1024;
    private const long MaxExpandedBytes = 1024 * 1024 * 1024;
    private const string Studio = InstalledLayout.StudioName;
    private const string Launcher = "Launcher/" + InstalledLayout.LauncherName;

    private static readonly string[] RequiredFiles =
    [
        Studio, Launcher, "MotionHost/DesktopMotionStudio.MotionHost.exe",
        "hostfxr.dll", "Launcher/hostfxr.dll", "MotionHost/hostfxr.dll",
        "coreclr.dll", "Launcher/coreclr.dll", "MotionHost/coreclr.dll"
    ];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 8
    };

    private readonly Manifest _manifest;
    private readonly byte[] _manifestBytes;

    private InstallPayload(Manifest manifest, byte[] manifestBytes, Version version)
    {
        _manifest = manifest;
        _manifestBytes = manifestBytes;
        Version = version;
    }

    internal Version Version { get; }

    internal InstalledSelection Stage(WindowsFileScope generation, Stream archive)
    {
        if (!archive.CanSeek || archive.Position != 0 || archive.Length is 0 or > MaxArchiveBytes ||
            Convert.ToHexString(SHA256.HashData(archive)) != _manifest.ArchiveSha256)
            throw new InvalidDataException("The installer archive does not match its manifest.");
        archive.Position = 0;

        var byPath = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (Entry file in _manifest.Files)
        {
            if (file?.Path is not { Length: > 0 and <= 160 } path ||
                path.Split('/') is not { Length: > 0 and <= 8 } parts || parts.Any(part => !LocalPathPolicy.IsUnambiguousName(part)) ||
                string.Equals(parts[0], InstallStage.Inventory, StringComparison.OrdinalIgnoreCase) ||
                generation.Path.Length + path.Length + 1 > 259 ||
                file.Bytes is < 0 or > MaxFileBytes || file.Sha256 is null || !InstalledSelection.IsDigest(file.Sha256) ||
                !byPath.TryAdd(path, file))
                throw new InvalidDataException("Invalid or duplicate installer file.");
            bytes += file.Bytes;
        }
        if (bytes > MaxExpandedBytes || RequiredFiles.Any(name => !byPath.ContainsKey(name)))
            throw new InvalidDataException("The installer inventory is incomplete or too large.");

        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count != byPath.Count) throw new InvalidDataException("Unexpected installer archive entry count.");
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!byPath.TryGetValue(entry.FullName, out Entry? expected) || expected.Path != entry.FullName ||
                expected.Bytes != entry.Length || !entries.Add(entry.FullName))
                throw new InvalidDataException("The installer archive entry does not match its inventory.");
        }

        byte[] buffer = new byte[64 * 1024];
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            using Stream input = entry.Open();
            WriteFile(generation, entry.FullName.Split('/'), 0, input, byPath[entry.FullName], buffer);
        }
        return new(Version, byPath[Studio].Sha256, byPath[Launcher].Sha256);
    }

    internal static InstallPayload Read(Stream stream)
    {
        byte[] bytes = new byte[MaxManifestBytes + 1];
        int length = stream.ReadAtLeast(bytes, bytes.Length, false);
        if (length is 0 or > MaxManifestBytes) throw new InvalidDataException("Invalid installer manifest size.");
        Manifest? contract = JsonSerializer.Deserialize<Manifest>(bytes.AsSpan(0, length), Json);
        if (contract is not { SchemaVersion: 1, RuntimeIdentifier: "win-x64", Files.Length: > 0 and <= MaxFiles } ||
            contract.ProductVersion is null || InstalledSelection.ParseVersion(contract.ProductVersion) is not Version version ||
            contract.ArchiveSha256 is null || !InstalledSelection.IsDigest(contract.ArchiveSha256) ||
            contract.Frameworks.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Unsupported installer manifest.");
        return new(contract, bytes[..length], version);
    }

    internal ReadOnlyMemory<byte> InventoryBytes => _manifestBytes;

    private static void WriteFile(WindowsFileScope scope, string[] parts, int index, Stream input, Entry expected, byte[] buffer)
    {
        if (index < parts.Length - 1)
        {
            using var child = scope.OpenDirectory(parts[index], WindowsFileScope.OpenIf);
            WriteFile(child, parts, index + 1, input, expected, buffer);
            return;
        }

        using var handle = scope.OpenChild(parts[index],
            WindowsFileScope.ReadAccess | WindowsFileScope.WriteAccess | WindowsFileScope.DeleteAccess,
            disposition: WindowsFileScope.CreateNew);
        using var output = new FileStream(handle, FileAccess.ReadWrite);
        try
        {
            long remaining = expected.Bytes;
            while (remaining > 0)
            {
                int count = (int)Math.Min(buffer.Length, remaining);
                input.ReadExactly(buffer.AsSpan(0, count));
                output.Write(buffer.AsSpan(0, count));
                remaining -= count;
            }
            if (input.ReadByte() != -1) throw new InvalidDataException("An installer file exceeds its declared size.");
            output.Position = 0;
            if (Convert.ToHexString(SHA256.HashData(output)) != expected.Sha256)
                throw new InvalidDataException("An extracted installer file failed verification.");
            output.Flush(flushToDisk: true);
        }
        catch
        {
            // Delete only the unverified file created by this invocation.
            try { WindowsFileScope.Delete(handle); }
            catch (Win32Exception) { }
            throw;
        }
    }

    private sealed record Manifest([property: JsonRequired] int SchemaVersion, [property: JsonRequired] string ProductVersion,
        [property: JsonRequired] string RuntimeIdentifier, [property: JsonRequired] string ArchiveSha256,
        [property: JsonRequired] JsonElement Frameworks, [property: JsonRequired] Entry[] Files);

    private sealed record Entry([property: JsonRequired] string Path, [property: JsonRequired] long Bytes,
        [property: JsonRequired] string Sha256);
}
