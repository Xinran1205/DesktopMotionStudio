using DesktopMotionStudio.Core.Shortcuts;

internal static class F12CleanupChecks
{
    internal static int Run()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f12-cleanup-");
        int failures = 0;
        void Check(string name, bool result) => F09Checks.Check("F12 cleanup " + name, result, ref failures);
        try
        {
            string desktop = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Desktop")).FullName;
            string receiptRoot = Path.Combine(fixture.FullName, "receipts");
            string launcher = Path.Combine(fixture.FullName, "Launcher.exe");
            string otherLauncher = Path.Combine(fixture.FullName, "other.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "where.exe"), launcher);
            File.Copy(launcher, otherLauncher);
            var store = new ShortcutReceiptStore(receiptRoot);
            string owned = Add("orphan", launcher), edited = Add("edited", launcher), moved = Add("moved", launcher);
            string other = Add("other", otherLauncher), busy = Add("busy", launcher), future = Add("future", launcher);
            string recovered = Add("previous", launcher);
            string wrongKey = Add("wrong-key", launcher);
            string recordDirectory = Path.Combine(receiptRoot, "records");
            File.Move(Record("previous"), Path.ChangeExtension(Record("previous"), ".previous"));
            File.Move(Record("wrong-key"), Record("unrelated-id"));
            File.WriteAllText(Record("future"), "{\"schemaVersion\":99}");
            File.AppendAllText(edited, "edited");
            string movedPath = Path.Combine(desktop, "moved-by-user.lnk");
            File.Move(moved, movedPath);
            string foreign = Path.Combine(desktop, "foreign.lnk");
            File.WriteAllText(foreign, "foreign");
            byte[] editedBytes = File.ReadAllBytes(edited), movedBytes = File.ReadAllBytes(movedPath);

            string currentDesktop = desktop;
            var cleanup = new ManagedShortcutCleanup(receiptRoot, launcher, () => currentDesktop);
            if (store.Begin("busy", out var lease) != ShortcutReceiptStatus.Ok) throw new IOException("Could not lease receipt.");
            using (lease)
            {
                ShortcutCleanupResult result = cleanup.Run();
                Check("deletes orphan and recovered previous proofs without reading the profile",
                    result == new ShortcutCleanupResult(2, 6, false) && !File.Exists(owned) && !File.Exists(recovered));
                Check("successful cleanup retires only the handled receipts",
                    store.Read("orphan").Status == ShortcutReceiptStatus.Missing && store.Read("previous").Status == ShortcutReceiptStatus.Missing);
                Check("edited, relocated, other-install and busy files survive",
                    File.ReadAllBytes(edited).SequenceEqual(editedBytes) && File.ReadAllBytes(movedPath).SequenceEqual(movedBytes) &&
                    File.Exists(other) && File.Exists(busy) && File.Exists(future) && File.Exists(wrongKey));
                Check("unknown and mismatched receipts and unregistered files are preserved",
                    File.ReadAllText(Record("future")) == "{\"schemaVersion\":99}" &&
                    File.Exists(Record("unrelated-id")) && File.ReadAllText(foreign) == "foreign");
            }
            Check("retry can remove a previously busy entry", cleanup.Run().Removed == 1 && !File.Exists(busy));
            Check("moved entry retains its ownership evidence", store.Read("moved").Status == ShortcutReceiptStatus.Ok);

            string redirect = Path.Combine(fixture.FullName, "Redirected");
            F09Checks.CreateJunction(redirect, desktop);
            try
            {
                currentDesktop = redirect;
                Check("a redirected Desktop is refused without deleting files", cleanup.Run().Incomplete &&
                    File.Exists(edited) && File.Exists(other) && File.Exists(movedPath));
            }
            finally { Directory.Delete(redirect, recursive: false); }
            currentDesktop = desktop;
            string overLimit = Add("over-limit", launcher);
            for (int i = 0; i < 4096; i++) File.WriteAllBytes(Path.Combine(recordDirectory, $"fixture-{i}.bin"), []);
            Check("directory limit is checked before any entry deletion", cleanup.Run().Incomplete && File.Exists(overLimit));

            return failures;

            string Record(string id) => Path.Combine(recordDirectory, ShortcutReceiptStore.Key(id) + ".json");

            string Add(string id, string target)
            {
                using var platform = WindowsShortcutPlatform.Open(desktop);
                using var stage = platform.Stage(id + ".lnk", target, id, null);
                if (store.Begin(id, out var transaction) != ShortcutReceiptStatus.Ok)
                    throw new IOException("Could not create receipt fixture.");
                using (transaction)
                    if (transaction!.Save(new(1, id, stage.Proof)) != ShortcutReceiptStatus.Ok)
                        throw new IOException("Could not store receipt fixture.");
                stage.Publish();
                return stage.Proof.Path;
            }
        }
        finally
        {
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, temp, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f12-cleanup-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
    }
}
