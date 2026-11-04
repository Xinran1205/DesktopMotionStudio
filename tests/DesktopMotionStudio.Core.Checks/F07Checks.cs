using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DesktopMotionStudio.Core.Bindings;
using DesktopMotionStudio.Core.Instances;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.MotionHost;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;

internal static class F07Checks
{
    private const string SceneJson = """{"schemaVersion":1,"sceneId":"scene-one","sceneVersion":"1.0.0","name":"Fixture","displayMode":"centered","assets":[]}""";

    internal static int Run()
    {
        int failures = 0;
        string root = Path.Combine(Path.GetTempPath(), $"dms-f07-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            RunContracts(root, ref failures);
            RunTransactions(root, ref failures);
            if (OperatingSystem.IsWindows()) RunWorkers(root, ref failures);
            failures += F07WindowsIntegration.Run();
        }
        finally { Directory.Delete(root, recursive: true); }
        return failures;
    }

    private static void RunContracts(string root, ref int failures)
    {
        string catalogRoot = Path.Combine(root, "catalog");
        WriteScene(catalogRoot, "one", SceneJson);
        WriteScene(catalogRoot, "two", SceneJson.Replace("scene-one", "scene-two"));
        string path = Path.Combine(root, "bindings.json");
        var store = new StudioStateRepository(path);
        StudioState initial = Seed();
        Require(store.Save(initial) == RepositoryError.None);
        var catalog = new BuiltInSceneCatalog();
        LaunchTarget? currentTarget = new ExecutableTarget(Environment.ProcessPath!, []);
        var service = new BindingService(store, () => catalog.Scan(catalogRoot), _ => currentTarget);

        Record("binding resolve distinguishes missing registration and unbound app",
            service.Resolve("missing").Status == BindingResolutionStatus.ApplicationMissing &&
            service.Resolve("app-one").Status == BindingResolutionStatus.Unbound, ref failures);
        BindingMutationResult first = service.Bind("app-one", "scene-one");
        BindingMutationResult second = service.Bind("app-two", "scene-one");
        Record("one scene binds independently to two applications",
            first.Status == BindingMutationStatus.Applied && second.Status == BindingMutationStatus.Applied &&
            first.Binding!.BindingId != second.Binding!.BindingId && store.Load().State!.Bindings.Count == 2, ref failures);
        string firstId = first.Binding!.BindingId;
        byte[] beforeNoop = File.ReadAllBytes(path);
        byte[] backupBeforeNoop = File.ReadAllBytes(path + ".bak");
        Record("identical binding is a byte-preserving no-op",
            service.Bind("APP-ONE", "scene-one").Status == BindingMutationStatus.Unchanged &&
            File.ReadAllBytes(path).SequenceEqual(beforeNoop) && File.ReadAllBytes(path + ".bak").SequenceEqual(backupBeforeNoop), ref failures);
        Record("scene replacement retains binding identity and other app",
            service.Bind("app-one", "scene-two").Binding?.BindingId == firstId &&
            store.Load().State!.Bindings.Single(b => b.AppId == "app-two") == second.Binding, ref failures);

        currentTarget = new PackagedAppTarget("Example.Package!App");
        BindingResolution packaged = service.Resolve("app-one");
        Record("binding preserves an explicitly provided packaged target",
            packaged.Status == BindingResolutionStatus.Ready && packaged.Context!.Target == currentTarget, ref failures);
        var shortcut = new ShortcutTarget(Path.Combine(root, "explicit.lnk"));
        var explicitShortcut = new BindingService(store, () => catalog.Scan(catalogRoot), _ => shortcut,
            _ => new LaunchTargetResolution(new ResolvedExecutableLaunch(new ExecutableLaunchIdentity(Environment.ProcessPath!), [], root), LaunchTargetResolutionStatus.Resolved));
        Record("binding preserves explicit shortcut type without guessing legacy string",
            explicitShortcut.Resolve("app-one").Context?.Target == shortcut, ref failures);

        currentTarget = null;
        Record("disable works without target and preserves scene and ID",
            service.SetEnabled("app-one", false).Binding is { Enabled: false, SceneId: "scene-two" } disabled &&
            disabled.BindingId == firstId && service.Resolve("app-one").Status == BindingResolutionStatus.Disabled, ref failures);
        byte[] disabledBytes = File.ReadAllBytes(path);
        byte[] disabledBackup = File.ReadAllBytes(path + ".bak");
        Record("repeated disable is unchanged even with unavailable dependencies",
            service.SetEnabled("app-one", false).Status == BindingMutationStatus.Unchanged &&
            File.ReadAllBytes(path).SequenceEqual(disabledBytes) && File.ReadAllBytes(path + ".bak").SequenceEqual(disabledBackup), ref failures);
        Record("enable rejects unavailable target without writing",
            service.SetEnabled("app-one", true).Status == BindingMutationStatus.TargetUnavailable &&
            File.ReadAllBytes(path).SequenceEqual(disabledBytes), ref failures);
        currentTarget = new ExecutableTarget(Environment.ProcessPath!, []);
        Record("enable revalidates and retains binding ID",
            service.SetEnabled("app-one", true).Binding is { Enabled: true } enabled && enabled.BindingId == firstId, ref failures);

        byte[] readonlyBytes = File.ReadAllBytes(path);
        string manifestPath = Path.Combine(catalogRoot, "two", "scene.json");
        File.WriteAllText(manifestPath, SceneJson.Replace("scene-one", "scene-two").Replace("Fixture", "Renamed"));
        Record("resolve uses current manifest under stable ID without persisting",
            service.Resolve("app-one").Context?.Scene.Name == "Renamed" && File.ReadAllBytes(path).SequenceEqual(readonlyBytes), ref failures);
        File.Delete(manifestPath);
        BindingResolution missingScene = service.Resolve("app-one");
        Record("missing scene is diagnostic and retains binding",
            missingScene.Status == BindingResolutionStatus.SceneUnavailable && missingScene.Context is null &&
            File.ReadAllBytes(path).SequenceEqual(readonlyBytes), ref failures);
        Record("disable and remove work with missing scene; removal is idempotent",
            service.SetEnabled("app-one", false).Succeeded && service.Remove("app-one").Status == BindingMutationStatus.Applied &&
            service.Remove("app-one").Status == BindingMutationStatus.Unchanged &&
            service.SetEnabled("app-one", true).Status == BindingMutationStatus.BindingMissing, ref failures);
        byte[] removedBytes = File.ReadAllBytes(path);
        byte[] removedBackup = File.ReadAllBytes(path + ".bak");
        Record("repeated remove preserves state and backup bytes",
            service.Remove("app-one").Status == BindingMutationStatus.Unchanged &&
            File.ReadAllBytes(path).SequenceEqual(removedBytes) && File.ReadAllBytes(path + ".bak").SequenceEqual(removedBackup), ref failures);

        var targetThrows = new BindingService(store, () => catalog.Scan(catalogRoot), _ => throw new IOException("fixture"));
        var catalogThrows = new BindingService(store, () => throw new IOException("fixture"), _ => new ExecutableTarget(Environment.ProcessPath!, []));
        var nullTarget = new BindingService(store, () => catalog.Scan(catalogRoot), _ => null);
        Record("upstream exceptions become unavailable statuses without contexts",
            targetThrows.Resolve("app-two") is { Status: BindingResolutionStatus.TargetUnavailable, Context: null } &&
            catalogThrows.Resolve("app-two") is { Status: BindingResolutionStatus.SceneUnavailable, Context: null } &&
            nullTarget.Resolve("app-two") is { Status: BindingResolutionStatus.TargetUnavailable, Context: null }, ref failures);
        Record("rejected assignment preserves state and backup",
            targetThrows.Bind("app-one", "scene-one").Status == BindingMutationStatus.TargetUnavailable &&
            catalogThrows.Bind("app-one", "scene-one").Status == BindingMutationStatus.SceneUnavailable &&
            service.Bind("missing", "scene-one").Status == BindingMutationStatus.ApplicationMissing &&
            File.ReadAllBytes(path).SequenceEqual(removedBytes) && File.ReadAllBytes(path + ".bak").SequenceEqual(removedBackup), ref failures);
        currentTarget = new ExecutableTarget(Path.Combine(root, "missing.exe"), []);
        Record("removed executable is TargetUnavailable without state loss",
            service.Resolve("app-two").Status == BindingResolutionStatus.TargetUnavailable, ref failures);
        currentTarget = new ExecutableTarget(Environment.ProcessPath!, []);
        WriteScene(catalogRoot, "duplicate", SceneJson);
        Record("duplicate catalog ID is unavailable", service.Resolve("app-two").Status == BindingResolutionStatus.SceneUnavailable, ref failures);
        Directory.Delete(Path.Combine(catalogRoot, "duplicate"), recursive: true);
        File.WriteAllText(Path.Combine(catalogRoot, "one", "scene.json"), SceneJson.Replace("\"assets\":[]", "\"assets\":[\"missing.png\"]"));
        Record("missing scene assets are unavailable", service.Resolve("app-two").Status == BindingResolutionStatus.SceneUnavailable, ref failures);
        File.WriteAllText(Path.Combine(catalogRoot, "one", "scene.json"), SceneJson);
        var rootMissing = new BindingService(store, () => catalog.Scan(Path.Combine(root, "absent")), _ => currentTarget);
        Record("missing catalog root is unavailable", rootMissing.Resolve("app-two").Status == BindingResolutionStatus.SceneUnavailable, ref failures);
        Record("invalid IDs reject before persistence",
            service.Bind(new string('a', 129), "scene-one").Status == BindingMutationStatus.InvalidRequest &&
            service.Bind("app-one", "../scene").Status == BindingMutationStatus.InvalidRequest &&
            service.Resolve(" ").Status == BindingResolutionStatus.InvalidRequest, ref failures);
        StudioState after = store.Load().State!;
        Record("binding operations preserve profiles settings and scene content",
            after.Applications.SequenceEqual(initial.Applications) && after.Settings == initial.Settings &&
            File.ReadAllText(Path.Combine(catalogRoot, "one", "scene.json")) == SceneJson, ref failures);
        Parallel.For(0, 8, _ => service.Bind("app-one", "scene-one"));
        Record("concurrent same-app commands leave one binding",
            store.Load().State!.Bindings.Count(b => b.AppId == "app-one") == 1, ref failures);
        string longestAppId = new('x', 128);
        Require(store.Update(state => state with { Applications = state.Applications.Append(new ApplicationProfile(longestAppId, "Boundary", Environment.ProcessPath!)).ToArray() }).IsSuccess);
        Record("128-character application ID is accepted at the boundary", service.Bind(longestAppId, "scene-one").Succeeded, ref failures);
    }

    private static void RunTransactions(string root, ref int failures)
    {
        string path = Path.Combine(root, "transaction.json");
        var repository = new StudioStateRepository(path);
        Require(repository.Save(Seed()) == RepositoryError.None);
        int calls = 0;
        byte[] original = File.ReadAllBytes(path);
        RepositoryResult unchanged = repository.Update(state => { calls++; return state; });
        Record("transaction callback executes once and same-reference no-op does not create backup",
            unchanged.IsSuccess && calls == 1 && File.ReadAllBytes(path).SequenceEqual(original) && !File.Exists(path + ".bak"), ref failures);
        bool exceptionObserved = false;
        try { repository.Update(_ => throw new InvalidOperationException("fixture")); }
        catch (InvalidOperationException) { exceptionObserved = true; }
        Record("transaction exception leaves original bytes intact", exceptionObserved && File.ReadAllBytes(path).SequenceEqual(original), ref failures);
        RepositoryResult invalid = repository.Update(state => state with { SchemaVersion = 99 });
        Record("invalid transition output is rejected without write", invalid.Error == RepositoryError.InvalidState && File.ReadAllBytes(path).SequenceEqual(original), ref failures);
        Require(repository.Update(state => state with { Settings = new StudioSettings("light", false) }).IsSuccess);
        byte[] validState = File.ReadAllBytes(path);
        byte[] validBackup = File.ReadAllBytes(path + ".bak");
        if (OperatingSystem.IsWindows())
        {
            RepositoryResult failedWrite;
            using (File.Open(path + ".bak", FileMode.Open, FileAccess.Read, FileShare.None))
                failedWrite = repository.Update(state => state with { Settings = new StudioSettings("system", false) });
            Record("failed atomic replacement preserves both valid state and backup",
                failedWrite.Error == RepositoryError.IoError && File.ReadAllBytes(path).SequenceEqual(validState) &&
                File.ReadAllBytes(path + ".bak").SequenceEqual(validBackup), ref failures);
        }
        foreach (byte[] rejected in new[] { Encoding.UTF8.GetBytes("{"), Encoding.UTF8.GetBytes("{\"schemaVersion\":99}"), new byte[4 * 1024 * 1024 + 1] })
        {
            File.WriteAllBytes(path, rejected);
            calls = 0;
            RepositoryResult result = repository.Update(state => { calls++; return state; });
            Record("invalid or future state blocks mutation callback and preserves bytes",
                !result.IsSuccess && calls == 0 && File.ReadAllBytes(path).SequenceEqual(rejected) &&
                File.ReadAllBytes(path + ".bak").SequenceEqual(validBackup), ref failures);
        }
        string directoryPath = Path.Combine(root, "directory-as-state");
        Directory.CreateDirectory(directoryPath);
        var badRepository = new StudioStateRepository(directoryPath);
        Record("transaction reports real I/O failure", badRepository.Update(_ => Seed()).Error == RepositoryError.IoError, ref failures);
        var directoryService = new BindingService(badRepository, () => new SceneCatalogScanResult([], false), _ => null);
        Record("non-file configuration is storage failure for load resolve and no-op mutations",
            badRepository.Load().Error == RepositoryError.IoError &&
            directoryService.Resolve("app-one").Status == BindingResolutionStatus.StorageError &&
            directoryService.Remove("app-one").Status == BindingMutationStatus.StorageError, ref failures);
        var failingStore = new FailureStore();
        var service = new BindingService(failingStore, () => new SceneCatalogScanResult([], false), _ => null);
        Record("binding storage errors are explicit for reads and writes",
            service.Resolve("app-one") is { Status: BindingResolutionStatus.StorageError, Context: null, RepositoryError: RepositoryError.IoError } &&
            service.Remove("app-one").Status == BindingMutationStatus.StorageError, ref failures);
    }

    private static void RunWorkers(string root, ref int failures)
    {
        string workerRoot = Path.Combine(root, "workers");
        Directory.CreateDirectory(workerRoot);
        string statePath = Path.Combine(workerRoot, "state.json");
        string catalogRoot = Path.Combine(workerRoot, "catalog");
        WriteScene(catalogRoot, "one", SceneJson);
        var repository = new StudioStateRepository(statePath);
        Require(repository.Save(Seed()) == RepositoryError.None);
        string readyOne = Path.Combine(workerRoot, "ready1");
        string readyTwo = Path.Combine(workerRoot, "ready2");
        string gateOne = Path.Combine(workerRoot, "go1");
        string gateTwo = Path.Combine(workerRoot, "go2");
        using Process first = StartWorker(statePath, catalogRoot, "app-one", readyOne, gateOne);
        Require(WaitForFile(readyOne));
        using Process second = StartWorker(statePath, catalogRoot, "app-two", readyTwo, gateTwo);
        Require(WaitForFile(readyTwo + ".attempt"));
        File.WriteAllText(gateOne, "go");
        Require(WaitForFile(readyTwo));
        File.WriteAllText(gateTwo, "go");
        bool firstExited = first.WaitForExit(5_000);
        bool secondExited = second.WaitForExit(5_000);
        Record("separate-process binding edits both survive the shared transaction",
            firstExited && secondExited && first.ExitCode == 0 && second.ExitCode == 0 && repository.Load().State!.Bindings.Count == 2, ref failures);
        Console.WriteLine($"  F07 mutation workers: PID {first.Id}, PID {second.Id}");

        string busyReady = Path.Combine(workerRoot, "busy-ready");
        string busyGate = Path.Combine(workerRoot, "busy-go");
        using Process holder = StartWorker(statePath, catalogRoot, "app-one", busyReady, busyGate);
        Require(WaitForFile(busyReady));
        int callbacks = 0;
        long started = Stopwatch.GetTimestamp();
        RepositoryResult busy = repository.Update(state => { callbacks++; return state; });
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        Console.WriteLine($"  F07 lock-contention elapsed: {elapsed.TotalMilliseconds:F0}ms");
        File.WriteAllText(busyGate, "go");
        Require(holder.WaitForExit(5_000));
        Record("write contention returns Busy without callback within bounded wait",
            busy.Error == RepositoryError.Busy && callbacks == 0 && elapsed >= TimeSpan.FromSeconds(1.8) && elapsed < TimeSpan.FromSeconds(3), ref failures);

        string abandonedReady = Path.Combine(workerRoot, "abandoned-ready");
        using Process abandoned = StartWorker(statePath, catalogRoot, "app-one", abandonedReady, Path.Combine(workerRoot, "never-go"));
        Require(WaitForFile(abandonedReady));
        string lockName = "Local\\DesktopMotionStudio.State." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(statePath).ToUpperInvariant())));
        using Mutex retainedHandle = Mutex.OpenExisting(lockName);
        abandoned.Kill(entireProcessTree: false);
        Require(abandoned.WaitForExit(2_000));
        Record("abandoned write mutex revalidates and permits a later transaction", repository.Update(state => state).IsSuccess, ref failures);
    }

    internal static int RunWorker(string path, string catalogRoot, string appId, string ready, string gate)
    {
        var store = new PausingStore(new StudioStateRepository(path), ready, gate);
        var service = new BindingService(store, () => new BuiltInSceneCatalog().Scan(catalogRoot), app => new ExecutableTarget(app.LaunchTarget, []));
        return service.Bind(appId, "scene-one").Succeeded ? 0 : 1;
    }

    private static Process StartWorker(string path, string catalogRoot, string appId, string ready, string gate)
    {
        string executable = Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(System.Reflection.Assembly.GetEntryAssembly()!.Location);
        foreach (string value in new[] { "--f07-binding-worker", path, catalogRoot, appId, ready, gate }) start.ArgumentList.Add(value);
        return Process.Start(start) ?? throw new InvalidOperationException("Worker did not start.");
    }

    private static bool WaitForFile(string path)
    {
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(4))
        {
            if (File.Exists(path)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    private static StudioState Seed() => new(1,
        [new("app-one", "First", Environment.ProcessPath!), new("app-two", "Second", Environment.ProcessPath!)], [], new StudioSettings("dark", true));

    private static void WriteScene(string root, string directory, string json)
    {
        string path = Path.Combine(root, directory);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "scene.json"), json);
    }

    private static void Record(string name, bool passed, ref int failures)
    {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
        if (!passed) failures++;
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("F07 fixture precondition failed.");
    }

    private sealed class FailureStore : IStudioStateStore
    {
        public RepositoryResult Load() => new(null, RepositoryError.IoError);
        public RepositoryResult Update(Func<StudioState, StudioState> transition) => Load();
    }

    private sealed class PausingStore(StudioStateRepository repository, string ready, string gate) : IStudioStateStore
    {
        public RepositoryResult Load() => repository.Load();
        public RepositoryResult Update(Func<StudioState, StudioState> transition)
        {
            File.WriteAllText(ready + ".attempt", "attempt");
            return repository.Update(state =>
            {
                File.WriteAllText(ready, "locked");
                Require(WaitForFile(gate));
                return transition(state);
            });
        }
    }
}

internal static class F07WindowsIntegration
{
    internal static int Run()
    {
        string? targetSource = Environment.GetEnvironmentVariable("DMS_F06_TARGET_PATH");
        string? hostPath = Environment.GetEnvironmentVariable("DMS_F06_HOST_PATH");
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(targetSource) || !File.Exists(targetSource) ||
            string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath))
        {
            Console.WriteLine("SKIP F07 real two-target MotionHost integration (set DMS_F06_TARGET_PATH and DMS_F06_HOST_PATH)");
            return 0;
        }

        string root = Path.Combine(Path.GetTempPath(), $"dms-f07-windows-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var ownedTargets = new List<(Process Process, nint Window)>();
        int failures = 0;
        try
        {
            var apps = new List<ApplicationProfile>();
            for (int i = 0; i < 2; i++)
            {
                string appDirectory = Path.Combine(root, $"target-{i}");
                Directory.CreateDirectory(appDirectory);
                foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(targetSource)!))
                    File.Copy(file, Path.Combine(appDirectory, Path.GetFileName(file)));
                apps.Add(new ApplicationProfile($"fixture-{i}", $"Fixture {i}", Path.Combine(appDirectory, Path.GetFileName(targetSource))));
            }
            string sceneRoot = Path.Combine(root, "scenes");
            Directory.CreateDirectory(Path.Combine(sceneRoot, "one"));
            File.WriteAllText(Path.Combine(sceneRoot, "one", "scene.json"),
                """{"schemaVersion":1,"sceneId":"scene-one","sceneVersion":"1.0.0","name":"Shared fixture","displayMode":"centered","assets":[]}""");
            var store = new StudioStateRepository(Path.Combine(root, "state.json"));
            if (store.Save(new StudioState(1, apps, [], StudioSettings.Default)) != RepositoryError.None)
                throw new InvalidOperationException("Could not seed live binding fixtures.");
            var service = new BindingService(store, () => new BuiltInSceneCatalog().Scan(sceneRoot),
                app => new ExecutableTarget(app.LaunchTarget, []));
            var launcher = new ApplicationLauncher();
            LaunchResult Dispatch(ResolvedLaunchTarget target)
            {
                LaunchResult launched = launcher.Launch(target);
                if (launched.ProcessId is int processId)
                {
                    try { ownedTargets.Add((Process.GetProcessById(processId), 0)); }
                    catch (ArgumentException) { }
                }
                return launched;
            }
            var coordinator = new MotionHostLifecycleCoordinator(
                new ApplicationInstanceCoordinator(new WindowsWindowPlatform(), Dispatch), new WindowsMotionHostProcessFactory(hostPath));
            bool correct = true;
            foreach (ApplicationProfile app in apps)
            {
                BindingMutationResult bound = service.Bind(app.AppId, "scene-one");
                BindingResolution resolved = service.Resolve(app.AppId);
                if (!bound.Succeeded || resolved.Context is not BindingLaunchContext context)
                    throw new InvalidOperationException("Live fixture binding did not resolve.");
                MotionHostLifecycleResult result = coordinator.OpenAsync(app.AppId, context.Target, context.Scene).GetAwaiter().GetResult();
                if (result.TargetResult.Process is ProcessStamp processStamp)
                {
                    int ownedIndex = ownedTargets.FindIndex(item => item.Process.Id == processStamp.ProcessId);
                    if (ownedIndex < 0) throw new InvalidOperationException("Target dispatch ownership was not recorded.");
                    Process process = ownedTargets[ownedIndex].Process;
                    nint window = result.TargetResult.WindowHandle ?? 0;
                    ownedTargets[ownedIndex] = (process, window);
                    Console.WriteLine($"  F07 {app.AppId}: scene={context.Scene.SceneId}, PID={process.Id}, HWND=0x{window.ToInt64():X}, host={result.HostOutcome}");
                    correct &= result.TargetResult.Outcome == InstanceOutcome.WindowReady &&
                               result.HostOutcome == MotionHostOutcome.HandedOff && !process.HasExited && window != 0 && IsWindowVisible(window);
                }
                else correct = false;
            }
            correct &= ownedTargets.Count == 2 && ownedTargets.Select(item => item.Process.Id).Distinct().Count() == 2 &&
                       ownedTargets.Select(item => item.Window).Distinct().Count() == 2 && ownedTargets.All(item => !item.Process.HasExited);
            Console.WriteLine($"{(correct ? "PASS" : "FAIL")} shared scene resolves and hands off to two distinct real target windows");
            if (!correct) failures++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL F07 live binding integration: {ex.GetType().Name}: {ex.Message}");
            failures++;
        }
        finally
        {
            foreach ((Process process, nint window) in ownedTargets)
            {
                try
                {
                    if (!process.HasExited && window != 0) _ = PostMessage(window, 0x0010, 0, 0);
                    if (!process.HasExited && !process.WaitForExit(2_000))
                    {
                        process.Kill(entireProcessTree: false);
                        if (!process.WaitForExit(2_000)) throw new TimeoutException("Owned target did not exit.");
                    }
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAIL F07 owned target cleanup: {ex.GetType().Name}");
                    failures++;
                }
                finally { process.Dispose(); }
            }
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"FAIL F07 owned temporary directory cleanup: {ex.GetType().Name}");
                failures++;
            }
        }
        return failures;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
