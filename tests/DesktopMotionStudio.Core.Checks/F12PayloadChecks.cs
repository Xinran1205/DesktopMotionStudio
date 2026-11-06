using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopMotionStudio.Core.Distribution;
using DesktopMotionStudio.Core.IO;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Shortcuts;

internal static class F12PayloadChecks
{
    private const string Studio = "DesktopMotionStudio.Studio.exe";
    private static readonly string[] PayloadFiles =
    [
        Studio, "Launcher/DesktopMotionStudio.Launcher.exe", "MotionHost/DesktopMotionStudio.MotionHost.exe",
        "hostfxr.dll", "Launcher/hostfxr.dll", "MotionHost/hostfxr.dll",
        "coreclr.dll", "Launcher/coreclr.dll", "MotionHost/coreclr.dll"
    ];

    internal static int Run()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        return InFixture(root =>
        {
            int failures = 0;
            void Check(string name, bool pass) => F09Checks.Check("F12 payload " + name, pass, ref failures);
            var valid = Package(PayloadFiles);
            string good = NewDirectory("good");
            InstalledSelection selected = Stage(good, valid);
            Check("valid files stage in their original layout", selected.Version == new Version(0, 12, 0) &&
                PayloadFiles.All(path => File.ReadAllText(Path.Combine(good, path)) == path));
            Check("selector contains exact verified apphost digest", selected.StudioHash ==
                Digest(Encoding.UTF8.GetBytes(Studio)));

            string wrongArchive = NewDirectory("archive");
            var tampered = (valid.Manifest, valid.Archive.ToArray());
            tampered.Item2[^1] ^= 1;
            Check("archive mismatch writes no files", Reject<InvalidDataException>(() => Stage(wrongArchive, tampered)) &&
                !Directory.EnumerateFileSystemEntries(wrongArchive).Any());

            string escape = NewDirectory("escape");
            var traversal = Package([.. PayloadFiles, "../foreign.txt"]);
            Check("path traversal is refused before extraction", Reject<InvalidDataException>(() => Stage(escape, traversal)) &&
                !File.Exists(Path.Combine(root, "foreign.txt")) && !Directory.EnumerateFileSystemEntries(escape).Any());

            string collision = NewDirectory("collision");
            var duplicate = Package([.. PayloadFiles, Studio.ToLowerInvariant()]);
            Check("case-insensitive inventory aliases are refused", Reject<InvalidDataException>(() => Stage(collision, duplicate)) &&
                !Directory.EnumerateFileSystemEntries(collision).Any());
            File.WriteAllText(Path.Combine(collision, Studio), "foreign");
            Check("existing file is preserved", Reject<Win32Exception>(() => Stage(collision, valid)) &&
                File.ReadAllText(Path.Combine(collision, Studio)) == "foreign");

            string redirected = NewDirectory("redirected");
            string outside = NewDirectory("outside");
            string alias = Path.Combine(redirected, "Launcher");
            string outsideSentinel = Path.Combine(outside, "other.txt");
            File.WriteAllText(outsideSentinel, "foreign");
            F09Checks.CreateJunction(alias, outside);
            try
            {
                Check("nested junction cannot redirect extraction", Reject<InvalidDataException>(() => Stage(redirected, valid)) &&
                    Directory.EnumerateFileSystemEntries(outside).SequenceEqual([outsideSentinel]) &&
                    File.ReadAllText(outsideSentinel) == "foreign");
            }
            finally { Directory.Delete(alias, recursive: false); }

            string partial = NewDirectory("partial");
            var incorrectDigest = Package(PayloadFiles, Studio);
            Check("failed file hash removes only the unverified stage", Reject<InvalidDataException>(() => Stage(partial, incorrectDigest)) &&
                !File.Exists(Path.Combine(partial, Studio)));

            string omitted = NewDirectory("omitted");
            Check("missing required child runtime is refused", Reject<InvalidDataException>(() => Stage(omitted, Package(PayloadFiles[..^1]))) &&
                !Directory.EnumerateFileSystemEntries(omitted).Any());

            string programs = NewDirectory("programs");
            string installRoot = Path.Combine(programs, InstalledLayout.ProductDirectory);
            using (var parent = WindowsFileScope.Open(programs))
            using (var prepared = Prepare(parent, valid))
            {
                Check("preparation keeps the install root absent", !Directory.Exists(installRoot));
                prepared.Publish();
                using var installed = WindowsFileScope.Open(installRoot);
                Check("published installation remains inactive before commit",
                    Reject<Win32Exception>(() => InstalledEntry.ReadSelection(installed)) &&
                    File.Exists(Path.Combine(installRoot, InstallStage.Next)));
                Check("fixed entries and generation inventory exist before commit",
                    Digest(File.ReadAllBytes(InstalledLayout.LauncherPath(installRoot))) == prepared.EntryHash &&
                    Digest(File.ReadAllBytes(Path.Combine(installRoot, InstalledLayout.StudioName))) == prepared.EntryHash &&
                    File.ReadAllBytes(Path.Combine(installRoot, InstalledLayout.Versions, "0.12.0", InstallStage.Inventory))
                        .SequenceEqual(valid.Manifest));
                string menu = NewDirectory("menu");
                string studio = Path.Combine(installRoot, InstalledLayout.StudioName);
                string linkPath = Path.Combine(menu, InstalledLayout.StartMenuName);
                using (var platform = WindowsShortcutPlatform.Open(menu))
                using (var shortcut = platform.StageDirect(InstalledLayout.StartMenuName, studio))
                {
                    File.WriteAllText(linkPath, "foreign menu entry");
                    Check("a raced Start menu link is preserved", Reject<Win32Exception>(shortcut.Publish) &&
                        File.ReadAllText(linkPath) == "foreign menu entry");
                    File.Delete(linkPath);
                    shortcut.Publish();
                }
                ShellLinkTargetReadResult link = ShellLinkTargetReader.Read(linkPath);
                Check("Start menu link targets the stable Studio entry without arguments",
                    link.Status == LaunchTargetResolutionStatus.Resolved && link.ExecutablePath == studio &&
                    link.Arguments is [] && link.WorkingDirectory == installRoot);
                string active = Path.Combine(installRoot, InstalledEntry.Active);
                File.WriteAllText(active, "foreign");
                Check("activation refuses to replace an unexpected primary",
                    Reject<Win32Exception>(() => InstallStage.Activate(installed, prepared.Selection)) &&
                    File.ReadAllText(active) == "foreign");
                File.Delete(active);
                InstallStage.Activate(installed, prepared.Selection);
                Check("verified selector publication activates the prepared version",
                    InstalledEntry.ReadSelection(installed) == prepared.Selection &&
                    !File.Exists(Path.Combine(installRoot, InstallStage.Next)));
            }

            string occupied = NewDirectory("occupied");
            using (var parent = WindowsFileScope.Open(occupied))
            using (var prepared = Prepare(parent, valid))
            {
                string foreign = Directory.CreateDirectory(Path.Combine(occupied, InstalledLayout.ProductDirectory)).FullName;
                string sentinel = Path.Combine(foreign, "other.txt");
                File.WriteAllText(sentinel, "foreign");
                Check("program publication refuses a newly occupied root",
                    Reject<Win32Exception>(prepared.Publish) && File.ReadAllText(sentinel) == "foreign" &&
                    !File.Exists(Path.Combine(foreign, InstalledEntry.Active)));
            }

            string NewDirectory(string name) => Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            return failures;
        });
    }

    internal static int RunBuiltPayload(string output)
    {
        return InFixture(root =>
        {
            using var manifest = File.OpenRead(Path.Combine(output, "payload.manifest.json"));
            using var archive = File.OpenRead(Path.Combine(output, "payload.zip"));
            using var scope = WindowsFileScope.Open(root);
            InstalledSelection selected = InstallPayload.Read(manifest).Stage(scope, archive);
            if (InstalledSelection.ParseVersion(selected.Version.ToString()) is null) return 1;
            Console.WriteLine($"PASS F12 staged {Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count()} verified package files");
            return 0;
        });
    }

    internal static int RunBuiltStage(string output, string entryImage)
    {
        return InFixture(root =>
        {
            using var manifest = File.OpenRead(Path.Combine(output, "payload.manifest.json"));
            using var archive = File.OpenRead(Path.Combine(output, "payload.zip"));
            using var entry = File.OpenRead(entryImage);
            using var programs = WindowsFileScope.Open(root);
            using InstallStage prepared = InstallStage.Prepare(programs, manifest, archive, entry);
            string install = Path.Combine(root, InstalledLayout.ProductDirectory);
            if (Directory.Exists(install)) return 1;
            prepared.Publish();
            using var installed = WindowsFileScope.Open(install);
            if (!Reject<Win32Exception>(() => InstalledEntry.ReadSelection(installed))) return 1;
            string generation = Path.Combine(install, InstalledLayout.Versions, prepared.Selection.Version.ToString(3));
            int files = Directory.EnumerateFiles(generation, "*", SearchOption.AllDirectories).Count();
            Console.WriteLine($"PASS F12 published {files - 1} verified files plus inventory in an inactive install root");
            InstallStage.Activate(installed, prepared.Selection);
            if (InstalledEntry.ReadSelection(installed) != prepared.Selection) return 1;
            Console.WriteLine("PASS F12 published selector activates the complete program generation");
            foreach (string exe in new[] { Path.Combine(install, InstalledLayout.StudioName), InstalledLayout.LauncherPath(install) })
            {
                var start = new ProcessStartInfo(exe, "--invalid") { UseShellExecute = false };
                start.Environment["DOTNET_ROOT"] = Path.Combine(root, "no-runtime");
                start.Environment["DOTNET_ROOT_X64"] = start.Environment["DOTNET_ROOT"];
                start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(root, "runtime-cache");
                using Process process = Process.Start(start) ?? throw new IOException("Could not start the packaged entry.");
                if (!process.WaitForExit(8000))
                {
                    process.Kill();
                    process.WaitForExit();
                    return 1;
                }
                if (process.ExitCode != 2) return 1;
            }
            Console.WriteLine("PASS F12 both packaged entry copies reject non-install paths without accessing a profile");
            return 0;
        });
    }

    private static int InFixture(Func<string, int> check)
    {
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f12-payload-test-");
        try { return check(fixture.FullName); }
        finally
        {
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, temp, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f12-payload-test-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
    }

    private static InstalledSelection Stage(string folder, (byte[] Manifest, byte[] Archive) package)
    {
        using var manifest = new MemoryStream(package.Manifest);
        using var archive = new MemoryStream(package.Archive);
        using var scope = WindowsFileScope.Open(folder);
        return InstallPayload.Read(manifest).Stage(scope, archive);
    }

    private static InstallStage Prepare(WindowsFileScope parent, (byte[] Manifest, byte[] Archive) package)
    {
        using var manifest = new MemoryStream(package.Manifest);
        using var archive = new MemoryStream(package.Archive);
        using var stub = new MemoryStream("MZ test entry"u8.ToArray());
        return InstallStage.Prepare(parent, manifest, archive, stub);
    }

    internal static (byte[] Manifest, byte[] Archive) Package(string[]? paths = null, string? wrongHash = null, string version = "0.12.0")
    {
        paths ??= PayloadFiles;
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string path in paths)
            {
                ZipArchiveEntry item = zip.CreateEntry(path);
                item.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var file = item.Open();
                file.Write(Encoding.UTF8.GetBytes(path));
            }
        }
        byte[] archive = output.ToArray();
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            productVersion = version,
            runtimeIdentifier = "win-x64",
            archiveSha256 = Digest(archive),
            frameworks = new { },
            files = paths.Select(path => new
            {
                path,
                bytes = Encoding.UTF8.GetByteCount(path),
                sha256 = path == wrongHash ? new string('0', 64) : Digest(Encoding.UTF8.GetBytes(path))
            })
        });
        return (manifest, archive);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static bool Reject<T>(Action extract) where T : Exception
    {
        try { extract(); return false; }
        catch (T) { return true; }
    }
}
