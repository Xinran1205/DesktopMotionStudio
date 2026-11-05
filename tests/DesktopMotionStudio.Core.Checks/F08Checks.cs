using System.Diagnostics;
using System.Text;
using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.State;
using System.Runtime.InteropServices;

internal static class F08Checks
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), $"dms-f08-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        int failures = 0;
        try
        {
            RunMigration(root, ref failures);
            RunDiscoveryProtocol(ref failures);
            if (OperatingSystem.IsWindows()) RunRegistry(root, ref failures);
        }
        finally { Directory.Delete(root, recursive: true); }
        return failures;
    }

    private static void RunMigration(string root, ref int failures)
    {
        string path = Path.Combine(root, "legacy.json");
        const string legacy = """
            {"schemaVersion":1,"applications":[
            {"appId":"exe","displayName":"Old executable","launchTarget":"C:/old/app.exe"},
            {"appId":"link","displayName":"Old link","launchTarget":"C:/old/app.lnk"},
            {"appId":"package","displayName":"Old package","launchTarget":"Example.Package!App"},
            {"appId":"raw","displayName":"Old text","launchTarget":"  unclassified / literal text  "}],
            "bindings":[{"bindingId":"binding-old","appId":"raw","sceneId":"scene-one","enabled":true}],
            "settings":{"theme":"dark","muted":true}}
            """;
        byte[] oldBytes = Encoding.UTF8.GetBytes(legacy);
        File.WriteAllBytes(path, oldBytes);
        File.WriteAllText(path + ".bak", "existing backup marker");
        byte[] oldBackup = File.ReadAllBytes(path + ".bak");
        var store = new StudioStateRepository(path);
        StudioState? loaded = store.Load().State;
        Record("legacy state upgrades only in memory without target-type inference",
            loaded is { SchemaVersion: StudioState.CurrentSchemaVersion } && loaded.Applications.All(app => app.Target is null) &&
            loaded.Applications[3].LaunchTarget == "  unclassified / literal text  " && loaded.Bindings[0].BindingId == "binding-old" &&
            loaded.Settings == new StudioSettings("dark", true) && File.ReadAllBytes(path).SequenceEqual(oldBytes), ref failures);
        var registry = new ApplicationRegistry(store);
        Record("legacy reads and no-op rename preserve source and backup bytes",
            store.Update(state => state).IsSuccess && registry.Rename("raw", "Old text").Status == ApplicationRegistryStatus.Unchanged &&
            File.ReadAllBytes(path).SequenceEqual(oldBytes) && File.ReadAllBytes(path + ".bak").SequenceEqual(oldBackup), ref failures);
        Record("first changed legacy mutation persists v2 with exact old-file backup",
            registry.Rename("raw", "Renamed").Status == ApplicationRegistryStatus.Applied &&
            File.ReadAllBytes(path + ".bak").SequenceEqual(oldBytes) && store.Load().State!.SchemaVersion == 2 &&
            store.Load().State!.Applications.Single(app => app.AppId == "raw").LaunchTarget == loaded!.Applications[3].LaunchTarget, ref failures);
        byte[] migrated = File.ReadAllBytes(path);
        Record("migration is repeatable and idempotent",
            registry.Rename("raw", "Renamed").Status == ApplicationRegistryStatus.Unchanged &&
            store.Load().State!.Bindings[0].BindingId == "binding-old" && File.ReadAllBytes(path).SequenceEqual(migrated), ref failures);

        string current = """{"schemaVersion":2,"applications":[{"appId":"one","displayName":"One","launchTarget":"raw","target":{"kind":"Executable","identifier":"C:\\missing\\app.exe","arguments":[],"workingDirectory":null}}],"bindings":[],"settings":{"theme":"system","muted":false}}""";
        File.WriteAllText(path, current);
        Record("typed missing executable remains loadable for repair", store.Load().IsSuccess, ref failures);
        var invalidCases = new (string Name, string Json, RepositoryError Error)[]
        {
            ("future before typed enum", current.Replace("\"schemaVersion\":2", "\"schemaVersion\":3").Replace("Executable", "FutureType"), RepositoryError.UnsupportedSchema),
            ("non-numeric schema", current.Replace("\"schemaVersion\":2", "\"schemaVersion\":\"2\""), RepositoryError.InvalidState),
            ("unknown typed enum", current.Replace("Executable", "FutureType"), RepositoryError.InvalidJson),
            ("numeric typed enum", current.Replace("\"Executable\"", "0"), RepositoryError.InvalidJson),
            ("missing typed kind", current.Replace("\"kind\":\"Executable\",", ""), RepositoryError.InvalidJson),
            ("duplicate member", current.Replace("\"schemaVersion\":2", "\"schemaVersion\":2,\"schemaVersion\":2"), RepositoryError.InvalidJson),
            ("unknown target member", current.Replace("\"arguments\":[]", "\"arguments\":[],\"future\":true"), RepositoryError.InvalidJson),
            ("shortcut arguments", current.Replace("Executable", "Shortcut").Replace("app.exe", "app.lnk").Replace("\"arguments\":[]", "\"arguments\":[\"x\"]"), RepositoryError.InvalidState),
            ("legacy typed member", current.Replace("\"schemaVersion\":2", "\"schemaVersion\":1"), RepositoryError.InvalidState)
        };
        foreach (var test in invalidCases)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(test.Json);
            File.WriteAllBytes(path, bytes);
            int callbacks = 0;
            RepositoryResult result = store.Update(state => { callbacks++; return state; });
            Record($"state rejects {test.Name} without writing", result.Error == test.Error && callbacks == 0 &&
                File.ReadAllBytes(path).SequenceEqual(bytes) && File.ReadAllBytes(path + ".bak").SequenceEqual(oldBytes), ref failures);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RunRegistry(string root, ref int failures)
    {
        string path = Path.Combine(root, "registry.json");
        var store = new StudioStateRepository(path);
        var registry = new ApplicationRegistry(store);
        string executable = Environment.ProcessPath!;
        string work = Path.GetDirectoryName(executable)!;
        var definition = new ApplicationTargetDefinition(ApplicationTargetKind.Executable, executable, ["--one"]);
        ApplicationRegistryResult added = registry.Add("First", definition);
        Record("typed executable registration round-trips", added.Status == ApplicationRegistryStatus.Applied &&
            store.Load().State!.Applications.Single().Target?.Kind == ApplicationTargetKind.Executable, ref failures);
        byte[] firstBytes = File.ReadAllBytes(path);
        Record("resolved definition detects duplicate despite display-name and path casing",
            registry.Add("Different name", definition with { Identifier = executable.ToUpperInvariant(), WorkingDirectory = work }).Status == ApplicationRegistryStatus.Duplicate &&
            File.ReadAllBytes(path).SequenceEqual(firstBytes), ref failures);
        ApplicationRegistryResult second = registry.Add("First", definition with { Arguments = ["--ONE"] });
        Record("same names and argument-case differences can be distinct profiles",
            second.Status == ApplicationRegistryStatus.Applied && second.Profile!.AppId != added.Profile!.AppId, ref failures);
        ApplicationRegistryResult package = registry.Add("Package", new(ApplicationTargetKind.Packaged, "Example.Package!App", []));
        Record("packaged target keeps explicit kind and exact AUMID",
            package.Succeeded && ApplicationRegistry.GetTarget(package.Profile!) is DesktopMotionStudio.Core.Launch.PackagedAppTarget { Aumid: "Example.Package!App" }, ref failures);
        string appId = added.Profile!.AppId;
        store.Update(state => state with { Bindings = [new("stable-binding", appId, "scene-one", true)] });
        ApplicationRegistryResult repaired = registry.Repair(appId, definition with { Arguments = ["--repaired"] });
        registry.Rename(appId, "Renamed");
        Record("repair and rename retain app and binding identities",
            repaired.Succeeded && store.Load().State!.Applications.Single(app => app.AppId == appId).DisplayName == "Renamed" &&
            store.Load().State!.Bindings.Single().BindingId == "stable-binding", ref failures);
        byte[] beforeReject = File.ReadAllBytes(path);
        byte[] backupBeforeReject = File.ReadAllBytes(path + ".bak");
        Record("invalid targets and duplicate repair preserve bytes",
            registry.Add("Invalid", definition with { Identifier = Path.Combine(root, "missing.exe") }).Status == ApplicationRegistryStatus.TargetUnavailable &&
            registry.Add(" ", definition).Status == ApplicationRegistryStatus.InvalidRequest &&
            registry.Repair(appId, second.Profile!.Target!).Status == ApplicationRegistryStatus.Duplicate &&
            File.ReadAllBytes(path).SequenceEqual(beforeReject) && File.ReadAllBytes(path + ".bak").SequenceEqual(backupBeforeReject), ref failures);
        string[] originalArgs = ["one"];
        var profile = new ApplicationProfile("copy", "Copy", "raw", definition with { Arguments = originalArgs });
        var target = (DesktopMotionStudio.Core.Launch.ExecutableTarget)ApplicationRegistry.GetTarget(profile)!;
        originalArgs[0] = "changed";
        Record("typed provider snapshots arguments and never infers legacy text",
            target.Arguments[0] == "one" && ApplicationRegistry.GetTarget(new("legacy", "Legacy", executable)) is null, ref failures);
        Record("remove atomically removes only app and its binding",
            registry.Remove(appId).Succeeded && !store.Load().State!.Applications.Any(app => app.AppId == appId) &&
            store.Load().State!.Bindings.Count == 0 && store.Load().State!.Applications.Any(app => app.AppId == second.Profile!.AppId), ref failures);
        byte[] removed = File.ReadAllBytes(path);
        Record("repeated removal is a no-op", registry.Remove(appId).Status == ApplicationRegistryStatus.Unchanged &&
            File.ReadAllBytes(path).SequenceEqual(removed), ref failures);
        var unsafeTarget = definition with { Identifier = "\\\\invalid.example\\share\\app.exe" };
        Record("network targets reject before filesystem resolution", registry.Add("Remote", unsafeTarget).Status == ApplicationRegistryStatus.TargetUnavailable, ref failures);

        Parallel.Invoke(() => registry.Rename(second.Profile!.AppId, "Concurrent rename"),
            () => registry.Repair(second.Profile!.AppId, definition with { Arguments = ["--concurrent"] }));
        ApplicationProfile concurrent = store.Load().State!.Applications.Single(app => app.AppId == second.Profile!.AppId);
        Record("concurrent rename and repair preserve both field edits", concurrent.DisplayName == "Concurrent rename" && concurrent.Target!.Arguments.SequenceEqual(["--concurrent"]), ref failures);
        string shortcut = Path.Combine(root, "fixture.lnk");
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object link = ((dynamic)shell).CreateShortcut(shortcut);
        try
        {
            ((dynamic)link).TargetPath = executable;
            ((dynamic)link).WorkingDirectory = work;
            ((dynamic)link).Arguments = "--concurrent";
            ((dynamic)link).Save();
        }
        finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
        byte[] linkBytes = File.ReadAllBytes(shortcut);
        Record("equivalent shortcut is duplicate without changing the shortcut file",
            registry.Add("Alias", new(ApplicationTargetKind.Shortcut, shortcut, [])).Status == ApplicationRegistryStatus.Duplicate &&
            File.ReadAllBytes(shortcut).SequenceEqual(linkBytes), ref failures);
        registry.Remove(second.Profile!.AppId);
        ApplicationRegistryResult linkAdded = registry.Add("Shortcut", new(ApplicationTargetKind.Shortcut, shortcut, []));
        Record("shortcut registration retains original typed link identity", linkAdded.Profile?.Target is { Kind: ApplicationTargetKind.Shortcut } storedLink && storedLink.Identifier == shortcut, ref failures);
        var failing = new ApplicationRegistry(new FailureStore());
        Record("registry propagates storage failure without successful result",
            failing.Rename("id", "Name").Status == ApplicationRegistryStatus.StorageError && failing.Remove("id").RepositoryError == RepositoryError.IoError, ref failures);
        string linkTarget = Path.Combine(root, "junction-target");
        string junction = Path.Combine(root, "junction-entry");
        Directory.CreateDirectory(linkTarget);
        File.Copy(executable, Path.Combine(linkTarget, "app.exe"));
        string script = "New-Item -ItemType Junction -Path '" + junction.Replace("'", "''") + "' -Target '" + linkTarget.Replace("'", "''") + "' -ErrorAction Stop | Out-Null";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using Process creator = Process.Start(start) ?? throw new InvalidOperationException("Junction fixture could not start.");
        if (!creator.WaitForExit(5_000)) { creator.Kill(); creator.WaitForExit(1_000); throw new InvalidOperationException("Junction creation timed out."); }
        if (creator.ExitCode != 0) throw new InvalidOperationException("Junction fixture failed.");
        try
        {
            Record("reparse ancestry is rejected before target filesystem probes",
                registry.Add("Reparse", definition with { Identifier = Path.Combine(junction, "app.exe"), Arguments = [] }).Status == ApplicationRegistryStatus.TargetUnavailable, ref failures);
        }
        finally { Directory.Delete(junction, recursive: false); }
    }

    private static void RunDiscoveryProtocol(ref int failures)
    {
        var candidate = new ApplicationCandidate("Fixture", new(ApplicationTargetKind.Packaged, "Example.Package!App", []), "Packaged");
        byte[] encoded = ApplicationDiscoveryProtocol.Encode(new(ApplicationDiscoveryStatus.Complete, [candidate]));
        Record("discovery protocol round-trips explicit typed candidates", ApplicationDiscoveryProtocol.Decode(encoded).Candidates.Single().Target.Identifier == candidate.Target.Identifier, ref failures);
        string json = Encoding.UTF8.GetString(encoded);
        foreach (string invalid in new[]
        {
            json.Replace("\"protocolVersion\":1", "\"protocolVersion\":2"),
            json.Replace("\"protocolVersion\":1", "\"protocolVersion\":1,\"protocolVersion\":1"),
            json.Replace("\"status\":\"Complete\",", ""),
            json.Replace("\"kind\":\"Packaged\",", ""),
            json.Replace("\"status\":\"Complete\"", "\"status\":0"),
            json.Replace("\"source\":\"Packaged\"", "\"source\":\"StartMenu\"")
        })
        {
            bool rejected = false;
            try { _ = ApplicationDiscoveryProtocol.Decode(Encoding.UTF8.GetBytes(invalid)); }
            catch (InvalidDataException) { rejected = true; }
            Record("discovery rejects unsupported malformed or inconsistent metadata", rejected, ref failures);
        }
        bool byteLimit = false, countLimit = false;
        try { _ = ApplicationDiscoveryProtocol.Decode(new byte[ApplicationDiscoveryProtocol.MaximumBytes + 1]); }
        catch (InvalidDataException) { byteLimit = true; }
        try { _ = ApplicationDiscoveryProtocol.Encode(new(ApplicationDiscoveryStatus.Complete, Enumerable.Repeat(candidate, 2001).ToArray())); }
        catch (InvalidDataException) { countLimit = true; }
        Record("discovery enforces byte and candidate limits", byteLimit && countLimit, ref failures);
    }

    private sealed class FailureStore : IStudioStateStore
    {
        public RepositoryResult Load() => new(null, RepositoryError.IoError);
        public RepositoryResult Update(Func<StudioState, StudioState> transition) => Load();
    }

    private static void Record(string name, bool passed, ref int failures)
    {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
        if (!passed) failures++;
    }
}
