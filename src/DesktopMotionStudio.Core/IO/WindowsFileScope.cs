using System.ComponentModel;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using DesktopMotionStudio.Core.IO;
using Microsoft.Win32.SafeHandles;

namespace DesktopMotionStudio.Core.IO;

// A bounded local NTFS directory scope. Pin each component before resolving the next.
internal sealed class WindowsFileScope(string path, List<SafeFileHandle> pins) : IDisposable
{
    private StageContents? _stage;

    internal string Path { get; } = path;

    internal static bool IsCanonicalLocalPath(string? path)
    {
        if (path is null || path.Length is < 3 or > 259 ||
            !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
            return false;
        if (path.Length > 3 && path[3..].Split('\\').Any(part => !LocalPathPolicy.IsUnambiguousName(part))) return false;
        return string.Equals(path, System.IO.Path.GetFullPath(path), StringComparison.Ordinal);
    }

    internal static WindowsFileScope Open(string path, bool create = false)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            !IsCanonicalLocalPath(path) || path.Length == 3)
            throw new InvalidDataException("The shortcut directory is outside the supported local scope.");

        string root = path[..3];
        var drive = new DriveInfo(root);
        if (drive.DriveType != DriveType.Fixed || !drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Shortcut storage requires a fixed NTFS volume.");

        var pins = new List<SafeFileHandle>();
        try
        {
            pins.Add(PinRoot(root));
            foreach (string component in path[3..].Split('\\'))
            {
                SafeFileHandle handle = OpenRelative(pins[^1], component, 0x00100081, 3,
                    create ? OpenIf : OpenExisting, directory: true);
                pins.Add(handle);
                VerifyDirectory(handle);
            }
            return new(path, pins);
        }
        catch
        {
            Release(pins);
            throw;
        }
    }

    internal string Child(string name)
    {
        ObjectDisposedException.ThrowIf(pins.Count == 0, this);
        if (!LocalPathPolicy.IsUnambiguousName(name) || Path.Length + 1 + name.Length > 259)
            throw new InvalidDataException("The shortcut filename is outside the supported local scope.");
        return System.IO.Path.Combine(Path, name);
    }

    internal SafeFileHandle OpenChild(string name, uint access, uint share = 1, uint disposition = OpenExisting)
    {
        _ = Child(name);
        const uint readAttributes = 0x80;
        uint desiredAccess = _stage is not null && disposition == CreateNew ? access | readAttributes : access;
        SafeFileHandle handle = OpenRelative(pins[^1], name, desiredAccess, share, disposition, directory: false);
        if (_stage is null || disposition != CreateNew) return handle;
        try
        {
            FileInfo info = ReadInfo(handle);
            if (!info.IsSingleRegularFile) throw new InvalidDataException("A staged file is not regular.");
            _stage.Files.Add((info.VolumeSerial, info.FileIndex), name);
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    // The caller keeps this parent scope alive while using the returned child.
    internal WindowsFileScope OpenDirectory(string name, uint disposition) =>
        OpenDirectory(name, disposition, access: 0x00100081);

    internal StagedDirectory StageDirectory(string name)
    {
        WindowsFileScope directory = OpenDirectory(name, CreateNew, access: 0x00100081 | DeleteAccess);
        try
        {
            directory._stage = new StageContents(directory.Identity);
            return new(this, name, directory);
        }
        catch { directory.Dispose(); throw; }
    }

    internal StagedDirectory OpenMovableDirectory(string name) =>
        new(this, name, OpenDirectory(name, OpenExisting, access: 0x00100081 | DeleteAccess));

    private WindowsFileScope OpenDirectory(string name, uint disposition, uint access)
    {
        string path = Child(name);
        StageContents? tracked = null;
        if (_stage is not null)
        {
            _stage.Directories.TryGetValue(name, out tracked);
            if (disposition == OpenIf) disposition = tracked is null ? CreateNew : OpenExisting;
            if (disposition == OpenExisting && tracked is null)
                throw new InvalidDataException("The staging directory was not created by this operation.");
        }
        SafeFileHandle child = OpenRelative(pins[^1], name, access, 3, disposition, directory: true);
        try
        {
            FileInfo info = VerifyDirectory(child);
            if (_stage is not null)
            {
                var identity = (info.VolumeSerial, info.FileIndex);
                if (tracked is null) _stage.Directories.Add(name, tracked = new StageContents(identity));
                else if (tracked.Identity != identity) throw new InvalidDataException("A staged directory changed.");
            }
            return new(path, [child]) { _stage = tracked };
        }
        catch
        {
            child.Dispose();
            throw;
        }
    }

    private SafeFileHandle DirectoryHandle
    {
        get
        {
            ObjectDisposedException.ThrowIf(pins.Count == 0, this);
            return pins[^1];
        }
    }

    internal (uint Volume, ulong Index) Identity
    {
        get
        {
            FileInfo info = ReadInfo(DirectoryHandle);
            return (info.VolumeSerial, info.FileIndex);
        }
    }

    internal bool RemoveEmptyDirectory(string name, (uint Volume, ulong Index) expected)
    {
        try
        {
            using var child = OpenDirectory(name, OpenExisting, 0x00100081 | DeleteAccess);
            if (child.Identity != expected) return false;
            Delete(child.DirectoryHandle);
            return true;
        }
        catch (Win32Exception ex) when (IsMissing(ex)) { return true; }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 5 or 145 || IsSharing(ex)) { return false; }
    }

    internal sealed class StagedDirectory(WindowsFileScope parent, string temporaryName, WindowsFileScope directory) : IDisposable
    {
        private bool _retain, _disposed;
        internal WindowsFileScope Scope { get; } = directory;

        internal void RetainForRecovery() => _retain = true;

        internal void Publish(string name)
        {
            _ = parent.Child(name);
            SafeFileHandle handle = Scope.DirectoryHandle;
            VerifyDirectory(handle);
            Rename(handle, name);
            _retain = true;
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StageContents? abandoned = _retain ? null : Scope._stage;
            try { abandoned?.RemoveCreated(Scope); }
            finally { Scope.Dispose(); }
            if (abandoned is not null)
            {
                try { parent.RemoveEmptyDirectory(temporaryName, abandoned.Identity); }
                catch (Exception ex) when (IsStageCleanupFailure(ex)) { }
            }
        }
    }

    private sealed class StageContents((uint Volume, ulong Index) identity)
    {
        internal (uint Volume, ulong Index) Identity { get; } = identity;
        internal Dictionary<(uint Volume, ulong Index), string> Files { get; } = [];
        internal Dictionary<string, StageContents> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal void RemoveCreated(WindowsFileScope scope)
        {
            foreach (var (expected, name) in Files)
            {
                try
                {
                    using SafeFileHandle handle = scope.OpenChild(name, ReadAccess | DeleteAccess);
                    FileInfo current = ReadInfo(handle);
                    if (current.IsSingleRegularFile && (current.VolumeSerial, current.FileIndex) == expected)
                        Delete(handle);
                }
                catch (Exception ex) when (IsStageCleanupFailure(ex)) { }
            }
            foreach (var (name, contents) in Directories)
            {
                try
                {
                    using (WindowsFileScope child = scope.OpenDirectory(name, OpenExisting))
                        contents.RemoveCreated(child);
                    scope.RemoveEmptyDirectory(name, contents.Identity);
                }
                catch (Exception ex) when (IsStageCleanupFailure(ex)) { }
            }
        }
    }

    private static bool IsStageCleanupFailure(Exception ex) => ex is Win32Exception or IOException or
        InvalidDataException or UnauthorizedAccessException;

    internal void Publish(SafeFileHandle staged, string name)
    {
        _ = Child(name);
        var stagedInfo = ReadInfo(staged);
        if (!stagedInfo.IsSingleRegularFile) throw new InvalidDataException("Staged file is no longer a single regular file.");
        var identity = (stagedInfo.VolumeSerial, stagedInfo.FileIndex);
        if (_stage is not null && !_stage.Files.ContainsKey(identity))
            throw new InvalidDataException("The file was not created in this stage.");
        // A bare filename and null RootDirectory keep the retained file in its actual parent.
        Rename(staged, name);
        if (_stage is not null) _stage.Files[identity] = name;
    }

    internal void ReplaceFile(SafeFileHandle staged, string name)
    {
        _ = Child(name);
        if (!ReadInfo(staged).IsSingleRegularFile)
            throw new InvalidDataException("Replacement must be a single regular file.");
        Rename(staged, name, replace: true);
    }

    internal static FileInfo ReadInfo(SafeFileHandle handle) =>
        GetFileInformationByHandle(handle, out FileInfo info) ? info : throw new Win32Exception(Marshal.GetLastWin32Error());

    internal static void Delete(SafeFileHandle handle)
    {
        if (!SetFileDisposition(handle, 4, [1], 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }


    internal static bool IsMissing(Win32Exception ex) => ex.NativeErrorCode is 2 or 3;
    internal static bool IsSharing(Win32Exception ex) => ex.NativeErrorCode is 32 or 33;

    internal IReadOnlyList<string> ListNames(int maximum)
    {
        const int header = 68; // FILE_FULL_DIR_INFO.FileName
        const int restart = 15, next = 14;
        var names = new List<string>();
        byte[] buffer = new byte[16 * 1024];
        int infoClass = restart;
        while (GetDirectoryInformation(DirectoryHandle, infoClass, buffer, (uint)buffer.Length))
        {
            infoClass = next;
            int offset = 0;
            while (true)
            {
                Span<byte> record = buffer.AsSpan(offset);
                if (record.Length < header) throw new InvalidDataException("Invalid directory information.");
                int length = BinaryPrimitives.ReadInt32LittleEndian(record[60..]);
                if (length <= 0 || (length & 1) != 0 || length > record.Length - header)
                    throw new InvalidDataException("Invalid directory entry name.");
                string name = Encoding.Unicode.GetString(record.Slice(header, length));
                if (name is not ("." or ".."))
                {
                    if (names.Count == maximum) throw new InvalidDataException("The directory exceeds its enumeration limit.");
                    names.Add(name);
                }
                int following = BinaryPrimitives.ReadInt32LittleEndian(record);
                if (following == 0) break;
                if (following < header + length || following > record.Length - header)
                    throw new InvalidDataException("Invalid directory record offset.");
                offset += following;
            }
        }
        int error = Marshal.GetLastWin32Error();
        if (error != 18) throw new Win32Exception(error);
        return names;
    }

    public void Dispose() => Release(pins);

    private static void Release(List<SafeFileHandle> handles)
    {
        for (int i = handles.Count - 1; i >= 0; i--) handles[i].Dispose();
        handles.Clear();
    }

    private static SafeFileHandle PinRoot(string path)
    {
        SafeFileHandle handle = CreateFileW(path, 0x81, 3, 0, 3, 0x00200000 | 0x02000000, 0);
        try
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            VerifyDirectory(handle);
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static FileInfo VerifyDirectory(SafeFileHandle handle)
    {
        FileInfo info = ReadInfo(handle);
        if ((info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new InvalidDataException("Shortcut path includes a reparse point or non-directory.");
        return info;
    }

    private static SafeFileHandle OpenRelative(SafeFileHandle parent, string name, uint access, uint share, uint disposition, bool directory)
    {
        int length = checked(name.Length * sizeof(char));
        nint chars = Marshal.StringToHGlobalUni(name);
        nint unicode = 0;
        bool retained = false;
        try
        {
            unicode = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(new UnicodeString((ushort)length, (ushort)(length + 2), chars), unicode, false);
            parent.DangerousAddRef(ref retained);
            var attributes = new ObjectAttributes
            {
                Length = (uint)Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = unicode,
                Attributes = 0x1040 // OBJ_DONT_REPARSE | OBJ_CASE_INSENSITIVE
            };
            uint options = 0x00200020 | (directory ? 1u : disposition == OpenExisting ? 0u : 0x40u);
            int status = NtCreateFile(out var file, access | 0x00100000, in attributes, out _, 0, 0x80,
                share, disposition, options, 0, 0);
            if (status >= 0 && !file.IsInvalid) return file;
            file.Dispose();
            throw new Win32Exception(RtlNtStatusToDosError(status));
        }
        finally
        {
            if (retained) parent.DangerousRelease();
            Marshal.FreeHGlobal(unicode);
            Marshal.FreeHGlobal(chars);
        }
    }

    private static void Rename(SafeFileHandle file, string destination, bool replace = false)
    {
        // FILE_RENAME_INFORMATION layout on Windows x64.
        byte[] name = Encoding.Unicode.GetBytes(destination + "\0");
        const int nameOffset = 20;
        int size = nameOffset + name.Length;
        nint info = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt64(info, replace ? 1 : 0);
            Marshal.WriteIntPtr(info, 8, 0);
            Marshal.WriteInt32(info, 16, name.Length - 2);
            Marshal.Copy(name, 0, info + nameOffset, name.Length);
            int status = NtSetInformationFile(file, out _, info, (uint)size, 10);
            if (status < 0) throw new Win32Exception(RtlNtStatusToDosError(status));
        }
        finally { Marshal.FreeHGlobal(info); }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileInfo
    {
        internal FileAttributes Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        internal uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;

        internal readonly ulong FileIndex => ((ulong)IndexHigh << 32) | IndexLow;
        internal readonly ulong Length => ((ulong)SizeHigh << 32) | SizeLow;
        internal readonly bool IsSingleRegularFile =>
            Links == 1 && (Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct UnicodeString(ushort Length, ushort MaximumLength, nint Buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        internal uint Length;
        internal nint RootDirectory, ObjectName;
        internal uint Attributes;
        internal nint SecurityDescriptor, SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { internal nint Status; internal nuint Information; }

    internal const uint OpenExisting = 1, CreateNew = 2, OpenIf = 3;
    internal const uint ReadAccess = 0x80000000;
    internal const uint WriteAccess = 0x40000000;
    internal const uint DeleteAccess = 0x00010000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfo info);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDirectoryInformation(SafeFileHandle file, int infoClass, [Out] byte[] buffer, uint size);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtCreateFile(out SafeFileHandle file, uint access, in ObjectAttributes attributes,
        out IoStatusBlock ioStatus, nint allocationSize, uint fileAttributes, uint share, uint disposition,
        uint options, nint extendedAttributes, uint extendedLength);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtSetInformationFile(SafeFileHandle file, out IoStatusBlock ioStatus, nint info, uint size, int informationClass);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int RtlNtStatusToDosError(int status);

    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileDisposition(SafeFileHandle file, int informationClass, byte[] info, uint size);
}
