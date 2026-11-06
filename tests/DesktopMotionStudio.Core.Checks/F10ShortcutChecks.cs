using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Shortcuts;
using Microsoft.Win32.SafeHandles;

internal static class F10ShortcutChecks
{
    internal static int Run()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f10-shell-");
        int failures = 0;
        void Check(string name, bool passed) => F09Checks.Check("F10 shell " + name, passed, ref failures);
        try { RunCases(fixture.FullName, Check); }
        finally
        {
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, parent, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f10-shell-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
        return failures;
    }

    private static void RunCases(string root, Action<string, bool> check)
    {
        string desktop = Directory.CreateDirectory(Path.Combine(root, "Desktop")).FullName;
        string launcher = Path.Combine(root, "Launcher fixture.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "where.exe"), launcher);
        const string binding = "旧绑定 #1";
        const string name = "Fixture 动效.lnk";
        string path = Path.Combine(desktop, name);
        string unrelated = Path.Combine(desktop, "unrelated.lnk");
        File.WriteAllText(unrelated, "unrelated");
        ShortcutProof proof;
        byte[] bytes;
        string raced = Path.Combine(desktop, "Raced.lnk");

        using (var platform = WindowsShortcutPlatform.Open(desktop))
        {
            using (var stage = platform.Stage(name, launcher, binding, null))
            {
                proof = stage.Proof;
                check("stage is inert and publication path absent", !File.Exists(path) && Directory.GetFiles(desktop, "*.tmp").Length == 1);
                stage.Publish();
            }

            bytes = File.ReadAllBytes(path);
            ShellLinkTargetReadResult link = ShellLinkTargetReader.Read(path);
            check("actual .lnk contains launcher, exact binding token and working directory",
                link.Status == LaunchTargetResolutionStatus.Resolved && link.ExecutablePath == launcher &&
                link.Arguments is ["--binding", var token] && ManagedLaunchRequest.ParseArguments(["--binding", token]) == binding &&
                link.WorkingDirectory == root && Convert.ToHexString(SHA256.HashData(bytes)) == proof.Sha256);
            using (var observed = platform.Inspect(name, proof))
                check("published file retains recorded identity", observed.State == ShortcutFileState.Owned);
            Directory.CreateDirectory(Path.Combine(desktop, "Folder.lnk"));
            using (var folder = platform.Inspect("Folder.lnk"))
                check("same-name directory is a conflict", folder.State == ShortcutFileState.Conflict);

            using (var colliding = platform.Stage(name, launcher, binding, null))
            {
                check("occupied destination refuses publication",
                    Throws<Win32Exception>(colliding.Publish) && File.ReadAllBytes(path).SequenceEqual(bytes));
            }
            using (var staged = platform.Stage("Raced.lnk", launcher, binding, null))
            {
                File.WriteAllText(raced, "new foreign file");
                check("file arriving after stage is preserved", Throws<Win32Exception>(staged.Publish) &&
                    File.ReadAllText(raced) == "new foreign file");
            }
        }

        string alias = Path.Combine(desktop, "hardlink.lnk");
        if (!CreateHardLinkW(alias, path, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (var platform = WindowsShortcutPlatform.Open(desktop))
        {
            using (var hardlink = platform.Inspect(name, proof, forDelete: true))
                check("existing hard-link count rejects deletion", hardlink.State == ShortcutFileState.Conflict &&
                    File.Exists(path) && File.Exists(alias));
            File.Delete(alias);

            using (var locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            using (var busy = platform.Inspect(name, proof, forDelete: true))
                check("active writer blocks deletion", busy.State == ShortcutFileState.Busy);

            using (var owned = platform.Inspect(name, proof, forDelete: true))
            {
                check("retained handle blocks concurrent replacement", owned.State == ShortcutFileState.Owned &&
                    (Throws<IOException>(() => File.Move(path, Path.Combine(desktop, "Moved.lnk"))) ||
                     Throws<UnauthorizedAccessException>(() => File.Move(path, Path.Combine(desktop, "Moved.lnk")))));
            }
        }

        string moved = Path.Combine(desktop, "Moved.lnk");
        File.Move(path, moved);
        using (var platform = WindowsShortcutPlatform.Open(desktop))
        {
            using (var missing = platform.Inspect(name, proof))
                check("moving the entry leaves its registered name missing", missing.State == ShortcutFileState.Missing);
            File.Copy(moved, path);
            using (var copy = platform.Inspect(name, proof, forDelete: true))
                check("byte-identical substitute has different identity", copy.State == ShortcutFileState.Conflict);
        }
        File.Delete(path);
        File.Move(moved, path);

        using (var platform = WindowsShortcutPlatform.Open(desktop))
        {
            File.AppendAllText(path, "edit");
            using (var edited = platform.Inspect(name, proof, forDelete: true))
                check("in-place edit is foreign", edited.State == ShortcutFileState.Conflict);
            File.WriteAllBytes(path, bytes);
            using (var verified = platform.Inspect(name, proof, forDelete: true))
            {
                check("verified delete targets retained object", verified.State == ShortcutFileState.Owned);
                verified.Delete();
            }
            check("verified removal preserves unrelated bytes", !File.Exists(path) && File.ReadAllText(unrelated) == "unrelated" &&
                File.ReadAllText(raced) == "new foreign file" && Directory.GetFiles(desktop, "*.tmp").Length == 0);
        }

        string redirected = Path.Combine(root, "Redirected");
        F09Checks.CreateJunction(redirected, desktop);
        try
        {
            check("junction in desktop ancestry refused",
                Throws<InvalidDataException>(() => WindowsShortcutPlatform.Open(Path.Combine(redirected, "Child")).Dispose()));
        }
        finally { Directory.Delete(redirected, recursive: false); }
        check("missing desktop is not created",
            Throws<Win32Exception>(() => WindowsShortcutPlatform.Open(Path.Combine(root, "Missing")).Dispose()) &&
            !Directory.Exists(Path.Combine(root, "Missing")));

        string empty = Directory.CreateDirectory(Path.Combine(root, "EmptyPinned")).FullName;
        string outside = Directory.CreateDirectory(Path.Combine(root, "Outside")).FullName;
        bool converted = false;
        try
        {
            using var pinned = WindowsShortcutPlatform.Open(empty);
            converted = SetJunctionWithAttributeAccess(empty, outside);
            check("attribute-only writer cannot redirect retained-root staging",
                converted && Throws<Win32Exception>(() => pinned.Stage(name, launcher, binding, null).Dispose()) &&
                !Directory.GetFileSystemEntries(outside).Any());
        }
        finally { if (converted) Directory.Delete(empty, recursive: false); }
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    private static bool SetJunctionWithAttributeAccess(string directory, string target)
    {
        using var handle = CreateFileW(directory, 0x100, 7, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid) return false;
        byte[] substitute = Encoding.Unicode.GetBytes("\\??\\" + target);
        byte[] display = Encoding.Unicode.GetBytes(target);
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        writer.Write(0xA0000003u);
        writer.Write((ushort)(12 + substitute.Length + display.Length));
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)substitute.Length);
        writer.Write((ushort)(substitute.Length + 2));
        writer.Write((ushort)display.Length);
        writer.Write(substitute);
        writer.Write((ushort)0);
        writer.Write(display);
        writer.Write((ushort)0);
        byte[] payload = bytes.ToArray();
        return DeviceIoControl(handle, 0x900A4, payload, (uint)payload.Length, 0, 0, out _, 0);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string name, string target, nint reserved);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code, byte[] input, uint inputSize, nint output, uint outputSize,
        out uint returned, nint overlapped);
}
