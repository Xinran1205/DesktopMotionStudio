using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DesktopMotionStudio.Core.IO;
using Microsoft.Win32.SafeHandles;

namespace DesktopMotionStudio.Core.IO;

// A bounded local NTFS directory scope. Pin each component before resolving the next.
internal sealed class WindowsFileScope(string path, List<SafeFileHandle> pins) : IDisposable
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
        return OpenRelative(pins[^1], name, access, share, disposition, directory: false);
    }

    internal void Publish(SafeFileHandle staged, string name)
    {
        _ = Child(name);
        var stagedInfo = ReadInfo(staged);
        if (!stagedInfo.IsSingleRegularFile) throw new InvalidDataException("Staged file is no longer a single regular file.");
        // A bare filename and null RootDirectory keep the retained file in its actual parent.
        Rename(staged, name);
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

    private static void VerifyDirectory(SafeFileHandle handle)
    {
        if ((ReadInfo(handle).Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new InvalidDataException("Shortcut path includes a reparse point or non-directory.");
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

    private static void Rename(SafeFileHandle file, string destination)
    {
        // FILE_RENAME_INFORMATION layout on Windows x64, with replacement disabled.
        byte[] name = Encoding.Unicode.GetBytes(destination + "\0");
        const int nameOffset = 20;
        int size = nameOffset + name.Length;
        nint info = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt64(info, 0);
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
