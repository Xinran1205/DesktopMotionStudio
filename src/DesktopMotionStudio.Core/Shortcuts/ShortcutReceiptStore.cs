using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace DesktopMotionStudio.Core.Shortcuts;

internal enum ShortcutReceiptStatus { Ok, Missing, Invalid, Unsupported, Busy, Unavailable, IoError }

internal sealed record ShortcutProof(
    [property: JsonRequired] string Path,
    [property: JsonRequired] string LauncherPath,
    [property: JsonRequired] uint VolumeSerial,
    [property: JsonRequired] ulong FileIndex,
    [property: JsonRequired] long CreationTime,
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
            using var scope = ShortcutNativeDirectory.Open(directory);
            return Read(scope, Key(bindingId) + ".json", bindingId);
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { return new(Map(ex)); }
    }

    internal ShortcutReceiptStatus Begin(string bindingId, out Transaction? transaction)
    {
        transaction = null;
        if (ManagedLaunchRequest.EncodeBindingId(bindingId) is null) return ShortcutReceiptStatus.Invalid;
        ShortcutNativeDirectory? scope = null;
        SafeFileHandle? handle = null;
        try
        {
            scope = ShortcutNativeDirectory.Open(directory, create: true);
            string key = Key(bindingId);
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    handle = scope.OpenChild(key + ".lock", ShortcutNativeDirectory.ReadAccess | ShortcutNativeDirectory.WriteAccess,
                        share: 0, disposition: 4);
                    break;
                }
                catch (Win32Exception ex) when (ShortcutNativeDirectory.IsSharing(ex))
                {
                    TimeSpan remaining = LockWait - elapsed.Elapsed;
                    if (remaining <= TimeSpan.Zero) return ShortcutReceiptStatus.Busy;
                    Thread.Sleep(remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25));
                }
            }
            if (!ShortcutNativeDirectory.ReadInfo(handle).IsSingleRegularFile) return ShortcutReceiptStatus.Invalid;
            transaction = new(scope, handle, key + ".json", bindingId);
            scope = null;
            handle = null;
            return ShortcutReceiptStatus.Ok;
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { return Map(ex); }
        finally { handle?.Dispose(); scope?.Dispose(); }
    }

    internal sealed class Transaction(ShortcutNativeDirectory scope, SafeFileHandle gate, string name, string bindingId) : IDisposable
    {
        internal ShortcutReceiptRead Read()
        {
            ObjectDisposedException.ThrowIf(gate.IsClosed, this);
            return ShortcutReceiptStore.Read(scope, name, bindingId);
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
                    ShortcutNativeDirectory.ReadAccess | ShortcutNativeDirectory.WriteAccess | ShortcutNativeDirectory.DeleteAccess,
                    disposition: 1);
                using var stream = new FileStream(stage, FileAccess.ReadWrite);
                bool published = false;
                try
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                    // The lock serializes product writers. Re-read before an atomic private receipt replacement.
                    ShortcutReceiptRead latest = Read();
                    if (latest.Status is not (ShortcutReceiptStatus.Ok or ShortcutReceiptStatus.Missing)) return latest.Status;
                    if (latest != existing) return ShortcutReceiptStatus.Busy;
                    scope.Publish(stage, name, replace: existing.Status == ShortcutReceiptStatus.Ok, ref published);
                    return ShortcutReceiptStatus.Ok;
                }
                finally
                {
                    if (!published)
                        try { ShortcutNativeDirectory.Delete(stage); }
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
                using var handle = scope.OpenChild(name, ShortcutNativeDirectory.ReadAccess | ShortcutNativeDirectory.DeleteAccess);
                ShortcutReceiptRead current = Decode(handle, bindingId);
                if (current.Status != ShortcutReceiptStatus.Ok) return current.Status;
                ShortcutNativeDirectory.Delete(handle);
                return ShortcutReceiptStatus.Ok;
            }
            catch (Win32Exception ex) when (ShortcutNativeDirectory.IsMissing(ex)) { return ShortcutReceiptStatus.Ok; }
            catch (Exception ex) when (IsStorageFailure(ex)) { return Map(ex); }
        }

        public void Dispose()
        {
            gate.Dispose();
            scope.Dispose();
        }
    }

    private static ShortcutReceiptRead Read(ShortcutNativeDirectory scope, string name, string id)
    {
        try
        {
            using var handle = scope.OpenChild(name, ShortcutNativeDirectory.ReadAccess);
            return Decode(handle, id);
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { return new(Map(ex)); }
    }

    private static ShortcutReceiptRead Decode(SafeFileHandle handle, string id)
    {
        var info = ShortcutNativeDirectory.ReadInfo(handle);
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
        return receipt is not null && Valid(receipt, id) ? new(ShortcutReceiptStatus.Ok, receipt) : new(ShortcutReceiptStatus.Invalid);
    }

    private static bool Valid(ShortcutReceipt receipt, string id) =>
        receipt.SchemaVersion == 1 && ManagedLaunchRequest.EncodeBindingId(receipt.BindingId) is not null &&
        string.Equals(receipt.BindingId, id, StringComparison.OrdinalIgnoreCase) &&
        (receipt.Current is not null || receipt.Pending is not null) &&
        (receipt.Current is null || Valid(receipt.Current)) && (receipt.Pending is null || Valid(receipt.Pending));

    private static bool Valid(ShortcutProof proof) =>
        ShortcutNativeDirectory.IsCanonicalLocalPath(proof.Path) && proof.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
        ShortcutNativeDirectory.IsCanonicalLocalPath(proof.LauncherPath) && proof.LauncherPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        proof.FileIndex != 0 && proof.CreationTime > 0 &&
        proof.Sha256 is { Length: 64 } && proof.Sha256.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F');

    private static string Key(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToUpperInvariant())));

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or Win32Exception or InvalidDataException;

    private static ShortcutReceiptStatus Map(Exception ex) => ex switch
    {
        Win32Exception native when ShortcutNativeDirectory.IsMissing(native) => ShortcutReceiptStatus.Missing,
        Win32Exception native when ShortcutNativeDirectory.IsSharing(native) => ShortcutReceiptStatus.Busy,
        InvalidDataException => ShortcutReceiptStatus.Unavailable,
        JsonException => ShortcutReceiptStatus.Invalid,
        _ => ShortcutReceiptStatus.IoError
    };
}
