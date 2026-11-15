using System.ComponentModel;
using System.Text.Json;
using DesktopMotionStudio.Core.IO;
using DesktopMotionStudio.Core.Scenes;
using Microsoft.Win32.SafeHandles;

namespace DesktopMotionStudio.Core.Video;

public sealed class ImportedVideoStore(string directory)
{
    private const string MediaFile = "clip.mp4";
    private const string WorkFile = "work.json";
    private const string OwnerFile = "draft.json";
    private const string DraftPrefix = ".draft-";
    private const string TrashPrefix = ".trash-";
    private const int MaxMetadataBytes = 4096;
    private readonly string _directory = Path.GetFullPath(directory);

    public static ImportedVideoStore ForProfile(string profile) =>
        new(Path.GetFullPath(profile) + ".motions");

    public async Task<ImportResult<Draft>> PrepareAsync(string sourcePath, VideoInspector inspector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        WindowsFileScope? root = null;
        WindowsFileScope.StagedDirectory? stage = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LocalPathPolicy.Inspect(sourcePath, out string local, out var attributes) != LocalPathError.None ||
                (attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0 ||
                !string.Equals(Path.GetExtension(local), ".mp4", StringComparison.OrdinalIgnoreCase))
                return new(null, ImportError.UnsafePath);

            await using var source = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
            if (!WindowsFileScope.ReadInfo(source.SafeFileHandle).IsSingleRegularFile ||
                LocalPathPolicy.Inspect(local, out _, out _) != LocalPathError.None) return new(null, ImportError.UnsafePath);
            long length = source.Length;
            if (length == 0) return new(null, ImportError.Empty);
            if (length > VideoClip.MaximumBytes) return new(null, ImportError.TooLarge);

            root = WindowsFileScope.Open(_directory, create: true);
            RecoverDrafts(root);
            string id = ImportedVideoWork.Prefix + Guid.NewGuid().ToString("N");
            stage = root.StageDirectory(DraftPrefix + id);
            ImportFileIdentity identity;
            using (var target = stage.Scope.OpenChild(MediaFile, WindowsFileScope.WriteAccess, disposition: WindowsFileScope.CreateNew))
            {
                identity = ImportFileIdentity.Of(WindowsFileScope.ReadInfo(target));
                using (var marker = stage.Scope.OpenChild(OwnerFile, WindowsFileScope.WriteAccess, disposition: WindowsFileScope.CreateNew))
                    WriteJson(marker, new DraftOwner(1, id, identity));

                byte[] buffer = new byte[64 * 1024];
                long copied = 0;
                while (copied < length)
                {
                    int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - copied)),
                        cancellationToken).ConfigureAwait(false);
                    if (read == 0) return new(null, ImportError.Changed);
                    RandomAccess.Write(target, buffer.AsSpan(0, read), copied);
                    copied += read;
                }
                if (source.ReadByte() != -1) return new(null, ImportError.Changed);
                RandomAccess.FlushToDisk(target);
            }
            await source.DisposeAsync().ConfigureAwait(false);
            string asset = stage.Scope.Child(MediaFile);
            using var validated = stage.Scope.OpenChild(MediaFile, WindowsFileScope.ReadAccess);
            var verified = WindowsFileScope.ReadInfo(validated);
            if (!verified.IsSingleRegularFile || verified.Length != (ulong)length || ImportFileIdentity.Of(verified) != identity)
                return new(null, ImportError.Changed);
            var inspection = await inspector.InspectAsync(asset, cancellationToken).ConfigureAwait(false);
            if (inspection.Error != VideoInspectionError.None)
                return new(null, inspection.Error switch
                {
                    VideoInspectionError.InvalidMedia => ImportError.InvalidMedia,
                    VideoInspectionError.TimedOut => ImportError.TimedOut,
                    _ => ImportError.DecoderUnavailable
                });
            cancellationToken.ThrowIfCancellationRequested();
            var draft = new Draft(root, stage, id, Path.GetFileNameWithoutExtension(local), asset, verified, inspection);
            stage = null;
            root = null;
            return new(draft, ImportError.None);
        }
        catch (Exception error) when (IsStorageFailure(error)) { return new(null, Map(error)); }
        finally
        {
            try { stage?.Dispose(); }
            finally { root?.Dispose(); }
        }
    }

    public ImportedVideoWork? Find(string id)
    {
        if (!ImportedVideoWork.IsId(id)) return null;
        try
        {
            using var root = WindowsFileScope.Open(_directory);
            return TryRead(root, id);
        }
        catch (Exception error) when (IsStorageFailure(error)) { return null; }
    }

    public IReadOnlyList<ImportedVideoWork> List()
    {
        try
        {
            using var root = WindowsFileScope.Open(_directory);
            return root.ListNames(4096).Where(ImportedVideoWork.IsId).Order(StringComparer.Ordinal)
                .Select(id => TryRead(root, id)).OfType<ImportedVideoWork>().ToArray();
        }
        catch (Exception error) when (IsStorageFailure(error)) { return []; }
    }

    public IReadOnlyList<SceneLibraryItem> ListLibraryItems() => List().Select(work => work.ToLibraryItem()).ToArray();

    public ImportResult<ImportedVideoWork> Edit(string id, int revision, ImportedVideoOptions options)
    {
        if (!ImportedVideoWork.IsId(id)) return new(null, ImportError.Missing);
        try
        {
            using var root = WindowsFileScope.Open(_directory);
            using var mutation = root.OpenMovableDirectory(id);
            var folder = mutation.Scope;
            var metadata = ReadWork(folder, id, out var priorIdentity);
            if (metadata is null || metadata.Revision != revision || revision == int.MaxValue)
                return new(null, ImportError.Changed);
            if (!options.IsValid(metadata.SourceDurationMilliseconds)) return new(null, ImportError.InvalidOptions);
            using var media = folder.OpenChild(MediaFile, WindowsFileScope.ReadAccess);
            if (!MatchesMedia(media, metadata.Media)) return new(null, ImportError.Changed);
            var next = metadata.WithOptions(options, revision + 1);
            string temp = ".work-" + Guid.NewGuid().ToString("N");
            using (var file = folder.OpenChild(temp, WindowsFileScope.ReadAccess | WindowsFileScope.WriteAccess | WindowsFileScope.DeleteAccess, share: 4,
                disposition: WindowsFileScope.CreateNew))
            {
                try
                {
                    WriteJson(file, next);
                    var current = ReadWork(folder, id, out var currentIdentity);
                    if (current != metadata || currentIdentity != priorIdentity)
                    {
                        WindowsFileScope.Delete(file);
                        return new(null, ImportError.Changed);
                    }
                    folder.ReplaceFile(file, WorkFile);
                }
                catch
                {
                    WindowsFileScope.Delete(file);
                    throw;
                }
            }
            return new(ToWork(folder, next), ImportError.None);
        }
        catch (Exception error) when (IsStorageFailure(error)) { return new(null, Map(error)); }
    }

    public ImportError Remove(string id, int revision)
    {
        if (!ImportedVideoWork.IsId(id)) return ImportError.Missing;
        try
        {
            using var root = WindowsFileScope.Open(_directory);
            using var mutation = root.OpenMovableDirectory(id);
            var folder = mutation.Scope;
            var metadata = ReadWork(folder, id);
            if (metadata is null || metadata.Revision != revision) return ImportError.Changed;
            string tombstone = TrashPrefix + id;
            using (var media = folder.OpenChild(MediaFile, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess, share: 4))
                if (!MatchesMedia(media, metadata.Media)) return ImportError.Changed;
            // The Windows directory rename requires closed descendant file handles.
            mutation.Publish(tombstone);
            TryCleanTombstone(root, tombstone, id);
            return ImportError.None;
        }
        catch (Exception error) when (IsStorageFailure(error)) { return Map(error); }
    }

    private static ImportedVideoWork? TryRead(WindowsFileScope root, string id)
    {
        try
        {
            using var folder = root.OpenDirectory(id, WindowsFileScope.OpenExisting);
            var metadata = ReadWork(folder, id);
            using var media = folder.OpenChild(MediaFile, WindowsFileScope.ReadAccess, share: 1);
            return metadata is not null && MatchesMedia(media, metadata.Media) ? ToWork(folder, metadata) : null;
        }
        catch (Exception error) when (IsStorageFailure(error)) { return null; }
    }

    private static ImportedVideoMetadata? ReadWork(WindowsFileScope folder, string id) => ReadWork(folder, id, out _);

    private static ImportedVideoMetadata? ReadWork(WindowsFileScope folder, string id, out ImportFileIdentity identity)
    {
        using var file = folder.OpenChild(WorkFile, WindowsFileScope.ReadAccess);
        identity = ImportFileIdentity.Of(WindowsFileScope.ReadInfo(file));
        return ImportedVideoMetadata.Read(ReadBytes(file), id);
    }

    private static ImportedVideoWork ToWork(WindowsFileScope folder, ImportedVideoMetadata metadata) =>
        new(metadata.Id, metadata.Revision, metadata.SourceDurationMilliseconds, metadata.HasAudio, metadata.Options, folder.Child(MediaFile));

    private static byte[] ReadBytes(SafeFileHandle file)
    {
        var info = WindowsFileScope.ReadInfo(file);
        if (!info.IsSingleRegularFile || info.Length is 0 or > MaxMetadataBytes)
            throw new InvalidDataException("Invalid motion metadata file.");
        byte[] bytes = new byte[(int)info.Length];
        if (RandomAccess.Read(file, bytes, 0) != bytes.Length)
            throw new InvalidDataException("Incomplete motion metadata file.");
        return bytes;
    }

    private static void WriteJson<T>(SafeFileHandle file, T data)
    {
        RandomAccess.Write(file, JsonSerializer.SerializeToUtf8Bytes(data, SceneDataJson.Options), 0);
        RandomAccess.FlushToDisk(file);
    }

    private static bool MatchesMedia(SafeFileHandle handle, ImportFileIdentity identity)
    {
        var info = WindowsFileScope.ReadInfo(handle);
        return info.IsSingleRegularFile && info.Length is > 0 and <= VideoClip.MaximumBytes &&
            identity == ImportFileIdentity.Of(info);
    }

    private static bool IsStorageFailure(Exception error) =>
        error is Win32Exception or IOException or UnauthorizedAccessException or InvalidDataException;

    private static ImportError Map(Exception error) => (error is Win32Exception native ? native.NativeErrorCode : error.HResult & 0xFFFF) switch
    {
        2 or 3 => ImportError.Missing,
        32 or 33 => ImportError.Busy,
        39 or 112 => ImportError.NoSpace,
        80 or 183 => ImportError.Changed,
        _ => ImportError.StorageUnavailable
    };

    public void RecoverDrafts()
    {
        try
        {
            using var root = WindowsFileScope.Open(_directory);
            RecoverDrafts(root);
        }
        catch (Exception error) when (IsStorageFailure(error)) { }
    }

    private static void RecoverDrafts(WindowsFileScope root)
    {
        foreach (string name in root.ListNames(4096))
        {
            if (name.StartsWith(TrashPrefix, StringComparison.Ordinal) && ImportedVideoWork.IsId(name[TrashPrefix.Length..]))
            {
                TryCleanTombstone(root, name, name[TrashPrefix.Length..]);
                continue;
            }
            if (!name.StartsWith(DraftPrefix, StringComparison.Ordinal)) continue;
            string id = name[DraftPrefix.Length..];
            if (!ImportedVideoWork.IsId(id)) continue;
            try
            {
                // The draft owner pins this directory with delete access until it saves or closes.
                using var stale = root.OpenMovableDirectory(name);
                var folder = stale.Scope;
                var folderIdentity = folder.Identity;
                using (var marker = folder.OpenChild(OwnerFile, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess))
                {
                    var owner = DraftOwner.Read(ReadBytes(marker), id);
                    if (owner is null) continue;
                    try
                    {
                        using var media = folder.OpenChild(MediaFile, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess);
                        var info = WindowsFileScope.ReadInfo(media);
                        if (!info.IsSingleRegularFile || ImportFileIdentity.Of(info) != owner.Media) continue;
                        if (!RemoveDraftMetadata(folder, id, owner.Media)) continue;
                        WindowsFileScope.Delete(media);
                    }
                    catch (Win32Exception error) when (WindowsFileScope.IsMissing(error))
                    {
                        if (!RemoveDraftMetadata(folder, id, owner.Media)) continue;
                    }
                    WindowsFileScope.Delete(marker);
                }
                stale.Dispose();
                root.RemoveEmptyDirectory(name, folderIdentity);
            }
            catch (Exception error) when (IsStorageFailure(error)) { }
        }
    }

    private static void TryCleanTombstone(WindowsFileScope root, string name, string id)
    {
        try
        {
            using var mutation = root.OpenMovableDirectory(name);
            var folder = mutation.Scope;
            var identity = folder.Identity;
            using (var metadataFile = folder.OpenChild(WorkFile, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess))
            {
                var metadata = ImportedVideoMetadata.Read(ReadBytes(metadataFile), id);
                if (metadata is null) return;
                try
                {
                    using var media = folder.OpenChild(MediaFile, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess);
                    if (!MatchesMedia(media, metadata.Media)) return;
                    WindowsFileScope.Delete(media);
                }
                catch (Win32Exception error) when (WindowsFileScope.IsMissing(error)) { }
                WindowsFileScope.Delete(metadataFile);
            }
            mutation.Dispose();
            root.RemoveEmptyDirectory(name, identity);
        }
        catch (Exception error) when (IsStorageFailure(error)) { }
    }

    private static bool RemoveDraftMetadata(WindowsFileScope folder, string id, ImportFileIdentity identity)
    {
        try
        {
            using var file = folder.OpenChild(WorkFile, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess);
            var metadata = ImportedVideoMetadata.Read(ReadBytes(file), id);
            if (metadata?.Media != identity) return false;
            WindowsFileScope.Delete(file);
            return true;
        }
        catch (Win32Exception error) when (WindowsFileScope.IsMissing(error)) { return true; }
    }

    private sealed record DraftOwner(int SchemaVersion, string Id, ImportFileIdentity Media)
    {
        private static readonly HashSet<string> Fields = new(StringComparer.Ordinal) { "schemaVersion", "id", "media" };

        internal static DraftOwner? Read(byte[] bytes, string id)
        {
            var owner = SceneDataJson.Read<DraftOwner>(bytes, Fields).Value;
            return owner is { Media.Index: > 0 } && owner.Id == id ? owner : null;
        }
    }

    public sealed class Draft : IDisposable
    {
        private readonly WindowsFileScope _root;
        private readonly WindowsFileScope.StagedDirectory _stage;
        private readonly WindowsFileScope.FileInfo _verified;
        private bool _saved;
        private bool _closed;

        internal Draft(WindowsFileScope root, WindowsFileScope.StagedDirectory stage, string id, string name, string asset,
            WindowsFileScope.FileInfo verified, VideoInspectionResult inspection)
        {
            _root = root;
            _stage = stage;
            _verified = verified;
            Id = id;
            SuggestedName = name;
            AssetPath = asset;
            Inspection = inspection;
        }

        public string Id { get; }
        public string SuggestedName { get; }
        public string AssetPath { get; }
        public VideoInspectionResult Inspection { get; }

        public ImportResult<ImportedVideoWork> Save(ImportedVideoOptions options, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_saved) return new(null, ImportError.Changed);
            if (!options.IsValid(Inspection.DurationMilliseconds)) return new(null, ImportError.InvalidOptions);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var identity = ImportFileIdentity.Of(_verified);
                using (var media = _stage.Scope.OpenChild(MediaFile, WindowsFileScope.ReadAccess, share: 1))
                {
                    var current = WindowsFileScope.ReadInfo(media);
                    if (!MatchesMedia(media, identity) || current.Length != _verified.Length ||
                        !current.Write.Equals(_verified.Write)) return new(null, ImportError.Changed);
                }
                var metadata = new ImportedVideoMetadata(1, Id, 1, options.Name, Inspection.DurationMilliseconds,
                    options.DurationMilliseconds, "complete", false, Inspection.HasAudio, identity).WithOptions(options, 1);
                ImportFileIdentity metadataIdentity;
                using (var file = _stage.Scope.OpenChild(WorkFile, WindowsFileScope.WriteAccess | WindowsFileScope.DeleteAccess,
                    disposition: WindowsFileScope.CreateNew))
                {
                    metadataIdentity = ImportFileIdentity.Of(WindowsFileScope.ReadInfo(file));
                    try { WriteJson(file, metadata); }
                    catch { WindowsFileScope.Delete(file); throw; }
                }
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _stage.Publish(Id);
                }
                catch
                {
                    TryDiscardMetadata(metadataIdentity);
                    throw;
                }
                _saved = true;
                TryRemovePublishedMarker(Id, identity);
                return new(new(Id, 1, Inspection.DurationMilliseconds, Inspection.HasAudio, metadata.Options,
                    Path.Combine(_root.Child(Id), MediaFile)), ImportError.None);
            }
            catch (Exception error) when (IsStorageFailure(error)) { return new(null, Map(error)); }
        }

        private void TryDiscardMetadata(ImportFileIdentity expected)
        {
            try
            {
                using var file = _stage.Scope.OpenChild(WorkFile, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess);
                var info = WindowsFileScope.ReadInfo(file);
                if (info.IsSingleRegularFile && ImportFileIdentity.Of(info) == expected) WindowsFileScope.Delete(file);
            }
            catch (Exception error) when (IsStorageFailure(error)) { }
        }

        private void TryRemovePublishedMarker(string id, ImportFileIdentity identity)
        {
            try
            {
                using var folder = _root.OpenDirectory(id, WindowsFileScope.OpenExisting);
                using var marker = folder.OpenChild(OwnerFile, WindowsFileScope.ReadAccess | WindowsFileScope.DeleteAccess);
                if (DraftOwner.Read(ReadBytes(marker), id)?.Media == identity) WindowsFileScope.Delete(marker);
            }
            catch (Exception error) when (IsStorageFailure(error)) { }
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            try { _stage.Dispose(); }
            finally { _root.Dispose(); }
        }
    }
}
