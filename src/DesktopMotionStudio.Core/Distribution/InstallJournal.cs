using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopMotionStudio.Core.IO;
using DesktopMotionStudio.Core.Shortcuts;
using Microsoft.Win32.SafeHandles;

namespace DesktopMotionStudio.Core.Distribution;

internal sealed record InstallOwner([property: JsonRequired] int Schema, [property: JsonRequired] Guid Id,
    [property: JsonRequired] string EntryHash, [property: JsonRequired] uint Volume, [property: JsonRequired] ulong Index,
    [property: JsonRequired] uint LauncherVolume, [property: JsonRequired] ulong LauncherIndex);

internal sealed record InstallMenuIntent([property: JsonRequired] int Schema, [property: JsonRequired] Guid Id,
    [property: JsonRequired] string StageName, [property: JsonRequired] ShortcutProof Proof);

internal sealed record UpgradeIntent([property: JsonRequired] int Schema, [property: JsonRequired] Guid Id,
    [property: JsonRequired] InstalledSelection From, [property: JsonRequired] InstalledSelection To,
    InstalledSelection? Obsolete,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EntryUpgradeIntent? Entry = null);

internal sealed record EntryUpgradeIntent([property: JsonRequired] string FromHash, [property: JsonRequired] string ToHash);

internal sealed record InstallGeneration([property: JsonRequired] int Schema, [property: JsonRequired] Guid Id,
    [property: JsonRequired] string InventoryHash, [property: JsonRequired] uint Volume, [property: JsonRequired] ulong Index,
    [property: JsonRequired] InstallDirectory[] Directories,
    string? StageName = null);

internal sealed record InstallDirectory([property: JsonRequired] string Path, [property: JsonRequired] uint Volume,
    [property: JsonRequired] ulong Index);

internal sealed record RemoveIntent([property: JsonRequired] int Schema, [property: JsonRequired] InstallOwner Owner,
    [property: JsonRequired] InstalledSelection Active, InstalledSelection? Previous);

internal static class InstallJournal
{
    internal const string OwnerName = "install.owner.json";
    internal const string MenuName = "install.menu.json";
    internal const string UpgradeName = "install.upgrade.json";
    internal const string RemoveName = "DesktopMotionStudio.remove.json";
    private const int MaxBytes = 512 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    internal static T? Read<T>(WindowsFileScope root, string name) where T : class
    {
        try
        {
            using var file = root.OpenChild(name, WindowsFileScope.ReadAccess);
            var info = WindowsFileScope.ReadInfo(file);
            if (!info.IsSingleRegularFile || info.Length is 0 or > MaxBytes)
                throw new InvalidDataException("Invalid installer journal file.");
            using var stream = new FileStream(file, FileAccess.Read);
            byte[] bytes = new byte[(int)info.Length];
            stream.ReadExactly(bytes);
            return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Invalid installer journal.");
        }
        catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex)) { return null; }
    }

    internal static void Write<T>(WindowsFileScope root, string name, T value)
    {
        byte[] data = Encode(value);
        if (data.Length > MaxBytes) throw new InvalidDataException("Installer journal exceeded its limit.");
        WriteFile(root, name, data);
    }

    internal static void Remove<T>(WindowsFileScope root, string name, T expected)
    {
        byte[] data = Encode(expected);
        using var file = root.OpenChild(name, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess);
        if (!Matches(file, Convert.ToHexString(SHA256.HashData(data)), MaxBytes))
            throw new InvalidDataException("The installer journal changed.");
        WindowsFileScope.Delete(file);
    }

    internal static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);

    internal static bool RemoveFile(WindowsFileScope root, string name, string expected, long maxBytes)
    {
        try
        {
            using var file = root.OpenChild(name, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess);
            if (!Matches(file, expected, maxBytes)) return false;
            WindowsFileScope.Delete(file);
            return true;
        }
        catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex)) { return true; }
        catch (Exception ex) when (ex is Win32Exception or InvalidDataException) { return false; }
    }

    internal static void WriteFile(WindowsFileScope root, string name, ReadOnlySpan<byte> data)
    {
        using var stage = root.OpenChild($".write-{Guid.NewGuid():N}", WindowsFileScope.ReadAccess |
            WindowsFileScope.WriteAccess | WindowsFileScope.DeleteAccess, disposition: WindowsFileScope.CreateNew);
        using var output = new FileStream(stage, FileAccess.ReadWrite);
        try
        {
            output.Write(data);
            output.Flush(flushToDisk: true);
            root.Publish(stage, name);
        }
        catch
        {
            try { WindowsFileScope.Delete(stage); }
            catch (Win32Exception) { }
            throw;
        }
    }

    internal static bool Matches(SafeFileHandle file, string digest, long maximumBytes)
    {
        var info = WindowsFileScope.ReadInfo(file);
        if (!info.IsSingleRegularFile || info.Length > (ulong)maximumBytes) return false;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long offset = 0;
        while (offset < (long)info.Length)
        {
            int size = (int)Math.Min(buffer.Length, (long)info.Length - offset);
            int read = RandomAccess.Read(file, buffer.AsSpan(0, size), offset);
            if (read == 0) return false;
            hash.AppendData(buffer, 0, read);
            offset += read;
        }
        return Convert.ToHexString(hash.GetHashAndReset()) == digest;
    }
}
