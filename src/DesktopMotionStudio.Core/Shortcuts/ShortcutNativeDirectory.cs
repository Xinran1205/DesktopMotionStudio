using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DesktopMotionStudio.Core.IO;
using Microsoft.Win32.SafeHandles;

namespace DesktopMotionStudio.Core.Shortcuts;

// A bounded local NTFS directory scope. Pin each component before resolving the next.
internal sealed class ShortcutNativeDirectory(string path, List<SafeFileHandle> pins) : IDisposable
{
    internal string Path { get; } = path;

    internal static bool IsCanonicalLocalPath(string? path)
    {
        if (path is null || path.Length is < 3 or > 259 ||
            !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
            return false;
        if (path.Length > 3 && path[3..].Split('\\').Any(part => !LocalPathPolicy.IsUnambiguousName(part))) return false;
        return string.Equals(path, System.IO.Path.GetFullPath(path), StringComparison.Ordinal);
    }

    internal static ShortcutNativeDirectory Open(string path, bool create = false)
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
            string current = root;
            pins.Add(Pin(current));
            foreach (string component in path[3..].Split('\\'))
            {
                current = System.IO.Path.Combine(current, component);
                try { pins.Add(Pin(current)); }
                catch (Win32Exception ex) when (create && ex.NativeErrorCode == 2)
                {
                    Directory.CreateDirectory(current);
                    pins.Add(Pin(current));
                }
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

    internal SafeFileHandle OpenChild(string name, uint access, uint share = 1, uint disposition = 3) =>
        OpenFile(Child(name), access, share, disposition, 0x00200000 | 0x02000000);

    internal void Publish(SafeFileHandle staged, string name, bool replace, ref bool published)
    {
        string destination = Child(name);
        var stagedInfo = ReadInfo(staged);
        if (!stagedInfo.IsSingleRegularFile) throw new InvalidDataException("Staged file is no longer a single regular file.");

        // The stage keeps the directory nonempty while its write-sharing pin is in force.
        int last = pins.Count - 1;
        var original = ReadInfo(pins[last]);
        using SafeFileHandle publishPin = Pin(Path, allowWrite: true);
        if (!original.SameObject(ReadInfo(publishPin)))
            throw new InvalidDataException("The shortcut directory identity changed.");
        pins[last].Dispose();
        try
        {
            Rename(staged, destination, replace);
            published = true;
        }
        finally
        {
            // Restoration can fail after the rename committed. The caller still owns a
            // published file in that case and must not clean it up as a temporary stage.
            try { pins[last] = Pin(Path); }
            catch { Dispose(); throw; }
        }
    }

    internal static FileInfo ReadInfo(SafeFileHandle handle) =>
        GetFileInformationByHandle(handle, out FileInfo info) ? info : throw new Win32Exception(Marshal.GetLastWin32Error());

    internal static void Delete(SafeFileHandle handle)
    {
        if (!SetFileDisposition(handle, 4, [1], 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal static bool IsMissing(Win32Exception ex) => ex.NativeErrorCode is 2 or 3;
    internal static bool IsSharing(Win32Exception ex) => ex.NativeErrorCode is 32 or 33;

    public void Dispose() => Release(pins);

    private static void Release(List<SafeFileHandle> handles)
    {
        for (int i = handles.Count - 1; i >= 0; i--) handles[i].Dispose();
        handles.Clear();
    }

    private static SafeFileHandle Pin(string path, bool allowWrite = false)
    {
        SafeFileHandle handle = OpenFile(path, 1, allowWrite ? 3u : 1u, 3, 0x00200000 | 0x02000000);
        try
        {
            if ((ReadInfo(handle).Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                throw new InvalidDataException("Shortcut path includes a reparse point or non-directory.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static SafeFileHandle OpenFile(string path, uint access, uint share, uint disposition, uint flags)
    {
        SafeFileHandle handle = CreateFileW(path, access, share, 0, disposition, flags, 0);
        if (!handle.IsInvalid) return handle;
        int error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new Win32Exception(error);
    }

    private static void Rename(SafeFileHandle file, string destination, bool replace)
    {
        // FILE_RENAME_INFO layout on Windows x64. RootDirectory is null for an absolute name.
        byte[] name = Encoding.Unicode.GetBytes(destination + "\0");
        const int nameOffset = 20;
        int size = nameOffset + name.Length;
        nint info = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt64(info, replace ? 1L : 0L);
            Marshal.WriteIntPtr(info, 8, 0);
            Marshal.WriteInt32(info, 16, name.Length - 2);
            Marshal.Copy(name, 0, info + nameOffset, name.Length);
            if (!SetFileInformationByHandle(file, 3, info, (uint)size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
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
        internal readonly long CreationTime => ((long)Creation.dwHighDateTime << 32) | (uint)Creation.dwLowDateTime;
        internal readonly ulong Length => ((ulong)SizeHigh << 32) | SizeLow;
        internal readonly bool IsSingleRegularFile =>
            Links == 1 && (Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) == 0;
        internal readonly bool SameObject(FileInfo other) =>
            VolumeSerial == other.VolumeSerial && FileIndex == other.FileIndex && CreationTime == other.CreationTime;
    }

    internal const uint ReadAccess = 0x80000000;
    internal const uint WriteAccess = 0x40000000;
    internal const uint DeleteAccess = 0x00010000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfo info);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, nint info, uint size);

    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileDisposition(SafeFileHandle file, int informationClass, byte[] info, uint size);
}
