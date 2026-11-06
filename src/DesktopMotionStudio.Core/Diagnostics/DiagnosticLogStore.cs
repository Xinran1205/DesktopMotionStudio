using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopMotionStudio.Core.IO;
using Microsoft.Win32.SafeHandles;

namespace DesktopMotionStudio.Core.Diagnostics;

public enum DiagnosticStatus { Empty, Ready, Stored, Busy, Invalid, Unsupported, Unavailable }

public sealed record DiagnosticSnapshot(DiagnosticStatus Status, IReadOnlyList<DiagnosticEvent> Events);

public sealed class DiagnosticLogStore(string directory)
{
    private const int MaxEvents = 128, MaxBytes = 32 * 1024;
    private const string Primary = "events.json", Previous = "events.previous", Next = "events.next";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 8,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public string DirectoryPath => directory;
    public static string CurrentUserDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopMotionStudio", "logs");

    public DiagnosticSnapshot Read()
    {
        try
        {
            using var scope = WindowsFileScope.Open(Path.Combine(directory, "records"));
            using Entry current = Open(scope, Primary);
            using Entry previous = Open(scope, Previous);
            using Entry next = Open(scope, Next);
            DiagnosticStatus error = Failure(current, previous, next);
            if (error != DiagnosticStatus.Ready) return new(error, []);
            return current.Snapshot.Status == DiagnosticStatus.Ready ? current.Snapshot : previous.Snapshot;
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { return new(Map(ex), []); }
    }

    public DiagnosticStatus Append(params DiagnosticEvent[] events)
    {
        if (events is null || events.Length is < 1 or > 3 || events.Any(item => item is null || !item.IsValid))
            return DiagnosticStatus.Invalid;

        try
        {
            using var locks = WindowsFileScope.Open(Path.Combine(directory, "locks"), create: true);
            using var gate = locks.OpenChild("events.lock", WindowsFileScope.ReadAccess | WindowsFileScope.WriteAccess,
                share: 0, disposition: WindowsFileScope.OpenIf);
            if (!WindowsFileScope.ReadInfo(gate).IsSingleRegularFile) return DiagnosticStatus.Invalid;
            using var scope = WindowsFileScope.Open(Path.Combine(directory, "records"), create: true);
            using Entry current = Open(scope, Primary, forMutation: true);
            using Entry previous = Open(scope, Previous, forMutation: true);
            using Entry abandoned = Open(scope, Next, forMutation: true);
            DiagnosticStatus error = Failure(current, previous, abandoned);
            if (error != DiagnosticStatus.Ready) return error;

            IReadOnlyList<DiagnosticEvent> existing =
                current.Snapshot.Status == DiagnosticStatus.Ready ? current.Snapshot.Events : previous.Snapshot.Events;
            byte[] bytes = Encode(existing, events);

            // Retire only verified obsolete generations before creating the fixed stage.
            // At each durable step the previous or current committed generation survives.
            abandoned.Delete();
            if (current.Handle is not null) previous.Delete();
            using var stage = scope.OpenChild(Next, WindowsFileScope.ReadAccess | WindowsFileScope.WriteAccess | WindowsFileScope.DeleteAccess,
                disposition: WindowsFileScope.CreateNew);
            using var stream = new FileStream(stage, FileAccess.ReadWrite);
            bool published = false;
            try
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
                if (current.Handle is not null)
                {
                    scope.Publish(current.Handle, Previous);
                    current.Dispose();
                }
                scope.Publish(stage, Primary);
                published = true;
                return DiagnosticStatus.Stored;
            }
            finally
            {
                if (!published)
                    try { WindowsFileScope.Delete(stage); }
                    catch (Win32Exception) { }
            }
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            DiagnosticStatus mapped = Map(ex);
            return mapped == DiagnosticStatus.Empty ? DiagnosticStatus.Unavailable : mapped;
        }
    }

    private static byte[] Encode(IReadOnlyList<DiagnosticEvent> existing, DiagnosticEvent[] appended)
    {
        DiagnosticEvent[] kept = existing.Concat(appended).TakeLast(MaxEvents).ToArray();
        for (int first = 0; first < kept.Length; first++)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, kept[first..]), Json);
            if (bytes.Length <= MaxBytes) return bytes;
        }
        throw new InvalidDataException("Diagnostic batch exceeded its bound.");
    }

    private static Entry Open(WindowsFileScope scope, string name, bool forMutation = false)
    {
        SafeFileHandle? handle = null;
        try
        {
            uint access = WindowsFileScope.ReadAccess | (forMutation ? WindowsFileScope.DeleteAccess : 0);
            handle = scope.OpenChild(name, access);
            DiagnosticSnapshot snapshot = Decode(handle);
            if (!forMutation || snapshot.Status != DiagnosticStatus.Ready) return new(snapshot);
            var entry = new Entry(snapshot, handle);
            handle = null;
            return entry;
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { return new(new(Map(ex), [])); }
        finally { handle?.Dispose(); }
    }

    private static DiagnosticSnapshot Decode(SafeFileHandle handle)
    {
        var info = WindowsFileScope.ReadInfo(handle);
        if (!info.IsSingleRegularFile || info.Length is 0 or > MaxBytes) return new(DiagnosticStatus.Invalid, []);
        byte[] bytes = new byte[(int)info.Length];
        int read = 0;
        while (read < bytes.Length)
        {
            int count = RandomAccess.Read(handle, bytes.AsSpan(read), read);
            if (count == 0) return new(DiagnosticStatus.Invalid, []);
            read += count;
        }

        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8, AllowDuplicateProperties = false });
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("schemaVersion", out JsonElement version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int schema))
            return new(DiagnosticStatus.Invalid, []);
        if (schema > 1) return new(DiagnosticStatus.Unsupported, []);
        Envelope? data = JsonSerializer.Deserialize<Envelope>(bytes, Json);
        if (data is not { SchemaVersion: 1, Events.Length: > 0 and <= MaxEvents } ||
            data.Events.Any(item => item is null || !item.IsValid))
            return new(DiagnosticStatus.Invalid, []);
        return new(DiagnosticStatus.Ready, Array.AsReadOnly(data.Events));
    }

    private static DiagnosticStatus Failure(params Entry[] entries)
    {
        foreach (Entry entry in entries)
            if (entry.Snapshot.Status is not (DiagnosticStatus.Ready or DiagnosticStatus.Empty))
                return entry.Snapshot.Status;
        return DiagnosticStatus.Ready;
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidDataException or JsonException;

    private static DiagnosticStatus Map(Exception ex) => ex switch
    {
        Win32Exception native when WindowsFileScope.IsMissing(native) => DiagnosticStatus.Empty,
        Win32Exception native when WindowsFileScope.IsSharing(native) => DiagnosticStatus.Busy,
        JsonException => DiagnosticStatus.Invalid,
        _ => DiagnosticStatus.Unavailable
    };

    private sealed record Envelope([property: JsonRequired] int SchemaVersion, [property: JsonRequired] DiagnosticEvent[] Events);

    private sealed class Entry(DiagnosticSnapshot snapshot, SafeFileHandle? handle = null) : IDisposable
    {
        internal DiagnosticSnapshot Snapshot { get; } = snapshot;
        internal SafeFileHandle? Handle { get; } = handle;

        internal void Delete()
        {
            if (Handle is null) return;
            WindowsFileScope.Delete(Handle);
            Handle.Dispose();
        }

        public void Dispose() => Handle?.Dispose();
    }
}
