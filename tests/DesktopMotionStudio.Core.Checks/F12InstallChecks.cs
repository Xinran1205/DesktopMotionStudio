using System.Diagnostics;
using System.Runtime.Versioning;
using DesktopMotionStudio.Core.Distribution;
using DesktopMotionStudio.Core.Shortcuts;
using Microsoft.Win32;

internal static class F12InstallChecks
{
    private const string Prefix = "dms-f12-install-check-";

    internal static int Run() => OperatingSystem.IsWindows() ? RunWindows() : 0;
    internal static int RunRemoval() => OperatingSystem.IsWindows() ? RunRemovalWindows() : 0;

    internal static int RunBuiltLifecycle(string first, string second) =>
        OperatingSystem.IsWindows() ? RunBuiltLifecycleWindows(first, second) : 2;

    [SupportedOSPlatform("windows")]
    private static int RunBuiltLifecycleWindows(string first, string second)
    {
        int failures = 0;
        void Check(string name, bool result) => F09Checks.Check("F12 built " + name, result, ref failures);
        string versionA = ReadVersion(first);
        string versionB = ReadVersion(second);
        WithFixture((directory, registry) =>
        {
            InstallCoordinator setup = new(Path.Combine(directory, "Programs"), Path.Combine(directory, "Menu"), new InstallRegistration(registry));
            string root = InstallRoot(directory);
            string profile = Path.Combine(directory, "profile.json");
            File.WriteAllText(profile, "retained profile");
            Check("real package A installs", InstallBuilt(first) == InstallOutcome.Installed && RegistrationMatches(directory, registry, versionA));
            string shortcut = CreateManagedEntry(directory);
            string active = Path.Combine(root, InstalledEntry.Active);
            byte[] previous = File.ReadAllBytes(active);
            byte[] entry = File.ReadAllBytes(InstalledLayout.LauncherPath(root));
            byte[] link = File.ReadAllBytes(shortcut);
            bool failed = false;
            try { InstallBuilt(second, step => { if (step == InstallStep.UpgradeRetired) throw new OperationCanceledException(); }); }
            catch (OperationCanceledException) { failed = true; }
            Check("real package B rollback retains old selector, registration and stable entry", failed &&
                File.ReadAllBytes(active).SequenceEqual(previous) && RegistrationMatches(directory, registry, versionA) &&
                File.ReadAllBytes(InstalledLayout.LauncherPath(root)).SequenceEqual(entry));
            Check("real package B commits and stale A is refused", InstallBuilt(second) == InstallOutcome.Upgraded &&
                InstallBuilt(first) == InstallOutcome.IncompatibleVersion && RegistrationMatches(directory, registry, versionB));
            Check("real upgrade preserves Shell Link bytes and installs both new images at stable paths",
                File.ReadAllBytes(shortcut).SequenceEqual(link) &&
                File.ReadAllBytes(InstalledLayout.LauncherPath(root)).SequenceEqual(
                    File.ReadAllBytes(Path.Combine(second, "entry", "DesktopMotionStudio.Entry.exe"))) &&
                File.ReadAllBytes(Path.Combine(root, InstalledLayout.StudioName)).SequenceEqual(
                    File.ReadAllBytes(Path.Combine(second, "entry", "DesktopMotionStudio.Entry.exe"))));
            UninstallResult result = Uninstall(directory, registry);
            Check("real payload files, registration and owned links are removed", !result.Busy && result.PreservedProgramItems == 0 &&
                result.Shortcuts.Removed == 1 && !Directory.Exists(root) && !File.Exists(shortcut) &&
                !File.Exists(MenuLink(directory)) && !new InstallRegistration(registry).Exists());
            Check("real reinstall leaves retained profile readable", InstallBuilt(second) == InstallOutcome.Installed &&
                File.ReadAllText(profile) == "retained profile");

            InstallOutcome InstallBuilt(string path, Action<InstallStep>? checkpoint = null)
            {
                using var manifest = File.OpenRead(Path.Combine(path, "payload.manifest.json"));
                using var archive = File.OpenRead(Path.Combine(path, "payload.zip"));
                using var stub = File.OpenRead(Path.Combine(path, "entry", "DesktopMotionStudio.Entry.exe"));
                return setup.Install(manifest, archive, stub, checkpoint);
            }
        });
        return failures;

        static string ReadVersion(string package)
        {
            using var manifest = File.OpenRead(Path.Combine(package, "payload.manifest.json"));
            return InstallPayload.Read(manifest).Version.ToString(3);
        }
    }

    [SupportedOSPlatform("windows")]
    private static int RunRemovalWindows()
    {
        int failures = 0;
        void Check(string name, bool result) => F09Checks.Check("F12 uninstall " + name, result, ref failures);
        foreach (RemoveStep step in Enum.GetValues<RemoveStep>())
        {
            WithFixture((directory, registry) =>
            {
                if (Install(directory, registry) != InstallOutcome.Installed ||
                    Install(directory, registry, version: "0.12.1") != InstallOutcome.Upgraded)
                    throw new InvalidOperationException("Could not prepare uninstall fixture.");
                string profile = Path.Combine(directory, "profile.json");
                File.WriteAllText(profile, "original profile");
                string managed = CreateManagedEntry(directory);
                using Process worker = StartWorker(directory, step, "--f12-uninstall-worker");
                try
                {
                    string? observed = worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                    if (observed != step.ToString()) throw new InvalidOperationException($"Uninstall worker did not reach {step}: {observed}.");
                    worker.Kill();
                    if (!worker.WaitForExit(5000)) throw new TimeoutException("Interrupted uninstall worker did not exit.");
                }
                finally { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); } }
                Check($"{step} handles desktop links only after registration removal",
                    File.Exists(managed) == (step != RemoveStep.Shortcuts) &&
                    RegistrationMatches(directory, registry, "0.12.1") == (step < RemoveStep.Registration));
                Check($"{step} blocks new setup until removal recovery", Install(directory, registry) == InstallOutcome.RemovalPending);
                UninstallResult result = Uninstall(directory, registry);
                if (Directory.Exists(InstallRoot(directory)))
                    Console.WriteLine("  retained program paths: " + string.Join(", ", Directory.EnumerateFileSystemEntries(
                        InstallRoot(directory), "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(InstallRoot(directory), path))));
                Console.WriteLine($"  removal {result}, managed={File.Exists(managed)}, menu={File.Exists(MenuLink(directory))}, registration={new InstallRegistration(registry).Exists()}");
                Check($"{step} resumes and removes the program, link and registration",
                    !result.Busy && result.PreservedProgramItems == 0 && !Directory.Exists(InstallRoot(directory)) &&
                    !File.Exists(managed) && !File.Exists(MenuLink(directory)) && !new InstallRegistration(registry).Exists());
                Check($"{step} reinstall succeeds and profile remains exact", Install(directory, registry) == InstallOutcome.Installed &&
                    File.ReadAllText(profile) == "original profile");
            });
        }
        WithFixture((directory, registry) =>
        {
            if (Install(directory, registry) != InstallOutcome.Installed) throw new InvalidOperationException("Could not install fixture.");
            string root = InstallRoot(directory);
            string oldExe = Path.Combine(root, InstalledLayout.Versions, "0.12.0", InstalledLayout.StudioName);
            using (var running = File.Open(oldExe, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool denied = false;
                try { Uninstall(directory, registry); }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 32 or 33) { denied = true; }
                Check("occupied installed executable prevents uninstall commit", denied && RegistrationMatches(directory, registry) &&
                    !File.Exists(Path.Combine(directory, "Programs", InstallJournal.RemoveName)));
            }
            string outside = Path.Combine(root, "foreign.txt");
            File.WriteAllText(outside, "foreign");
            File.AppendAllText(MenuLink(directory), "edited menu");
            byte[] foreignMenu = File.ReadAllBytes(MenuLink(directory));
            UninstallResult result = Uninstall(directory, registry);
            Check("uninstall preserves foreign files and edited Start menu bytes", result.PreservedProgramItems > 0 &&
                File.ReadAllText(outside) == "foreign" && File.ReadAllBytes(MenuLink(directory)).SequenceEqual(foreignMenu) &&
                !new InstallRegistration(registry).Exists());
        });
        WithFixture((directory, registry) =>
        {
            if (Install(directory, registry) != InstallOutcome.Installed ||
                Install(directory, registry, version: "0.12.1") != InstallOutcome.Upgraded)
                throw new InvalidOperationException("Could not prepare old-generation lock fixture.");
            string root = InstallRoot(directory);
            Guid oldOwner;
            using (var scope = DesktopMotionStudio.Core.IO.WindowsFileScope.Open(root))
                oldOwner = InstallCoordinator.ReadOwnerRecord(scope).Id;
            string oldExe = Path.Combine(root, InstalledLayout.Versions, "0.12.0", InstalledLayout.StudioName);
            using (var running = File.Open(oldExe, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool denied = false;
                try { Uninstall(directory, registry); }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 32 or 33) { denied = true; }
                Check("an occupied previous generation also blocks removal", denied && RegistrationMatches(directory, registry, "0.12.1"));
            }
            if (Uninstall(directory, registry).PreservedProgramItems != 0 || Install(directory, registry) != InstallOutcome.Installed)
                throw new InvalidOperationException("Could not reinstall stale-worker fixture.");
            Check("stale worker cannot remove a reinstalled owner", RejectRemoval(() =>
                Uninstall(directory, registry, expectedOwner: oldOwner)) && RegistrationMatches(directory, registry));
            Check("resume cannot initiate removal after the pending operation is gone", RejectRemoval(() =>
                Uninstall(directory, registry, resumeOnly: true)) && RegistrationMatches(directory, registry));
            using (RegistryKey? edited = registry.OpenSubKey("DesktopMotionStudio", writable: true)) edited!.SetValue("DisplayName", "Modified");
            Check("edited registered values prevent removal before any program deletion",
                RejectRemoval(() => Uninstall(directory, registry)) && File.Exists(Path.Combine(root, InstalledLayout.StudioName)));
        });
        WithFixture((directory, registry) =>
        {
            if (Install(directory, registry) != InstallOutcome.Installed) throw new InvalidOperationException("Could not prepare replacement folder.");
            string root = InstallRoot(directory);
            string nested = Path.Combine(root, InstalledLayout.Versions, "0.12.0", "Launcher");
            Directory.Move(nested, Path.Combine(directory, "original-launcher"));
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(root, InstallJournal.MenuName), "{invalid");
            UninstallResult result = Uninstall(directory, registry);
            Check("a same-name replacement directory is preserved", result.PreservedProgramItems > 0 && Directory.Exists(nested));
            Check("a corrupt menu receipt is preserved without trapping uninstall in recovery", !new InstallRegistration(registry).Exists() &&
                File.ReadAllText(Path.Combine(root, InstallJournal.MenuName)) == "{invalid" &&
                !File.Exists(Path.Combine(directory, "Programs", InstallJournal.RemoveName)));
        });
        WithFixture((directory, registry) =>
        {
            if (Install(directory, registry) != InstallOutcome.Installed) throw new InvalidOperationException("Could not prepare a stage.");
            bool stopped = false;
            try { Install(directory, registry, step => { if (step == InstallStep.UpgradeClaimed) throw new OperationCanceledException(); }, "0.12.1"); }
            catch (OperationCanceledException) { stopped = true; }
            Check("uninstall can remove a claimed upgrade interrupted before directory publication", stopped &&
                Uninstall(directory, registry).PreservedProgramItems == 0 && !Directory.Exists(InstallRoot(directory)));
        });
        return failures;
    }

    [SupportedOSPlatform("windows")]
    private static string CreateManagedEntry(string fixture)
    {
        string desktop = Directory.CreateDirectory(Path.Combine(fixture, "Desktop")).FullName;
        using var platform = WindowsShortcutPlatform.Open(desktop);
        using var stage = platform.Stage("managed.lnk", InstalledLayout.LauncherPath(InstallRoot(fixture)), "test", null);
        var store = new ShortcutReceiptStore(Path.Combine(fixture, "receipts"));
        if (store.Begin("test", out var transaction) != ShortcutReceiptStatus.Ok)
            throw new IOException("Could not create managed shortcut receipt.");
        using (transaction)
            if (transaction!.Save(new(1, "test", stage.Proof)) != ShortcutReceiptStatus.Ok) throw new IOException("Receipt rejected.");
        stage.Publish();
        return stage.Proof.Path;
    }

    [SupportedOSPlatform("windows")]
    private static int RunWindows()
    {
        int failures = 0;
        void Check(string name, bool result) => F09Checks.Check("F12 setup " + name, result, ref failures);

        foreach (InstallStep step in new[]
        {
            InstallStep.DirectoryPublished, InstallStep.MenuPrepared, InstallStep.MenuPublished,
            InstallStep.RegistrationClaimed, InstallStep.Registered
        })
        {
            WithFixture((directory, registry) =>
            {
                using Process worker = StartWorker(directory, step);
                try
                {
                    string? observed = worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                    if (observed != step.ToString()) throw new InvalidOperationException($"Setup worker did not reach {step}: {observed}.");
                    worker.Kill();
                    if (!worker.WaitForExit(5000)) throw new TimeoutException("Interrupted setup worker did not exit.");
                }
                finally { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); } }

                string active = Path.Combine(InstallRoot(directory), InstalledEntry.Active);
                Check($"{step} leaves no active program before registration completes", !File.Exists(active));
                Check($"{step} resumes from durable evidence", Install(directory, registry) == InstallOutcome.Installed &&
                    File.Exists(active) && RegistrationMatches(directory, registry) && File.Exists(MenuLink(directory)));
                byte[] link = File.ReadAllBytes(MenuLink(directory));
                byte[] before = File.ReadAllBytes(active);
                Check($"{step} repeat preserves link and active bytes", Install(directory, registry) == InstallOutcome.AlreadyInstalled &&
                    File.ReadAllBytes(MenuLink(directory)).SequenceEqual(link) &&
                    File.ReadAllBytes(active).SequenceEqual(before));
            });
        }

        WithFixture((directory, registry) =>
        {
            using (RegistryKey foreign = registry.CreateSubKey("DesktopMotionStudio"))
                foreign.SetValue("DisplayName", "Foreign");
            Check("foreign registration prevents install", Reject(() => Install(directory, registry)) &&
                !Directory.Exists(InstallRoot(directory)) && !File.Exists(MenuLink(directory)));
            using RegistryKey? retained = registry.OpenSubKey("DesktopMotionStudio");
            Check("foreign registration retains original value", retained?.GetValue("DisplayName") as string == "Foreign");
        });
        WithFixture((directory, registry) =>
        {
            string foreign = MenuLink(directory);
            File.WriteAllText(foreign, "Foreign menu");
            Check("foreign Start menu file prevents install", Reject(() => Install(directory, registry)) &&
                File.ReadAllText(foreign) == "Foreign menu" && !Directory.Exists(InstallRoot(directory)));
        });
        WithFixture((directory, registry) =>
        {
            Check("registration race keeps the root inactive", Reject(() => Install(directory, registry, step =>
            {
                if (step != InstallStep.MenuPublished) return;
                using RegistryKey foreign = registry.CreateSubKey("DesktopMotionStudio");
                foreign.SetValue("DisplayName", "Raced");
            })) && !File.Exists(Path.Combine(InstallRoot(directory), InstalledEntry.Active)));
            using RegistryKey? retained = registry.OpenSubKey("DesktopMotionStudio");
            Check("raced registry owner remains intact", retained?.GetValue("DisplayName") as string == "Raced");
        });
        foreach (InstallStep step in new[] { InstallStep.UpgradePrepared, InstallStep.UpgradeRetired, InstallStep.UpgradeActivated })
        {
            WithFixture((directory, registry) =>
            {
                const string newVersion = "0.12.1";
                if (Install(directory, registry) != InstallOutcome.Installed) throw new InvalidOperationException("Old version did not install.");
                string root = InstallRoot(directory);
                string profile = Path.Combine(directory, "profile.json");
                File.WriteAllText(profile, "unchanged profile");
                byte[] launcher = File.ReadAllBytes(InstalledLayout.LauncherPath(root));
                byte[] menu = File.ReadAllBytes(MenuLink(directory));
                if (step == InstallStep.UpgradePrepared)
                {
                    string oldExe = Path.Combine(root, InstalledLayout.Versions, "0.12.0", InstalledLayout.StudioName);
                    using (var busy = File.Open(oldExe, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        bool denied = false;
                        try { Install(directory, registry, version: newVersion); }
                        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 32 or 33) { denied = true; }
                        Check("program lock refuses the update before a journal is created", denied &&
                            !File.Exists(Path.Combine(root, InstallJournal.UpgradeName)));
                    }
                    byte[] before = File.ReadAllBytes(Path.Combine(root, InstalledEntry.Active));
                    bool canceled = false;
                    try { Install(directory, registry, step => { if (step == InstallStep.UpgradeRetired) throw new OperationCanceledException(); }, newVersion); }
                    catch (OperationCanceledException) { canceled = true; }
                    Check("controlled failure after retirement restores exact old primary", canceled &&
                        File.ReadAllBytes(Path.Combine(root, InstalledEntry.Active)).SequenceEqual(before) &&
                        RegistrationMatches(directory, registry));
                }
                using (Process worker = StartWorker(directory, step))
                {
                    try
                    {
                        string? observed = worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                        if (observed != step.ToString()) throw new InvalidOperationException($"Upgrade worker did not reach {step}: {observed}.");
                        worker.Kill();
                        if (!worker.WaitForExit(5000)) throw new TimeoutException("Interrupted upgrade worker did not exit.");
                    }
                    finally { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); } }
                }
                using (var scope = DesktopMotionStudio.Core.IO.WindowsFileScope.Open(root))
                {
                    string expected = step == InstallStep.UpgradeActivated ? newVersion : "0.12.0";
                    Check($"{step} selects the expected complete version", InstalledEntry.ReadSelection(scope).Version.ToString(3) == expected);
                }
                Check($"{step} keeps the previous registration until completion", RegistrationMatches(directory, registry));
                Check($"{step} retry commits the new version", Install(directory, registry, version: newVersion) == InstallOutcome.Upgraded &&
                    RegistrationMatches(directory, registry, newVersion));
                Check($"{step} stale setup is refused and stable artifacts remain exact", Install(directory, registry) == InstallOutcome.IncompatibleVersion &&
                    File.ReadAllBytes(InstalledLayout.LauncherPath(root)).SequenceEqual(launcher) &&
                    File.ReadAllBytes(MenuLink(directory)).SequenceEqual(menu) && File.ReadAllText(profile) == "unchanged profile");
            });
        }
        WithFixture((directory, registry) =>
        {
            if (Install(directory, registry) != InstallOutcome.Installed) throw new InvalidOperationException("Could not install fixture.");
            string root = InstallRoot(directory);
            string dependency = Path.Combine(root, InstalledLayout.Versions, "0.12.1", "coreclr.dll");
            byte[] active = File.ReadAllBytes(Path.Combine(root, InstalledEntry.Active));
            Check("activation detects a same-name payload mutation and restores old selector", Reject(() =>
                Install(directory, registry, step =>
                {
                    if (step == InstallStep.UpgradeRetired) File.AppendAllText(dependency, "modified");
                }, "0.12.1")) && File.ReadAllBytes(Path.Combine(root, InstalledEntry.Active)).SequenceEqual(active) &&
                RegistrationMatches(directory, registry));
        });
        WithFixture((directory, registry) =>
        {
            if (Install(directory, registry) != InstallOutcome.Installed ||
                Install(directory, registry, version: "0.12.1") != InstallOutcome.Upgraded)
                throw new InvalidOperationException("Could not install two versions.");
            string root = InstallRoot(directory);
            string versions = Path.Combine(root, InstalledLayout.Versions);
            string unrelated = Directory.CreateDirectory(Path.Combine(versions, "0.11.9")).FullName;
            string unrelatedFile = Path.Combine(unrelated, "foreign.txt");
            File.WriteAllText(unrelatedFile, "unrelated");
            string first = Path.Combine(versions, "0.12.0");
            bool heldAfterFirstUpgrade = Directory.Exists(first);
            Check("third version retires only fully owned programs older than rollback",
                Install(directory, registry, version: "0.12.2") == InstallOutcome.Upgraded &&
                heldAfterFirstUpgrade && !Directory.Exists(first) && File.ReadAllText(unrelatedFile) == "unrelated" &&
                Directory.Exists(Path.Combine(versions, "0.12.1")) && Directory.Exists(Path.Combine(versions, "0.12.2")));

            string older = Path.Combine(versions, "0.12.1");
            string conflict = Path.Combine(older, "foreign.txt");
            File.WriteAllText(conflict, "owned directory, foreign file");
            Check("retirement preserves foreign bytes and proof for retry",
                Install(directory, registry, version: "0.12.3") == InstallOutcome.Upgraded &&
                File.ReadAllText(conflict) == "owned directory, foreign file" &&
                File.Exists(Path.Combine(versions, "0.12.1.receipt.json")));
            File.Delete(conflict);
            Check("same-version retry completes retirement without touching either selected generation",
                Install(directory, registry, version: "0.12.3") == InstallOutcome.AlreadyInstalled &&
                !Directory.Exists(older) && File.ReadAllText(unrelatedFile) == "unrelated" &&
                Directory.Exists(Path.Combine(versions, "0.12.2")) && Directory.Exists(Path.Combine(versions, "0.12.3")));
        });
        WithFixture((directory, registry) =>
        {
            if (Install(directory, registry) != InstallOutcome.Installed) throw new InvalidOperationException("Could not install fixture.");
            string root = InstallRoot(directory);
            string foreign = Directory.CreateDirectory(Path.Combine(root, InstalledLayout.Versions, "0.12.1")).FullName;
            var package = F12PayloadChecks.Package(version: "0.12.1");
            using (var scope = DesktopMotionStudio.Core.IO.WindowsFileScope.Open(foreign))
            using (var manifest = new MemoryStream(package.Manifest))
            using (var archive = new MemoryStream(package.Archive))
                InstallStage.WriteGeneration(scope, InstallPayload.Read(manifest), archive);
            Check("a byte-identical but unclaimed version directory stays foreign", Reject(() =>
                Install(directory, registry, version: "0.12.1")) &&
                File.Exists(Path.Combine(foreign, InstalledLayout.StudioName)) &&
                !File.Exists(Path.Combine(root, InstalledLayout.Versions, "0.12.1.receipt.json")));
        });
        WithFixture((directory, registry) =>
        {
            if (Install(directory, registry) != InstallOutcome.Installed)
                throw new InvalidOperationException("Could not install initial entry images.");
            string root = InstallRoot(directory);
            InstallOwner original;
            using (var scope = DesktopMotionStudio.Core.IO.WindowsFileScope.Open(root))
                original = InstallCoordinator.ReadOwnerRecord(scope);
            byte[] link = File.ReadAllBytes(MenuLink(directory));
            Check("upgrade updates both stable entry images in place and preserves the menu link",
                Install(directory, registry, version: "0.12.1", image: "MZ upgraded fixture") == InstallOutcome.Upgraded &&
                File.ReadAllText(InstalledLayout.LauncherPath(root)) == "MZ upgraded fixture" &&
                File.ReadAllText(Path.Combine(root, InstalledLayout.StudioName)) == "MZ upgraded fixture" &&
                File.ReadAllBytes(MenuLink(directory)).SequenceEqual(link) && RegistrationMatches(directory, registry, "0.12.1"));
            Check("uninstall refuses a request from the superseded entry image",
                RejectRemoval(() => Uninstall(directory, registry, expectedOwner: original.Id, entryHash: original.EntryHash)) &&
                RegistrationMatches(directory, registry, "0.12.1"));
            Check("updated entry ownership permits removal of both stable files",
                Uninstall(directory, registry).PreservedProgramItems == 0 && !Directory.Exists(root));
        });
        foreach (InstallStep interruption in new[]
        {
            InstallStep.EntryStudioRetired, InstallStep.EntryLauncherRetired, InstallStep.EntryOwnerRetired
        })
        {
            WithFixture((directory, registry) =>
            {
                if (Install(directory, registry) != InstallOutcome.Installed)
                    throw new InvalidOperationException("Could not install the entry recovery fixture.");
                using Process worker = StartWorker(directory, interruption);
                try
                {
                    string? observed = worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                    if (observed != interruption.ToString()) throw new InvalidOperationException($"Entry worker missed {interruption}: {observed}.");
                    worker.Kill();
                    if (!worker.WaitForExit(5000)) throw new TimeoutException("Interrupted entry worker did not exit.");
                }
                finally { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); } }

                string root = InstallRoot(directory);
                Check($"{interruption} resumes the committed version and clears swap files",
                    RegistrationMatches(directory, registry, "0.12.1") &&
                    Install(directory, registry, version: "0.12.1", image: "MZ upgraded fixture") == InstallOutcome.Upgraded &&
                    File.ReadAllText(InstalledLayout.LauncherPath(root)) == "MZ upgraded fixture" &&
                    File.ReadAllText(Path.Combine(root, InstalledLayout.StudioName)) == "MZ upgraded fixture" &&
                    !File.Exists(Path.Combine(root, InstallJournal.UpgradeName)) &&
                    !File.Exists(Path.Combine(root, InstallEntryUpgrade.RetiredOwnerName)) &&
                    !File.Exists(InstalledLayout.LauncherPath(root) + ".previous"));
            });
        }
        WithFixture((directory, registry) =>
        {
            if (Install(directory, registry) != InstallOutcome.Installed)
                throw new InvalidOperationException("Could not install the entry race fixture.");
            string launcher = InstalledLayout.LauncherPath(InstallRoot(directory));
            bool conflict = false;
            try
            {
                Install(directory, registry, step =>
                {
                    if (step == InstallStep.EntryLauncherRetired) File.WriteAllText(launcher, "foreign winner");
                }, "0.12.1", "MZ upgraded fixture");
            }
            catch (System.ComponentModel.Win32Exception) { conflict = true; }
            Check("a file arriving in the stable path is preserved during entry promotion and recovery",
                conflict && File.ReadAllText(launcher) == "foreign winner" &&
                Reject(() => Install(directory, registry, version: "0.12.1", image: "MZ upgraded fixture")) &&
                File.ReadAllText(launcher) == "foreign winner");
            File.Delete(launcher);
            Check("entry recovery can continue after the fixture removes its own conflict",
                Install(directory, registry, version: "0.12.1", image: "MZ upgraded fixture") == InstallOutcome.Upgraded &&
                File.ReadAllText(launcher) == "MZ upgraded fixture");
        });
        WithFixture((directory, registry) =>
        {
            if (Install(directory, registry) != InstallOutcome.Installed)
                throw new InvalidOperationException("Could not install the Launcher directory identity fixture.");
            string root = InstallRoot(directory);
            string launcher = InstalledLayout.LauncherPath(root);
            string launcherFolder = Path.GetDirectoryName(launcher)!;
            string retained = Path.Combine(directory, "original-launcher");
            Directory.Move(launcherFolder, retained);
            Directory.CreateDirectory(launcherFolder);
            File.Copy(Path.Combine(retained, InstalledLayout.LauncherName), launcher);
            byte[] before = File.ReadAllBytes(Path.Combine(root, InstalledEntry.Active));
            Check("a byte-identical entry in a replaced Launcher directory is refused before commit",
                Reject(() => Install(directory, registry, version: "0.12.1", image: "MZ upgraded fixture")) &&
                RegistrationMatches(directory, registry) &&
                File.ReadAllBytes(Path.Combine(root, InstalledEntry.Active)).SequenceEqual(before) &&
                File.ReadAllText(launcher) == "MZ install fixture");
        });
        return failures;
    }

    internal static int Worker(string directory, string interrupt)
    {
        if (!OperatingSystem.IsWindows() || !Enum.TryParse(interrupt, out InstallStep expected) || !IsFixture(directory)) return 2;
        using RegistryKey? registry = Registry.CurrentUser.OpenSubKey(RegistryPath(directory), writable: true);
        if (registry is null) return 2;
        Install(directory, registry, step =>
        {
            if (step != expected) return;
            Console.WriteLine(step);
            Console.ReadLine();
        }, expected >= InstallStep.UpgradeClaimed ? "0.12.1" : "0.12.0",
            expected >= InstallStep.EntryStudioRetired ? "MZ upgraded fixture" : "MZ install fixture");
        return 3;
    }

    internal static int RemoveWorker(string directory, string interrupt)
    {
        if (!OperatingSystem.IsWindows() || !Enum.TryParse(interrupt, out RemoveStep expected) || !IsFixture(directory)) return 2;
        using RegistryKey? registry = Registry.CurrentUser.OpenSubKey(RegistryPath(directory), writable: true);
        if (registry is null) return 2;
        Uninstall(directory, registry, step =>
        {
            if (step != expected) return;
            Console.WriteLine(step);
            Console.ReadLine();
        });
        return 3;
    }

    [SupportedOSPlatform("windows")]
    private static UninstallResult Uninstall(string directory, RegistryKey registry, Action<RemoveStep>? checkpoint = null,
        Guid? expectedOwner = null, bool resumeOnly = false, string? entryHash = null) =>
        new UninstallCoordinator(Path.Combine(directory, "Programs"), Path.Combine(directory, "Menu"), new InstallRegistration(registry),
            Path.Combine(directory, "receipts"), () => Path.Combine(directory, "Desktop")).Run(checkpoint, expectedOwner, resumeOnly, entryHash);

    private static bool RejectRemoval(Func<UninstallResult> remove)
    {
        try { remove(); return false; }
        catch (InvalidDataException) { return true; }
    }

    [SupportedOSPlatform("windows")]
    private static InstallOutcome Install(string directory, RegistryKey registry, Action<InstallStep>? checkpoint = null,
        string version = "0.12.0", string image = "MZ install fixture")
    {
        var package = F12PayloadChecks.Package(version: version);
        using var manifest = new MemoryStream(package.Manifest);
        using var archive = new MemoryStream(package.Archive);
        using var entry = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(image));
        return new InstallCoordinator(Path.Combine(directory, "Programs"), Path.Combine(directory, "Menu"),
            new InstallRegistration(registry)).Install(manifest, archive, entry, checkpoint);
    }

    [SupportedOSPlatform("windows")]
    private static bool RegistrationMatches(string directory, RegistryKey registry, string version = "0.12.0")
    {
        using RegistryKey? key = registry.OpenSubKey("DesktopMotionStudio");
        return key?.GetValue("DisplayVersion") as string == version &&
            key.GetValue("UninstallString") as string == $"\"{Path.Combine(InstallRoot(directory), InstalledLayout.StudioName)}\" --uninstall";
    }

    [SupportedOSPlatform("windows")]
    private static void WithFixture(Action<string, RegistryKey> run)
    {
        DirectoryInfo fixture = Directory.CreateTempSubdirectory(Prefix);
        if (!IsFixture(fixture.FullName)) throw new InvalidOperationException("Invalid test fixture.");
        string path = RegistryPath(fixture.FullName);
        try
        {
            Directory.CreateDirectory(Path.Combine(fixture.FullName, "Menu"));
            using RegistryKey registry = Registry.CurrentUser.CreateSubKey(path);
            run(fixture.FullName, registry);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            fixture.Delete(recursive: true);
        }
    }

    private static bool IsFixture(string path) =>
        string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
            StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).StartsWith(Prefix, StringComparison.Ordinal);

    private static string RegistryPath(string fixture) => @"Software\" + Path.GetFileName(fixture);
    private static string InstallRoot(string fixture) => Path.Combine(fixture, "Programs", InstalledLayout.ProductDirectory);
    private static string MenuLink(string fixture) => Path.Combine(fixture, "Menu", InstalledLayout.StartMenuName);

    private static bool Reject(Func<InstallOutcome> install)
    {
        try { install(); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static Process StartWorker<T>(string directory, T step, string mode = "--f12-install-worker") where T : Enum
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "DesktopMotionStudio.Core.Checks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        };
        foreach (string arg in new[] { mode, directory, step.ToString() }) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the installer worker.");
    }
}
