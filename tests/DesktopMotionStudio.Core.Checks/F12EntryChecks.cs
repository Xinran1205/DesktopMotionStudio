using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using DesktopMotionStudio.Core.Distribution;
using DesktopMotionStudio.Core.IO;
using DesktopMotionStudio.Core.Shortcuts;

internal static class F12EntryChecks
{
    private const string TraceVariable = "DMS_F12_ENTRY_CHECK_TRACE";
    private const string Harness = "DesktopMotionStudio.Core.Checks";

    internal static int Run()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f12-entry-");
        int failures = 0;
        void Check(string name, bool pass) => F09Checks.Check("F12 entry " + name, pass, ref failures);
        try
        {
            string root = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Programs", InstalledLayout.ProductDirectory)).FullName;
            string first = Stage(root, "1.0.0", out string hash);
            string second = Stage(root, "1.0.1", out _);
            string selector = Path.Combine(root, InstalledEntry.Active);
            string previous = Path.Combine(root, InstalledEntry.Previous);
            var a = new InstalledSelection(new Version(1, 0, 0), hash, hash);
            var b = a with { Version = new Version(1, 0, 1) };
            File.WriteAllBytes(selector, a.Encode());
            string[] binding = ["--binding", ManagedLaunchRequest.EncodeBindingId("测试 version A/B")!];
            Check("running A receives exact canonical token and working directory", Observe(root, first, binding));
            string[] test = [TestLaunchProtocol.Command, binding[1]];
            Check("test command forwards the same token and yields the actual child's typed result",
                Observe(root, first, test, (int)TestLaunchOutcome.OpenedWithoutMotion));
            Check("test command accepts only its exact argument shape and only the Launcher entry",
                !InstalledEntry.AcceptsArguments(false, test) &&
                !InstalledEntry.AcceptsArguments(true, [TestLaunchProtocol.Command, "%bad"]) &&
                !InstalledEntry.AcceptsArguments(true, [.. test, "--extra"]));
            using (var staging = new InstallGate(root))
            {
                bool dispatched = Task.Run(() =>
                {
                    try { return Observe(root, first, binding); }
                    catch (IOException) { return false; }
                }).GetAwaiter().GetResult();
                Check("an open setup transaction does not prevent stable entry dispatch", staging.Entered && dispatched);
            }
            using (var programs = WindowsFileScope.Open(Path.GetDirectoryName(root)!))
            {
                var owner = new InstallOwner(1, Guid.NewGuid(), hash, 0, 0, 0, 0);
                var intent = new RemoveIntent(1, owner, a, null);
                InstallJournal.Write(programs, InstallJournal.RemoveName, intent);
                Check("a published removal intent prevents dispatch",
                    Reject<InvalidDataException>(() => InstalledEntry.Start(root, launcher: true, binding)));
                InstallJournal.Remove(programs, InstallJournal.RemoveName, intent);
            }
            File.Move(selector, previous);
            Check("missing primary runs verified previous version", Observe(root, first, binding));
            File.WriteAllBytes(selector, b.Encode());
            Check("after commit the same entry starts version B", Observe(root, second, binding));
            Check("a valid generation uses the stable Launcher path",
                InstalledLayout.LauncherForBundle(Path.GetDirectoryName(second)!, root) ==
                Path.Combine(root, "Launcher", InstalledLayout.LauncherName));
            string development = Path.Combine(root, "Development", "1.0.1");
            Check("a development bundle uses its bundled Launcher",
                InstalledLayout.LauncherForBundle(development, root) == Path.Combine(development, "Launcher", InstalledLayout.LauncherName));
            Check("malformed binding is rejected before opening the install root", Reject<ArgumentException>(
                () => InstalledEntry.Start(Path.Combine(root, "missing"), launcher: true, ["--binding", "%bad"])));
            Check("Studio forwarding rejects unexpected parameters", Reject<ArgumentException>(
                () => InstalledEntry.Start(root, launcher: false, binding)));

            File.WriteAllText(selector, "future or corrupt");
            Check("malformed primary does not fall back to an old executable",
                Reject<InvalidDataException>(() => InstalledEntry.Start(root, launcher: true, binding)));
            File.WriteAllBytes(selector, b.Encode());
            File.AppendAllText(Path.Combine(second, InstalledLayout.LauncherName), "foreign");
            Check("modified executable is refused even when the old version is present",
                Reject<InvalidDataException>(() => InstalledEntry.Start(root, launcher: true, binding)));
            File.Delete(Path.Combine(second, InstalledLayout.LauncherName));
            Check("missing selected executable never falls back to another version",
                Reject<Win32Exception>(() => InstalledEntry.Start(root, launcher: true, binding)));
            Check("version field rejects path escape and noncanonical versions",
                InstalledSelection.ParseVersion("../1.0.0") is null &&
                InstalledSelection.ParseVersion("01.0.0") is null && InstalledSelection.ParseVersion("1.0.0.0") is null);
        }
        finally
        {
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, temp, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f12-entry-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
        return failures;
    }

    internal static bool TryObserve(string token)
    {
        string? trace = Environment.GetEnvironmentVariable(TraceVariable);
        if (trace is null || Path.GetFileName(trace) != "trace.txt" ||
            !string.Equals(Path.GetDirectoryName(trace), Path.GetDirectoryName(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase))
            return false;
        File.WriteAllLines(trace, [token, Environment.CurrentDirectory]);
        return true;
    }

    private static string Stage(string root, string version, out string hash)
    {
        string directory = Directory.CreateDirectory(Path.Combine(root, InstalledLayout.Versions, version, "Launcher")).FullName;
        foreach (string name in new[] { Harness + ".dll", Harness + ".deps.json", Harness + ".runtimeconfig.json", "DesktopMotionStudio.Core.dll" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(directory, name));
        string apphost = Path.Combine(directory, InstalledLayout.LauncherName);
        File.Copy(Path.Combine(AppContext.BaseDirectory, Harness + ".exe"), apphost);
        using var file = File.OpenRead(apphost);
        hash = Convert.ToHexString(SHA256.HashData(file));
        return directory;
    }

    private static bool Observe(string root, string generation, string[] binding, int expectedExit = 0)
    {
        string trace = Path.Combine(generation, "trace.txt");
        File.Delete(trace);
        string? previous = Environment.GetEnvironmentVariable(TraceVariable);
        Environment.SetEnvironmentVariable(TraceVariable, trace);
        try
        {
            using Process process = InstalledEntry.Start(root, launcher: true, binding);
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                process.WaitForExit();
                return false;
            }
            return process.ExitCode == expectedExit && File.Exists(trace) && File.ReadAllLines(trace).SequenceEqual([binding[1], generation]);
        }
        finally { Environment.SetEnvironmentVariable(TraceVariable, previous); }
    }

    private static bool Reject<T>(Func<Process> start) where T : Exception
    {
        try { using Process unexpected = start(); return false; }
        catch (T) { return true; }
    }
}
