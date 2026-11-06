using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using DesktopMotionStudio.Core.Launch;
using Microsoft.Win32.SafeHandles;
using FileInfo = DesktopMotionStudio.Core.Shortcuts.ShortcutNativeDirectory.FileInfo;
using static DesktopMotionStudio.Core.Launch.ShellLinkTargetReader;
using static DesktopMotionStudio.Core.Shortcuts.ShortcutNativeDirectory;

namespace DesktopMotionStudio.Core.Shortcuts;

internal enum ShortcutFileState { Missing, Owned, Conflict, Busy, Unavailable }

internal sealed class WindowsShortcutPlatform(ShortcutNativeDirectory scope) : IDisposable
{
    private const int MaximumBytes = 64 * 1024;

    internal string Root => scope.Path;
    internal static WindowsShortcutPlatform Open(string root) => new(ShortcutNativeDirectory.Open(root));

    internal StagedShortcut Stage(string name, string launcher, string bindingId, string? icon)
    {
        string destination = LinkPath(name);
        string token = ManagedLaunchRequest.EncodeBindingId(bindingId) ??
            throw new InvalidDataException("The shortcut binding ID is invalid.");
        if (!IsCanonicalLocalPath(launcher) || !File.Exists(launcher) ||
            !LaunchTargetIdentityResolver.IsLocalPath(launcher) ||
            !launcher.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The shortcut launcher is unavailable.");

        if (!IsCanonicalLocalPath(icon) || !File.Exists(icon) || !LaunchTargetIdentityResolver.IsLocalPath(icon))
            icon = launcher;
        byte[] bytes = BuildLink(launcher, "--binding " + token, icon!);

        SafeFileHandle handle = scope.OpenChild($"{Guid.NewGuid():N}.tmp", ReadAccess | WriteAccess | DeleteAccess, disposition: 1);
        FileStream? stream = null;
        try
        {
            stream = new FileStream(handle, FileAccess.ReadWrite);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            FileInfo info = ReadInfo(handle);
            string hash = Digest(handle, info) ?? throw new InvalidDataException("The shortcut stage is not a regular bounded file.");
            return new(scope, name, stream, new(destination, launcher, info.VolumeSerial, info.FileIndex, hash));
        }
        catch
        {
            try { ShortcutNativeDirectory.Delete(handle); }
            catch (Win32Exception) { }
            finally { stream?.Dispose(); handle.Dispose(); }
            throw;
        }
    }

    internal ObservedShortcut Inspect(string name, ShortcutProof? first = null, ShortcutProof? second = null, bool forDelete = false)
    {
        SafeFileHandle? handle = null;
        try
        {
            string path = LinkPath(name);
            handle = scope.OpenChild(name, ReadAccess | (forDelete ? DeleteAccess : 0));
            FileInfo info = ReadInfo(handle);
            if (!info.IsSingleRegularFile) return new(ShortcutFileState.Conflict);
            bool couldMatchFirst = MatchesIdentity(first, path, info);
            bool couldMatchSecond = MatchesIdentity(second, path, info);
            if (!couldMatchFirst && !couldMatchSecond) return new(ShortcutFileState.Conflict);
            string? hash = Digest(handle, info);
            ShortcutProof? matched = couldMatchFirst && hash == first!.Sha256 ? first :
                couldMatchSecond && hash == second!.Sha256 ? second : null;
            if (matched is null) return new(ShortcutFileState.Conflict);
            var owned = new ObservedShortcut(ShortcutFileState.Owned, matched, forDelete ? handle : null);
            if (forDelete) handle = null;
            return owned;
        }
        catch (Win32Exception ex) when (IsMissing(ex)) { return new(ShortcutFileState.Missing); }
        catch (Win32Exception ex) when (IsSharing(ex)) { return new(ShortcutFileState.Busy); }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException or InvalidDataException)
        { return new(ShortcutFileState.Unavailable); }
        finally { handle?.Dispose(); }
    }

    private string LinkPath(string name) =>
        name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            ? scope.Child(name)
            : throw new InvalidDataException("The managed entry must be a shortcut.");

    internal sealed class StagedShortcut(ShortcutNativeDirectory scope, string name, FileStream stream, ShortcutProof proof) : IDisposable
    {
        private bool _published;
        internal ShortcutProof Proof { get; } = proof;

        internal void Publish()
        {
            if (_published) throw new InvalidOperationException("Shortcut stage is already published.");
            SafeFileHandle handle = stream.SafeFileHandle;
            FileInfo info = ReadInfo(handle);
            if (!MatchesIdentity(Proof, Proof.Path, info) || Digest(handle, info) != Proof.Sha256)
                throw new InvalidDataException("Shortcut stage changed before publication.");
            scope.Publish(handle, name, replace: false, ref _published);
        }

        public void Dispose()
        {
            if (!_published && stream.CanWrite)
                try { ShortcutNativeDirectory.Delete(stream.SafeFileHandle); }
                catch (Win32Exception) { }
                finally { stream.Dispose(); }
            else stream.Dispose();
        }
    }

    internal sealed class ObservedShortcut(ShortcutFileState state, ShortcutProof? proof = null, SafeFileHandle? handle = null) : IDisposable
    {
        internal ShortcutFileState State { get; } = state;
        internal ShortcutProof? Proof { get; } = proof;

        internal void Delete()
        {
            if (handle is null || Proof is null)
                throw new InvalidOperationException("No verified shortcut handle is held.");
            FileInfo info = ReadInfo(handle);
            if (!MatchesIdentity(Proof, Proof.Path, info) || Digest(handle, info) != Proof.Sha256)
                throw new InvalidDataException("Shortcut changed before removal.");
            ShortcutNativeDirectory.Delete(handle);
        }

        public void Dispose() => handle?.Dispose();
    }

    public void Dispose() => scope.Dispose();

    private static bool MatchesIdentity(ShortcutProof? proof, string path, FileInfo info) =>
        proof is not null && string.Equals(proof.Path, path, StringComparison.OrdinalIgnoreCase) &&
        info.IsSingleRegularFile && info.VolumeSerial == proof.VolumeSerial && info.FileIndex == proof.FileIndex;

    private static string? Digest(SafeFileHandle handle, FileInfo info)
    {
        if (!info.IsSingleRegularFile || info.Length is 0 or > MaximumBytes) return null;
        byte[] bytes = new byte[(int)info.Length];
        int read = 0;
        while (read < bytes.Length)
        {
            int count = RandomAccess.Read(handle, bytes.AsSpan(read), read);
            if (count == 0) return null;
            read += count;
        }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] BuildWindowsLink(string launcher, string args, string icon)
    {
        string working = Path.GetDirectoryName(launcher)!;
        object shell = new ShellLinkCom();
        try
        {
            var link = (IShellLinkWCom)shell;
            Marshal.ThrowExceptionForHR(link.SetPath(launcher));
            Marshal.ThrowExceptionForHR(link.SetArguments(args));
            Marshal.ThrowExceptionForHR(link.SetWorkingDirectory(working));
            // Separate generations even if an NTFS index eventually recycles. The nonce is
            // covered by the complete-file digest and has no authority in the shell command.
            Marshal.ThrowExceptionForHR(link.SetDescription($"Desktop Motion Studio ({Guid.NewGuid():N})"));
            Marshal.ThrowExceptionForHR(link.SetIconLocation(icon, 0));

            IStream stream = SHCreateMemStream(null, 0) ?? throw new IOException("Cannot allocate a shortcut stream.");
            try
            {
                ((IPersistStreamCom)shell).Save(stream, false);
                stream.Stat(out STATSTG info, 1);
                if (info.cbSize is <= 0 or > MaximumBytes) throw new InvalidDataException("Shortcut stream exceeded its size limit.");
                byte[] bytes = new byte[(int)info.cbSize];
                stream.Seek(0, 0, 0);
                nint count = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    stream.Read(bytes, bytes.Length, count);
                    if (Marshal.ReadInt32(count) != bytes.Length) throw new IOException("Incomplete shortcut stream.");
                }
                finally { Marshal.FreeHGlobal(count); }

                VerifyLink(bytes, launcher, args, working, icon);
                return bytes;
            }
            finally { Marshal.FinalReleaseComObject(stream); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private static byte[] BuildLink(string launcher, string args, string icon) =>
        OperatingSystem.IsWindows() ? BuildWindowsLink(launcher, args, icon) : throw new PlatformNotSupportedException();

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void VerifyLink(byte[] bytes, string launcher, string args, string working, string icon)
    {
        IStream stream = SHCreateMemStream(bytes, (uint)bytes.Length) ?? throw new IOException("Cannot read the shortcut stream.");
        try
        {
            object shell = new ShellLinkCom();
            try
            {
                ((IPersistStreamCom)shell).Load(stream);
                var link = (IShellLinkWCom)shell;
                var buffer = new StringBuilder(32768);
                Marshal.ThrowExceptionForHR(link.GetPath(buffer, buffer.Capacity, 0, 4));
                bool matches = string.Equals(buffer.ToString(), launcher, StringComparison.OrdinalIgnoreCase);
                buffer.Clear();
                Marshal.ThrowExceptionForHR(link.GetArguments(buffer, buffer.Capacity));
                matches &= string.Equals(buffer.ToString(), args, StringComparison.Ordinal);
                buffer.Clear();
                Marshal.ThrowExceptionForHR(link.GetWorkingDirectory(buffer, buffer.Capacity));
                matches &= string.Equals(buffer.ToString(), working, StringComparison.OrdinalIgnoreCase);
                buffer.Clear();
                Marshal.ThrowExceptionForHR(link.GetIconLocation(buffer, buffer.Capacity, out int index));
                matches &= index == 0 && string.Equals(buffer.ToString(), icon, StringComparison.OrdinalIgnoreCase);
                if (!matches) throw new InvalidDataException("Shortcut fields did not survive serialization.");
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
        finally { Marshal.FinalReleaseComObject(stream); }
    }

    [ComImport, Guid("00000109-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistStreamCom
    {
        void GetClassID(out Guid clsid);
        [PreserveSig] int IsDirty();
        void Load(IStream stream);
        void Save(IStream stream, [MarshalAs(UnmanagedType.Bool)] bool clearDirty);
        void GetSizeMax(out long size);
    }

    [DllImport("shlwapi.dll", ExactSpelling = true)]
    private static extern IStream? SHCreateMemStream(byte[]? bytes, uint size);
}
