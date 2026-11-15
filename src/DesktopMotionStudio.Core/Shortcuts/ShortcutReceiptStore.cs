using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;
using DesktopMotionStudio.Core.IO;

namespace DesktopMotionStudio.Core.Shortcuts;

internal enum ShortcutReceiptStatus { Ok, Missing, Invalid, Unsupported, Busy, Unavailable, IoError }

internal sealed record ShortcutProof(
    [property: JsonRequired] string Path,
    [property: JsonRequired] string LauncherPath,
    [property: JsonRequired] uint VolumeSerial,
    [property: JsonRequired] ulong FileIndex,
    [property: JsonRequired] string Sha256);

internal sealed record ShortcutReceipt(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string BindingId,
    ShortcutProof? Current = null,
    ShortcutProof? Pending = null);

internal sealed record ShortcutReceiptRead(ShortcutReceiptStatus Status, ShortcutReceipt? Receipt = null);

internal sealed class ShortcutReceiptStore(string directory)
{
    private const int MaximumBytes = 16 * 1024;
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 8
    };

    internal ShortcutReceiptRead Read(string bindingId)
    {
        if (ManagedLaunchRequest.EncodeBindingId(bindingId) is null) return new(ShortcutReceiptStatus.Invalid);
        try
        {
            using var scope = WindowsFileScope.Open(Path.Combine(directory, "records"));
            return ReadRecoverable(scope, Key(bindingId) + ".json", bindingId);
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { return new(Map(ex)); }
    }

    internal ShortcutReceiptStatus Begin(string bindingId, out Transaction? transaction, bool wait = true)
    {
        transaction = null;
        if (ManagedLaunchRequest.EncodeBindingId(bindingId) is null) return ShortcutReceiptStatus.Invalid;
        WindowsFileScope? locks = null;
        WindowsFileScope? records = null;
        SafeFileHandle? handle = null;
        try
        {
            locks = WindowsFileScope.Open(Path.Combine(directory, "locks"), create: true);
            string key = Key(bindingId);
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    handle = locks.OpenChild(key + ".lock", WindowsFileScope.ReadAccess | WindowsFileScope.WriteAccess,
                        share: 0, disposition: WindowsFileScope.OpenIf);
                    break;
                }
                catch (Win32Exception ex) when (WindowsFileScope.IsSharing(ex))
                {
                    if (!wait) return ShortcutReceiptStatus.Busy;
                    TimeSpan remaining = LockWait - elapsed.Elapsed;
                    if (remaining <= TimeSpan.Zero) return ShortcutReceiptStatus.Busy;
                    Thread.Sleep(remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25));
                }
            }
            if (!WindowsFileScope.ReadInfo(handle).IsSingleRegularFile) return ShortcutReceiptStatus.Invalid;
            records = WindowsFileScope.Open(Path.Combine(directory, "records"), create: true);
            transaction = new(records, locks, handle, key + ".json", bindingId);
            records = null;
            locks = null;
            handle = null;
            return ShortcutReceiptStatus.Ok;
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { return Map(ex); }
        finally { handle?.Dispose(); records?.Dispose(); locks?.Dispose(); }
    }

    internal IReadOnlyList<ShortcutReceiptRead> Discover()
    {
        try
        {
            using var scope = WindowsFileScope.Open(Path.Combine(directory, "records"));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in scope.ListNames(4096))
            {
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".previous", StringComparison.OrdinalIgnoreCase))
                {
                    string key = Path.GetFileNameWithoutExtension(name);
                    if (key.Length == 64 && key.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F')) keys.Add(key);
                }
            }
            return keys.Select(key => ReadRecoverable(scope, key + ".json", id: null, key)).ToArray();
        }
        catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex)) { return []; }
    }

    internal sealed class Transaction(WindowsFileScope scope, WindowsFileScope locks,
        SafeFileHandle gate, string name, string bindingId) : IDisposable
    {
        internal ShortcutReceiptRead Read()
        {
            ObjectDisposedException.ThrowIf(gate.IsClosed, this);
            return ReadRecoverable(scope, name, bindingId);
        }

        internal ShortcutReceiptStatus Save(ShortcutReceipt receipt)
        {
            ObjectDisposedException.ThrowIf(gate.IsClosed, this);
            if (!Valid(receipt, bindingId)) return ShortcutReceiptStatus.Invalid;
            ShortcutReceiptRead existing = Read();
            if (existing.Status is not (ShortcutReceiptStatus.Ok or ShortcutReceiptStatus.Missing)) return existing.Status;
            if (existing.Receipt == receipt) return ShortcutReceiptStatus.Ok;

            try
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, Json);
                if (bytes.Length > MaximumBytes) return ShortcutReceiptStatus.Invalid;
                using SafeFileHandle stage = scope.OpenChild($"{Guid.NewGuid():N}.tmp",
                    WindowsFileScope.ReadAccess | WindowsFileScope.WriteAccess | WindowsFileScope.DeleteAccess,
                    disposition: WindowsFileScope.CreateNew);
                using var stream = new FileStream(stage, FileAccess.ReadWrite);
                bool published = false;
                try
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                    // Preserve a verified previous generation before publishing the new one.
                    // All renames refuse replacement, including races with outside editors.
                    using var primary = OpenVerified(scope, name, bindingId, out ShortcutReceiptRead latest);
                    if (latest.Status is not (ShortcutReceiptStatus.Ok or ShortcutReceiptStatus.Missing)) return latest.Status;
                    using var previous = OpenVerified(scope, Previous(name), bindingId, out ShortcutReceiptRead backup);
                    if (backup.Status is not (ShortcutReceiptStatus.Ok or ShortcutReceiptStatus.Missing)) return backup.Status;
                    if ((latest.Status == ShortcutReceiptStatus.Ok ? latest : backup) != existing) return ShortcutReceiptStatus.Busy;
                    if (primary is not null)
                    {
                        if (previous is not null)
                        {
                            WindowsFileScope.Delete(previous);
                            previous.Dispose();
                        }
                        scope.Publish(primary, Previous(name));
                        primary.Dispose();
                    }
                    scope.Publish(stage, name);
                    published = true;
                    return ShortcutReceiptStatus.Ok;
                }
                finally
                {
                    if (!published)
                        try { WindowsFileScope.Delete(stage); }
                        catch (Win32Exception) { }
                }
            }
            catch (Exception ex) when (IsStorageFailure(ex)) { return Map(ex); }
        }

        internal ShortcutReceiptStatus Clear()
        {
            ObjectDisposedException.ThrowIf(gate.IsClosed, this);
            try
            {
                using var current = OpenVerified(scope, name, bindingId, out ShortcutReceiptRead primary);
                using var previous = OpenVerified(scope, Previous(name), bindingId, out ShortcutReceiptRead backup);
                if (primary.Status is not (ShortcutReceiptStatus.Ok or ShortcutReceiptStatus.Missing)) return primary.Status;
                if (backup.Status is not (ShortcutReceiptStatus.Ok or ShortcutReceiptStatus.Missing)) return backup.Status;
                if (previous is not null)
                {
                    WindowsFileScope.Delete(previous);
                    previous.Dispose();
                }
                if (current is not null) WindowsFileScope.Delete(current);
                return ShortcutReceiptStatus.Ok;
            }
            catch (Win32Exception ex) when (WindowsFileScope.IsMissing(ex)) { return ShortcutReceiptStatus.Ok; }
            catch (Exception ex) when (IsStorageFailure(ex)) { return Map(ex); }
        }

        public void Dispose()
        {
            scope.Dispose();
            gate.Dispose();
            locks.Dispose();
        }
    }

    private static ShortcutReceiptRead ReadRecoverable(WindowsFileScope scope, string name, string? id, string? key = null)
    {
        ShortcutReceiptRead primary = Read(scope, name, id, key);
        if (primary.Status is not (ShortcutReceiptStatus.Ok or ShortcutReceiptStatus.Missing)) return primary;
        ShortcutReceiptRead backup = Read(scope, Previous(name), id, key);
        if (backup.Status is not (ShortcutReceiptStatus.Ok or ShortcutReceiptStatus.Missing)) return backup;
        return primary.Status == ShortcutReceiptStatus.Ok ? primary : backup;
    }

    private static SafeFileHandle? OpenVerified(WindowsFileScope scope, string name, string id,
        out ShortcutReceiptRead read)
    {
        SafeFileHandle? handle = null;
        try
        {
            handle = scope.OpenChild(name, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess);
            read = Decode(handle, id);
            if (read.Status != ShortcutReceiptStatus.Ok) return null;
            SafeFileHandle verified = handle;
            handle = null;
            return verified;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            read = new(Map(ex));
            return null;
        }
        finally { handle?.Dispose(); }
    }

    private static string Previous(string name) => Path.ChangeExtension(name, ".previous");

    private static ShortcutReceiptRead Read(WindowsFileScope scope, string name, string? id, string? key)
    {
        try
        {
            using var handle = scope.OpenChild(name, WindowsFileScope.ReadAccess);
            return Decode(handle, id, key);
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { return new(Map(ex)); }
    }

    private static ShortcutReceiptRead Decode(SafeFileHandle handle, string? id, string? key = null)
    {
        var info = WindowsFileScope.ReadInfo(handle);
        if (!info.IsSingleRegularFile || info.Length is 0 or > MaximumBytes) return new(ShortcutReceiptStatus.Invalid);
        byte[] bytes = new byte[(int)info.Length];
        int read = 0;
        while (read < bytes.Length)
        {
            int count = RandomAccess.Read(handle, bytes.AsSpan(read), read);
            if (count == 0) return new(ShortcutReceiptStatus.Invalid);
            read += count;
        }
        using JsonDocument parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8, AllowDuplicateProperties = false });
        if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
            !parsed.RootElement.TryGetProperty("schemaVersion", out JsonElement version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int schema))
            return new(ShortcutReceiptStatus.Invalid);
        if (schema > 1) return new(ShortcutReceiptStatus.Unsupported);
        ShortcutReceipt? receipt = JsonSerializer.Deserialize<ShortcutReceipt>(bytes, Json);
        return receipt is not null && Valid(receipt, id ?? receipt.BindingId) &&
               (key is null || Key(receipt.BindingId) == key)
            ? new(ShortcutReceiptStatus.Ok, receipt) : new(ShortcutReceiptStatus.Invalid);
    }

    private static bool Valid(ShortcutReceipt receipt, string id) =>
        receipt.SchemaVersion == 1 && ManagedLaunchRequest.EncodeBindingId(receipt.BindingId) is not null &&
        string.Equals(receipt.BindingId, id, StringComparison.OrdinalIgnoreCase) &&
        (receipt.Current is not null || receipt.Pending is not null) &&
        (receipt.Current is null || Valid(receipt.Current)) && (receipt.Pending is null || Valid(receipt.Pending)) &&
        (receipt.Current is null || receipt.Pending is null ||
         string.Equals(receipt.Current.Path, receipt.Pending.Path, StringComparison.OrdinalIgnoreCase));

    private static bool Valid(ShortcutProof proof) =>
        WindowsFileScope.IsCanonicalLocalPath(proof.Path) && proof.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
        WindowsFileScope.IsCanonicalLocalPath(proof.LauncherPath) && proof.LauncherPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        proof.FileIndex != 0 &&
        proof.Sha256 is { Length: 64 } && proof.Sha256.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');

    internal static string Key(string id)
    {
        var normalized = new StringBuilder(id.Length);
        foreach (Rune rune in id.EnumerateRunes())
        {
            string text = rune.ToString();
            string upper = Rune.ToUpperInvariant(rune).ToString();
            normalized.Append(string.Equals(text, upper, StringComparison.OrdinalIgnoreCase) ? upper : text);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToString())));
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or Win32Exception or InvalidDataException;

    private static ShortcutReceiptStatus Map(Exception ex) => ex switch
    {
        Win32Exception native when WindowsFileScope.IsMissing(native) => ShortcutReceiptStatus.Missing,
        Win32Exception native when WindowsFileScope.IsSharing(native) => ShortcutReceiptStatus.Busy,
        InvalidDataException => ShortcutReceiptStatus.Unavailable,
        JsonException => ShortcutReceiptStatus.Invalid,
        _ => ShortcutReceiptStatus.IoError
    };
}
