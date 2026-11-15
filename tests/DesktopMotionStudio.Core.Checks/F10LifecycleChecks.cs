using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;

internal static class F10LifecycleChecks
{
    private const string Id = "旧绑定";

    internal static int Run()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f10-lifecycle-");
        int failures = 0;
        void Check(string name, bool passed) => F09Checks.Check("F10 lifecycle " + name, passed, ref failures);
        try { RunCases(fixture.FullName, Check); }
        finally
        {
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, parent, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f10-lifecycle-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
        return failures;
    }

    private static void RunCases(string root, Action<string, bool> check)
    {
        string desktop = Directory.CreateDirectory(Path.Combine(root, "Desktop")).FullName;
        string other = Directory.CreateDirectory(Path.Combine(root, "OtherDesktop")).FullName;
        string activeDesktop = desktop;
        string storage = Path.Combine(root, "receipts");
        string profile = Path.Combine(root, "state.json");
        string catalog = Path.Combine(root, "Scenes");
        string launcher = Path.Combine(root, "Launcher.exe");
        string newer = Path.Combine(root, "New Launcher.exe");
        string nativeExe = Path.Combine(Environment.SystemDirectory, "where.exe");
        File.Copy(nativeExe, launcher);
        File.Copy(nativeExe, newer);
        F09Checks.WriteScene(catalog, "fixture");
        var app = new ApplicationProfile("fixture", "Fixture / 样本", nativeExe,
            new(ApplicationTargetKind.Executable, nativeExe, []));
        var binding = new ApplicationBinding(Id, app.AppId, "studio-pulse", true);
        StudioState state = StudioState.Empty with { Applications = [app], Bindings = [binding] };
        var profileStore = new StudioStateRepository(profile);
        if (profileStore.Save(state) != RepositoryError.None || profileStore.Save(state) != RepositoryError.None)
            throw new IOException("Failed to create state fixture.");
        byte[] expected = File.ReadAllBytes(profile);
        byte[] backup = File.ReadAllBytes(profile + ".bak");
        var receipts = new ShortcutReceiptStore(storage);
        ManagedShortcutService Service(string target) => new(profileStore, catalog, storage, target, () => activeDesktop);
        var service = Service(launcher);
        ShortcutResult initial = service.Inspect(Id);
        check("status read does not write", initial.Status == ShortcutStatus.NotCreated && !Directory.Exists(storage));
        File.WriteAllText(initial.Path!, "same-name foreign");
        check("create rejects existing same-name file", service.Create(Id).Status == ShortcutStatus.Conflict &&
            File.ReadAllText(initial.Path!) == "same-name foreign");
        File.Delete(initial.Path!);

        var a = Task.Run(() => Service(launcher).Create(Id));
        var b = Task.Run(() => Service(launcher).Create(Id));
        Task.WaitAll(a, b);
        string path = initial.Path!;
        Console.WriteLine($"  concurrent outcomes {a.Result.Status}/{b.Result.Status}, inspect {service.Inspect(Id).Status}");
        check("concurrent create publishes one verified entry",
            new[] { a.Result.Status, b.Result.Status }.Order().SequenceEqual(
                new[] { ShortcutStatus.Healthy, ShortcutStatus.Created }.Order()) &&
            Directory.GetFiles(desktop, "*.lnk").SequenceEqual([path]) && service.Inspect(Id).Status == ShortcutStatus.Healthy);
        string receiptFile = Directory.GetFiles(Path.Combine(storage, "records"), "*.json").Single();
        byte[] durable = File.ReadAllBytes(receiptFile);
        byte[] original = File.ReadAllBytes(path);
        ShortcutResult repeated = service.Create(Id);
        Console.WriteLine($"  repeated outcome {repeated.Status}");
        check("repeat create keeps file and receipt exact", repeated.Status == ShortcutStatus.Healthy &&
            File.ReadAllBytes(path).SequenceEqual(original) && File.ReadAllBytes(receiptFile).SequenceEqual(durable));

        File.WriteAllText(receiptFile, "{\"schemaVersion\":3}");
        check("future receipt stops desktop removal", service.Remove(Id).Status == ShortcutStatus.StorageError &&
            File.ReadAllBytes(path).SequenceEqual(original) && File.ReadAllText(receiptFile) == "{\"schemaVersion\":3}");
        File.WriteAllBytes(receiptFile, durable);

        string moved = Path.Combine(desktop, "Moved.lnk");
        File.Move(path, moved);
        ShortcutResult missing = service.Inspect(Id);
        ShortcutResult repaired = service.Repair(Id);
        ShortcutResult stale = Service(newer).Inspect(Id);
        ShortcutResult switched = Service(newer).Repair(Id);
        Console.WriteLine($"  missing {missing.Status}, repair {repaired.Status}, stale {stale.Status}, switch {switched.Status}");
        check("lost entry is repairable and moved file preserved", missing.Status == ShortcutStatus.Missing &&
            repaired.Status == ShortcutStatus.Created && File.ReadAllBytes(moved).SequenceEqual(original));
        check("launcher move detected and explicit repair switches target", stale.Status == ShortcutStatus.RepairRequired &&
            switched.Status == ShortcutStatus.Created && receipts.Read(Id).Receipt?.Current?.LauncherPath == newer);
        File.AppendAllText(path, "user edit");
        byte[] edited = File.ReadAllBytes(path);
        check("edited link stays untouched on repair and remove", Service(newer).Repair(Id).Status == ShortcutStatus.Conflict &&
            service.Remove(Id).Status == ShortcutStatus.Conflict && File.ReadAllBytes(path).SequenceEqual(edited));

        activeDesktop = other;
        check("desktop change limits removal to current root", Service(newer).Inspect(Id).Status == ShortcutStatus.OutOfScope &&
            Service(newer).Remove(Id).Status == ShortcutStatus.OutOfScope);
        var relocated = Service(newer).Repair(Id);
        check("explicit repair creates in new desktop only", relocated.Status == ShortcutStatus.Created &&
            File.Exists(relocated.Path) && File.ReadAllBytes(path).SequenceEqual(edited));
        check("verified remove preserves moved and edited entries", Service(newer).Remove(Id).Status == ShortcutStatus.Removed &&
            !File.Exists(relocated.Path) && File.Exists(moved) && File.ReadAllBytes(path).SequenceEqual(edited));

        const string interrupted = "Interrupted.lnk";
        using (var platform = WindowsShortcutPlatform.Open(other))
        using (var stage = platform.Stage(interrupted, newer, Id, null))
        {
            SavePending(receipts, stage.Proof);
            check("pending receipt with no visible entry reads as missing", Service(newer).Inspect(Id).Status == ShortcutStatus.Missing);
            stage.Publish();
        }
        check("pending proof after publication identifies file without promotion",
            Service(newer).Inspect(Id).Status == ShortcutStatus.Healthy && receipts.Read(Id).Receipt?.Pending is not null);
        check("pending proof can be used for verified remove", Service(newer).Remove(Id).Status == ShortcutStatus.Removed);

        int scopeReads = 0;
        var changed = new ManagedShortcutService(profileStore, catalog, storage, newer,
            () => ++scopeReads == 1 ? other : desktop);
        check("desktop change during create stops publication and preserves old files",
            changed.Create(Id).Status == ShortcutStatus.OutOfScope && !Directory.GetFiles(other).Any() &&
            File.ReadAllBytes(path).SequenceEqual(edited) && receipts.Read(Id).Status == ShortcutReceiptStatus.Missing);

        check("all operations preserve profile and backup bytes", File.ReadAllBytes(profile).SequenceEqual(expected) &&
            File.ReadAllBytes(profile + ".bak").SequenceEqual(backup));

        int beforePublish = 0;
        var changingBinding = new ManagedShortcutService(profileStore, catalog, storage, newer, () =>
        {
            if (++beforePublish == 2 && !profileStore.Update(current =>
                    current with { Bindings = [binding with { Enabled = false }] }).IsSuccess)
                throw new IOException("Could not disable binding fixture.");
            return other;
        });
        check("binding disabled after stage cannot publish an entry", changingBinding.Create(Id).Status == ShortcutStatus.Ineligible &&
            !Directory.GetFiles(other).Any() && receipts.Read(Id).Status == ShortcutReceiptStatus.Missing);
    }

    private static void SavePending(ShortcutReceiptStore store, ShortcutProof proof)
    {
        if (store.Begin(Id, out var tx) != ShortcutReceiptStatus.Ok) throw new IOException("Receipt lock unavailable.");
        using (tx)
            if (tx!.Save(new(1, Id, null, proof)) != ShortcutReceiptStatus.Ok) throw new IOException("Receipt write failed.");
    }
}
