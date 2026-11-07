using System.Text;
using System.Diagnostics;
using DesktopMotionStudio.Core.Instances;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;

const string Valid = """
    {"schemaVersion":1,"sceneId":"dragon-portal","sceneVersion":"1.0.0","name":"Dragon Portal","displayMode":"full","assets":["cover.png","video/intro.mp4"]}
    """;

if (args.Length == 4 && args[0] == "--f05-instance-worker")
    return RunInstanceWorker(args[1], args[2], args[3]);
if (args.Length == 4 && args[0] == "--pipe" && args[2] == "--request-id")
    return Environment.GetEnvironmentVariable("DMS_F09_HOST_FIXTURE") is string behavior
        ? F09OwnershipChecks.HostWorker(args[1], args[3], behavior) : F09Checks.StaleHostWorker(args[1], args[3]);
if (args.Length == 2 && args[0] == "--f09-owned-parent") return F09OwnershipChecks.Parent(args[1]);
if (args.Length == 1 && args[0] == "--f09-unassigned-worker") { Thread.Sleep(Timeout.Infinite); return 0; }
if (args.Length == 1 && args[0] == "--f09-ownership-checks") return F09OwnershipChecks.Run();
if (args.Length == 1 && args[0] == "--f09-playback-process-checks") return F09PlaybackProcessChecks.Run();
if (args.Length == 3 && args[0] == "--f06-disconnect-worker")
    return RunF06DisconnectWorker(args[1], args[2]);
if (args.Length == 1 && args[0] == "--f06-desktop-acceptance")
    return DesktopMotionStudio.Core.Checks.F06DesktopAcceptance.Run();
if (args.Length == 1 && args[0] == "--f06-process-integration")
    return F06Checks.RunWindowsProcessIntegration();
if (args.Length == 6 && args[0] == "--f07-binding-worker")
    return F07Checks.RunWorker(args[1], args[2], args[3], args[4], args[5]);
if (args.Length == 1 && args[0] == "--f07-checks")
    return F07Checks.Run();
if (args.Length == 1 && args[0] == "--f08-checks")
    return F08Checks.Run();
if (args.Length == 1 && args[0] == "--f09-checks")
    return F09Checks.Run();
if (args.Length is 1 or 2 && args[0] == "--f10-launch-checks")
    return F10LaunchChecks.Run(args.Length == 2 ? args[1] : null);
if (args is ["--f10-receipt-checks"]) return F10ReceiptChecks.Run();
if (args is ["--f10-receipt-worker", var receiptRoot]) return F10ReceiptChecks.Worker(receiptRoot);
if (args is ["--f10-shortcut-checks"]) return F10ShortcutChecks.Run();
if (args is ["--f10-lifecycle-checks"]) return F10LifecycleChecks.Run();
if (args is ["--f11-settings-checks"]) return F11SettingsChecks.Run();
if (args is ["--f11-diagnostic-checks"]) return F11DiagnosticChecks.Run();
if (args is ["--f12-entry-checks"]) return F12EntryChecks.Run();
if (args is ["--binding", var observedBinding] && F12EntryChecks.TryObserve(observedBinding)) return 0;
if (args is [DesktopMotionStudio.Core.Shortcuts.TestLaunchProtocol.Command, var testBinding] && F12EntryChecks.TryObserve(testBinding))
    return (int)DesktopMotionStudio.Core.Shortcuts.TestLaunchOutcome.OpenedWithoutMotion;
if (args is ["--f12-payload-checks"]) return F12PayloadChecks.Run();
if (args is ["--f12-stage-payload", var payload]) return F12PayloadChecks.RunBuiltPayload(payload);
if (args is ["--f12-stage-install", var installPayload, var entryImage]) return F12PayloadChecks.RunBuiltStage(installPayload, entryImage);
if (args is ["--f12-install-checks"]) return F12InstallChecks.Run();
if (args is ["--f12-install-worker", var installFixture, var interrupt]) return F12InstallChecks.Worker(installFixture, interrupt);
if (args is ["--f12-cleanup-checks"]) return F12CleanupChecks.Run();
if (args is ["--f12-uninstall-checks"]) return F12InstallChecks.RunRemoval();
if (args is ["--f12-uninstall-worker", var removeFixture, var removeInterrupt]) return F12InstallChecks.RemoveWorker(removeFixture, removeInterrupt);
if (args is ["--f12-built-lifecycle", var packageA, var packageB]) return F12InstallChecks.RunBuiltLifecycle(packageA, packageB);
if (args.Length == 2 && args[0] == "--f09-catalog-check")
    return F09Checks.VerifyShippedCatalog(args[1]);

var cases = new (string Name, string Json, SceneManifestError Expected)[]
{
    ("valid", Valid, SceneManifestError.None),
    ("future schema", Valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"), SceneManifestError.UnsupportedSchema),
    ("invalid schema", Valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":0"), SceneManifestError.InvalidSchema),
    ("malformed", "{", SceneManifestError.MalformedJson),
    ("duplicate root field", Valid.Replace("\"sceneId\":", "\"sceneId\":\"other\",\"sceneId\":"), SceneManifestError.MalformedJson),
    ("missing field", Valid.Replace("\"displayMode\":\"full\",", ""), SceneManifestError.MissingField),
    ("unsafe asset", Valid.Replace("cover.png", "../cover.png"), SceneManifestError.UnsafeAssetPath),
    ("duplicate asset", Valid.Replace("video/intro.mp4", "COVER.PNG"), SceneManifestError.DuplicateAsset),
    ("bad id", Valid.Replace("dragon-portal", "A"), SceneManifestError.InvalidField),
    ("invalid type", Valid.Replace("\"name\":\"Dragon Portal\"", "\"name\":42"), SceneManifestError.InvalidField),
    ("leading zero prerelease", Valid.Replace("1.0.0", "1.0.0-01"), SceneManifestError.InvalidField),
    ("hyphen in build metadata", Valid.Replace("1.0.0", "1.0.0+build-01"), SceneManifestError.None),
    ("missing schema", Valid.Replace("\"schemaVersion\":1,", ""), SceneManifestError.MissingField)
};

int failures = 0;
foreach (var test in cases)
{
    SceneManifestReadResult result = SceneManifestReader.Read(Encoding.UTF8.GetBytes(test.Json));
    bool passed = result.Error == test.Expected && (test.Expected == SceneManifestError.None) == result.IsSuccess;
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {test.Name}: {result.Error}");
    if (!passed) failures++;
}

byte[] oversized = new byte[SceneManifestReader.MaxBytes + 1];
bool oversizedPassed = SceneManifestReader.Read(oversized).Error == SceneManifestError.TooLarge;
Console.WriteLine($"{(oversizedPassed ? "PASS" : "FAIL")} over size limit");
if (!oversizedPassed) failures++;

byte[] validBytes = Encoding.UTF8.GetBytes(Valid);
byte[] atLimit = new byte[SceneManifestReader.MaxBytes];
validBytes.CopyTo(atLimit, 0);
Array.Fill(atLimit, (byte)' ', validBytes.Length, atLimit.Length - validBytes.Length);
bool boundaryPassed = SceneManifestReader.Read(atLimit).IsSuccess;
Console.WriteLine($"{(boundaryPassed ? "PASS" : "FAIL")} exact size limit");
if (!boundaryPassed) failures++;

failures += RunRepositoryChecks();
failures += RunCatalogChecks();
failures += RunLaunchChecks();
failures += RunInstanceChecks();
failures += F06Checks.Run();
failures += F07Checks.Run();
failures += F08Checks.Run();
failures += F09Checks.Run();
failures += F10LaunchChecks.Run(null);
failures += F10ReceiptChecks.Run();
failures += F10ShortcutChecks.Run();
failures += F10LifecycleChecks.Run();
failures += F11SettingsChecks.Run();
failures += F11DiagnosticChecks.Run();
failures += F12EntryChecks.Run();
failures += F12PayloadChecks.Run();
failures += F12InstallChecks.Run();
failures += F12CleanupChecks.Run();
failures += F12InstallChecks.RunRemoval();
failures += F09OwnershipChecks.Run();
failures += F09PlaybackProcessChecks.Run();
failures += RunWindowHostIntegration();
failures += RunPackagedInstanceIntegration();

static int RunRepositoryChecks()
{
    string directory = Path.Combine(Path.GetTempPath(), $"dms-checks-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        string path = Path.Combine(directory, "studio-state.json");
        var repository = new StudioStateRepository(path);
        RepositoryResult empty = repository.Load();
        bool emptyPassed = empty.IsSuccess && empty.State == StudioState.Empty;
        Console.WriteLine($"{(emptyPassed ? "PASS" : "FAIL")} empty state default");

        var first = new StudioState(StudioState.CurrentSchemaVersion,
            [new ApplicationProfile("app-1", "Editor", "C:/Apps/editor.exe")],
            [new ApplicationBinding("binding-1", "app-1", "dragon-portal", true)], StudioSettings.Default);
        bool firstSave = repository.Save(first) == RepositoryError.None;
        StudioState? loadedFirst = firstSave ? repository.Load().State : null;
        bool roundTrip = loadedFirst is not null && loadedFirst.Applications.Count == 1 &&
                         loadedFirst.Applications[0] == first.Applications[0] &&
                         loadedFirst.Bindings.Count == 1 && loadedFirst.Bindings[0] == first.Bindings[0] &&
                         loadedFirst.Settings == first.Settings;
        Console.WriteLine($"{(roundTrip ? "PASS" : "FAIL")} state round trip");

        var second = first with { Settings = new StudioSettings("dark", true) };
        bool secondSave = repository.Save(second) == RepositoryError.None;
        bool backup = secondSave && File.Exists(path + ".bak") &&
                      JsonLooksLikePreviousState(File.ReadAllBytes(path + ".bak"));
        Console.WriteLine($"{(backup ? "PASS" : "FAIL")} previous state backup");
        var third = second with { Settings = new StudioSettings("light", true) };
        bool rotatedBackup = repository.Save(third) == RepositoryError.None &&
                             JsonHasTheme(File.ReadAllBytes(path + ".bak"), "dark");
        Console.WriteLine($"{(rotatedBackup ? "PASS" : "FAIL")} backup rotation");

        byte[] future = Encoding.UTF8.GetBytes($"{{\"schemaVersion\":{StudioState.CurrentSchemaVersion + 1}}}");
        File.WriteAllBytes(path, future);
        bool futureRejected = repository.Load().Error == RepositoryError.UnsupportedSchema;
        RepositoryError futureSaveResult = repository.Save(first);
        bool futureBytesUnchanged = File.ReadAllBytes(path).SequenceEqual(future);
        bool futureSaveRefused = futureSaveResult == RepositoryError.UnsupportedSchema && futureBytesUnchanged;
        if (!futureSaveRefused) Console.WriteLine($"  details: load={repository.Load().Error}, save={futureSaveResult}, unchanged={futureBytesUnchanged}");
        Console.WriteLine($"{(futureRejected && futureSaveRefused ? "PASS" : "FAIL")} future state preserved");

        byte[] corrupt = Encoding.UTF8.GetBytes("{");
        File.WriteAllBytes(path, corrupt);
        bool corruptRejected = repository.Load().Error == RepositoryError.InvalidJson;
        bool corruptSaveRefused = repository.Save(first) == RepositoryError.InvalidJson &&
                                  File.ReadAllBytes(path).SequenceEqual(corrupt);
        Console.WriteLine($"{(corruptRejected && corruptSaveRefused ? "PASS" : "FAIL")} corrupt state preserved");

        var badBinding = first with { Bindings = [new ApplicationBinding("b", "missing", "scene-one", true)] };
        bool invalidStateRejected = repository.Save(badBinding) == RepositoryError.InvalidState;
        Console.WriteLine($"{(invalidStateRejected ? "PASS" : "FAIL")} broken binding rejected");

        var duplicateAppBinding = first with
        {
            Bindings = [first.Bindings[0], new ApplicationBinding("binding-2", "app-1", "scene-two", true)]
        };
        bool duplicateRejected = repository.Save(duplicateAppBinding) == RepositoryError.InvalidState;
        Console.WriteLine($"{(duplicateRejected ? "PASS" : "FAIL")} duplicate app binding rejected");

        var concurrentRepository = new StudioStateRepository(Path.Combine(directory, "concurrent.json"));
        var saveResults = new System.Collections.Concurrent.ConcurrentBag<RepositoryError>();
        Parallel.For(0, 8, index =>
        {
            string theme = index % 2 == 0 ? "dark" : "light";
            saveResults.Add(concurrentRepository.Save(first with { Settings = new StudioSettings(theme, index % 2 == 0) }));
        });
        bool concurrentPassed = saveResults.Count == 8 && saveResults.All(result => result == RepositoryError.None) &&
                                concurrentRepository.Load().IsSuccess;
        Console.WriteLine($"{(concurrentPassed ? "PASS" : "FAIL")} concurrent saves");

        byte[] oversizedState = new byte[4 * 1024 * 1024 + 1];
        File.WriteAllBytes(path, oversizedState);
        bool oversizedStateRejected = repository.Load().Error == RepositoryError.InvalidState;
        Console.WriteLine($"{(oversizedStateRejected ? "PASS" : "FAIL")} oversized state rejected");

        return (emptyPassed && roundTrip && backup && futureRejected && futureSaveRefused &&
                rotatedBackup && corruptRejected && corruptSaveRefused && invalidStateRejected &&
                duplicateRejected && concurrentPassed && oversizedStateRejected) ? 0 : 1;
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static bool JsonLooksLikePreviousState(byte[] bytes)
{
    StudioState? state = System.Text.Json.JsonSerializer.Deserialize<StudioState>(bytes,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    return state?.Settings == StudioSettings.Default;
}

static bool JsonHasTheme(byte[] bytes, string theme)
{
    using var document = System.Text.Json.JsonDocument.Parse(bytes);
    return document.RootElement.GetProperty("settings").GetProperty("theme").GetString() == theme;
}

static int RunCatalogChecks()
{
    string root = Path.Combine(Path.GetTempPath(), $"dms-catalog-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        string valid = Path.Combine(root, "z-valid");
        Directory.CreateDirectory(Path.Combine(valid, "art"));
        File.WriteAllText(Path.Combine(valid, "scene.json"), Manifest("dragon-portal", "art/cover.png"));
        File.WriteAllText(Path.Combine(valid, "art", "cover.png"), "placeholder");

        foreach (string name in new[] { "a-duplicate", "b-duplicate" })
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "scene.json"), Manifest("same-scene", "art.png"));
            File.WriteAllText(Path.Combine(directory, "art.png"), "placeholder");
        }

        string missing = Path.Combine(root, "c-missing-asset");
        Directory.CreateDirectory(missing);
        File.WriteAllText(Path.Combine(missing, "scene.json"), Manifest("missing-asset", "absent.png"));
        string noManifest = Path.Combine(root, "d-no-manifest");
        Directory.CreateDirectory(noManifest);
        string bad = Path.Combine(root, "e-bad-manifest");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "scene.json"), "{");
        File.WriteAllText(Path.Combine(root, "outside-secret.txt"), "must not be read");
        string traversal = Path.Combine(root, "f-traversal");
        Directory.CreateDirectory(traversal);
        File.WriteAllText(Path.Combine(traversal, "scene.json"), Manifest("traversal-test", "../outside-secret.txt"));

        var catalog = new BuiltInSceneCatalog();
        SceneCatalogScanResult result = catalog.Scan(root);
        bool rootPassed = result.RootAvailable;
        bool stableOrder = result.Items.Select(item => item.DirectoryName).SequenceEqual(
            new[] { "a-duplicate", "b-duplicate", "c-missing-asset", "d-no-manifest", "e-bad-manifest", "f-traversal", "z-valid" });
        bool duplicateIdsRejected = result.Items.Where(item => item.Manifest?.SceneId == "same-scene")
            .All(item => item.Diagnostic == SceneCatalogDiagnostic.DuplicateSceneId);
        bool validFound = result.Items.Single(item => item.DirectoryName == "z-valid").IsAvailable;
        bool faultsIsolated = result.Items.Single(item => item.DirectoryName == "c-missing-asset").Diagnostic == SceneCatalogDiagnostic.MissingAsset &&
                             result.Items.Single(item => item.DirectoryName == "d-no-manifest").Diagnostic == SceneCatalogDiagnostic.MissingManifest &&
                             result.Items.Single(item => item.DirectoryName == "e-bad-manifest").Diagnostic == SceneCatalogDiagnostic.InvalidManifest &&
                             result.Items.Single(item => item.DirectoryName == "f-traversal").Diagnostic == SceneCatalogDiagnostic.InvalidManifest;
        bool missingRoot = !catalog.Scan(Path.Combine(root, "not-present")).RootAvailable;
        foreach ((string name, bool passed) in new[]
        {
            ("catalog root", rootPassed), ("catalog stable ordering", stableOrder),
            ("duplicate IDs unavailable", duplicateIdsRejected), ("valid resources discovered", validFound),
            ("catalog errors isolated", faultsIsolated), ("missing root reported", missingRoot)
        })
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");

        return rootPassed && stableOrder && duplicateIdsRejected && validFound && faultsIsolated && missingRoot ? 0 : 1;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static string Manifest(string sceneId, string asset) =>
    $$"""{"schemaVersion":1,"sceneId":"{{sceneId}}","sceneVersion":"1.0.0","name":"Test Scene","displayMode":"centered","assets":["{{asset}}"]}""";

static int RunLaunchChecks()
{
    var launcher = new ApplicationLauncher();
    string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
    string commandProcessor = Path.Combine(systemDirectory, "cmd.exe");
    LaunchResult directResult = File.Exists(commandProcessor)
        ? launcher.Launch(new ExecutableTarget(commandProcessor, ["/d", "/c", "exit 0"], systemDirectory))
        : new LaunchResult(LaunchStatus.InvalidTarget);
    bool validExe = directResult.Succeeded && directResult.ProcessId is not null;
    bool missingExe = launcher.Launch(new ExecutableTarget(Path.Combine(systemDirectory, "missing.exe"), [])).Status == LaunchStatus.InvalidTarget;
    bool invalidWorkingDirectory = launcher.Launch(new ExecutableTarget(commandProcessor, [], Path.Combine(systemDirectory, "dms-missing-directory"))).Status == LaunchStatus.InvalidTarget;
    bool missingShortcut = launcher.Launch(new ShortcutTarget(Path.Combine(systemDirectory, "missing.lnk"))).Status == LaunchStatus.InvalidTarget;
    bool missingResolvedExe = launcher.Launch(new ResolvedExecutableLaunch(
        new ExecutableLaunchIdentity(Path.Combine(systemDirectory, "missing-resolved.exe")), [], systemDirectory)).Status == LaunchStatus.InvalidTarget;
    bool nonExecutableShortcut = TryRejectNonExecutableShortcut(launcher);
    bool invalidAumid = launcher.Launch(new PackagedAppTarget("not-an-aumid")).Status == LaunchStatus.InvalidTarget;
    bool embeddedNullAumid = launcher.Launch(new PackagedAppTarget("Package_Family!App\0ignored")).Status == LaunchStatus.InvalidTarget;
    bool oversizedAumid = launcher.Launch(new PackagedAppTarget($"Package_Family!{new string('a', 115)}")).Status == LaunchStatus.InvalidTarget;
    bool argumentLimit = launcher.Launch(new ExecutableTarget(commandProcessor, Enumerable.Repeat("x", 257).ToArray())).Status == LaunchStatus.InvalidTarget;
    bool commandLineLimit = launcher.Launch(new ExecutableTarget(commandProcessor, [new string('x', 30_001)], systemDirectory)).Status == LaunchStatus.InvalidTarget;
    bool validShortcut = TryLaunchTestShortcut(launcher, commandProcessor, systemDirectory);
    bool shortcutArgumentsPreserved = TryPreserveShortcutArguments(commandProcessor, systemDirectory);
    var deniedLauncher = new ApplicationLauncher(_ => throw new UnauthorizedAccessException("access denied"));
    LaunchResult deniedResult = deniedLauncher.Launch(new ExecutableTarget(commandProcessor, [], systemDirectory));
    bool permissionFailure = deniedResult.Status == LaunchStatus.StartFailed && deniedResult.NativeErrorCode is not null;
    string[]? receivedArguments = null;
    int dispatchCount = 0;
    var fakeLauncher = new ApplicationLauncher(startInfo =>
    {
        dispatchCount++;
        receivedArguments = startInfo.ArgumentList.ToArray();
        return 4242;
    });
    string[] expectedArguments = ["argument with spaces", "embedded\"quote"];
    LaunchResult fakeResult = fakeLauncher.Launch(new ExecutableTarget(commandProcessor, expectedArguments, systemDirectory));
    bool argumentsPreserved = fakeResult.Succeeded && fakeResult.ProcessId == 4242 && dispatchCount == 1 &&
                              receivedArguments is not null && receivedArguments.SequenceEqual(expectedArguments);
    string? testAumid = Environment.GetEnvironmentVariable("DMS_TEST_AUMID");
    bool packagedTarget = string.IsNullOrWhiteSpace(testAumid) ||
        launcher.Launch(new PackagedAppTarget(testAumid)).Status == LaunchStatus.Activated;
    bool missingPackagedTarget = string.IsNullOrWhiteSpace(testAumid) ||
        TryRejectMissingPackagedTarget(launcher, testAumid);
    foreach ((string name, bool passed) in new[]
    {
        ("direct executable launch", validExe), ("missing executable rejected", missingExe),
        ("invalid working directory rejected", invalidWorkingDirectory), ("missing shortcut rejected", missingShortcut),
        ("invalid resolved launch rejected", missingResolvedExe),
        ("non-executable shortcut rejected", nonExecutableShortcut), ("invalid AUMID rejected", invalidAumid),
        ("embedded null AUMID rejected", embeddedNullAumid), ("oversized AUMID rejected", oversizedAumid),
        ("argument count limited", argumentLimit), ("command line length limited", commandLineLimit),
        ("permission failure classified", permissionFailure), ("resolved executable shortcut", validShortcut),
        ("shortcut arguments preserve spaces and quotes", shortcutArgumentsPreserved),
        ("arguments preserved and launched once", argumentsPreserved),
        (string.IsNullOrWhiteSpace(testAumid) ? "packaged activation skipped (no test AUMID)" : "packaged application activation", packagedTarget),
        (string.IsNullOrWhiteSpace(testAumid) ? "missing packaged target check skipped (no test AUMID)" : "missing packaged target reports HRESULT", missingPackagedTarget)
    })
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
    return validExe && missingExe && invalidWorkingDirectory && missingShortcut && missingResolvedExe && nonExecutableShortcut &&
           invalidAumid && embeddedNullAumid && oversizedAumid && argumentLimit && commandLineLimit && permissionFailure && validShortcut &&
           shortcutArgumentsPreserved && argumentsPreserved && packagedTarget && missingPackagedTarget ? 0 : 1;
}

static int RunInstanceChecks()
{
    if (!OperatingSystem.IsWindows())
    {
        Console.WriteLine("SKIP F05 Windows-only instance checks");
        return 0;
    }

    string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
    var target = new ExecutableTarget(executable, []);
    var identity = new ExecutableLaunchIdentity(Path.GetFullPath(executable));
    var resolver = new LaunchTargetIdentityResolver();
    LaunchTargetResolution exeIdentity = resolver.Resolve(target);
    LaunchTargetResolution aumidIdentity = resolver.Resolve(new PackagedAppTarget("Package_Family!Application"));
    bool identitiesResolved = exeIdentity.Succeeded && exeIdentity.Target?.Identity == identity &&
                              aumidIdentity.Target?.Identity == new PackagedAppLaunchIdentity("Package_Family!Application");

    int failures = 0;
    Record("typed EXE and exact AUMID identity resolution", identitiesResolved, ref failures);

    FakeWindowPlatform existingPlatform = new();
    ProcessStamp existing = Stamp(5001, identity);
    existingPlatform.AddProcess(existing, Candidate(existing, 101));
    int duplicateDispatches = 0;
    var existingCoordinator = Coordinator(existingPlatform, _ => { duplicateDispatches++; return new LaunchResult(LaunchStatus.Started, 7001); });
    InstanceResult activated = existingCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target).GetAwaiter().GetResult();
    Record("existing exact process activates without redispatch", activated.Outcome == InstanceOutcome.ActivatedExisting &&
        duplicateDispatches == 0 && existingPlatform.ActivationCalls == 1, ref failures);

    var unrelatedPlatform = new FakeWindowPlatform();
    var unrelated = Stamp(5002, new ExecutableLaunchIdentity(Path.Combine(Path.GetDirectoryName(executable)!, "other", "cmd.exe")));
    unrelatedPlatform.AddProcess(unrelated, Candidate(unrelated, 102));
    var launched = Stamp(5003, identity);
    int launchCount = 0;
    var unrelatedCoordinator = Coordinator(unrelatedPlatform, _ =>
    {
        launchCount++;
        unrelatedPlatform.AddProcess(launched, Candidate(launched, 103));
        return new LaunchResult(LaunchStatus.Started, launched.ProcessId, ProcessCreationFileTimeUtc: launched.CreationFileTimeUtc);
    });
    InstanceResult unrelatedResult = unrelatedCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target).GetAwaiter().GetResult();
    Record("same executable basename is not an identity match", unrelatedResult.Outcome == InstanceOutcome.WindowReady &&
        launchCount == 1 && unrelatedPlatform.ActivationCalls == 0, ref failures);

    var unusablePlatform = new FakeWindowPlatform();
    ProcessStamp unusable = Stamp(5004, identity);
    unusablePlatform.AddProcess(unusable,
        Candidate(unusable, 104, visible: false), Candidate(unusable, 105, cloaked: true),
        Candidate(unusable, 106, owned: true), Candidate(unusable, 107, tool: true));
    int unusableDispatches = 0;
    var unusableCoordinator = Coordinator(unusablePlatform, _ => { unusableDispatches++; return new LaunchResult(LaunchStatus.Started, 7002); });
    InstanceResult unusableResult = unusableCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target).GetAwaiter().GetResult();
    Record("hidden cloaked owned and tool windows are excluded", unusableResult.Outcome == InstanceOutcome.NoUsableWindow &&
        unusableDispatches == 0, ref failures);

    var ambiguousPlatform = new FakeWindowPlatform();
    ProcessStamp ambiguous = Stamp(5005, identity);
    ambiguousPlatform.AddProcess(ambiguous, Candidate(ambiguous, 108), Candidate(ambiguous, 109));
    var ambiguousCoordinator = Coordinator(ambiguousPlatform, _ => new LaunchResult(LaunchStatus.Started, 7003));
    InstanceResult ambiguousResult = ambiguousCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target).GetAwaiter().GetResult();
    Record("tied window candidates are reported as ambiguous", ambiguousResult.Outcome == InstanceOutcome.AmbiguousWindow &&
        ambiguousPlatform.ActivationCalls == 0, ref failures);

    var deniedPlatform = new FakeWindowPlatform { ActivationStatus = WindowActivationStatus.ForegroundDenied };
    ProcessStamp denied = Stamp(5006, identity);
    deniedPlatform.AddProcess(denied, Candidate(denied, 110));
    var deniedCoordinator = Coordinator(deniedPlatform, _ => new LaunchResult(LaunchStatus.Started, 7004));
    InstanceResult deniedResult = deniedCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target).GetAwaiter().GetResult();
    Record("foreground denial returns one notification outcome", deniedResult.Outcome == InstanceOutcome.ForegroundDenied &&
        deniedPlatform.ActivationCalls == 1 && deniedPlatform.NotificationCalls == 1, ref failures);

    var packagedTarget = new PackagedAppTarget("Package_Family!Application");
    var packagedIdentity = new PackagedAppLaunchIdentity(packagedTarget.Aumid);
    var packagedPlatform = new FakeWindowPlatform();
    ProcessStamp packaged = Stamp(5007, packagedIdentity);
    packagedPlatform.AddProcess(packaged, Candidate(packaged, 111));
    int packagedDispatches = 0;
    var packagedCoordinator = Coordinator(packagedPlatform, _ => { packagedDispatches++; return new LaunchResult(LaunchStatus.Activated, 7005); });
    InstanceResult packagedResult = packagedCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), packagedTarget).GetAwaiter().GetResult();
    Record("exact AUMID match activates confirmed process", packagedResult.Outcome == InstanceOutcome.ActivatedExisting &&
        packagedDispatches == 0, ref failures);

    var reusedPlatform = new FakeWindowPlatform();
    ProcessStamp reused = Stamp(5008, identity, creation: 20);
    var actualReused = Stamp(5008, identity, creation: 21);
    var reusedCoordinator = Coordinator(reusedPlatform, _ =>
    {
        reusedPlatform.AddProcess(actualReused, Candidate(actualReused, 112));
        return new LaunchResult(LaunchStatus.Started, reused.ProcessId, ProcessCreationFileTimeUtc: reused.CreationFileTimeUtc);
    });
    InstanceResult reusedResult = reusedCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target).GetAwaiter().GetResult();
    Record("reused PID with a different creation time is rejected", reusedResult.Outcome == InstanceOutcome.IdentityChanged,
        ref failures);

    var delayedPlatform = new FakeWindowPlatform();
    ProcessStamp delayed = Stamp(5009, identity);
    var delayedClock = new FakeInstanceClock();
    delayedClock.DelayHandler = (_, _) =>
    {
        if (delayedClock.ElapsedSince(0) >= TimeSpan.FromMilliseconds(200))
            delayedPlatform.SetWindows(delayed.ProcessId, Candidate(delayed, 113));
        return Task.CompletedTask;
    };
    var delayedCoordinator = Coordinator(delayedPlatform,
        _ =>
        {
            delayedPlatform.AddProcess(delayed);
            return new LaunchResult(LaunchStatus.Started, delayed.ProcessId, ProcessCreationFileTimeUtc: delayed.CreationFileTimeUtc);
        },
        delayedClock, TimeSpan.FromSeconds(15));
    InstanceResult delayedResult = delayedCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target).GetAwaiter().GetResult();
    Record("delayed window returns ready after launch", delayedResult.Outcome == InstanceOutcome.WindowReady &&
        delayedResult.ElapsedWait == TimeSpan.FromMilliseconds(200) && delayedClock.DelayCount == 2, ref failures);

    var timeoutPlatform = new FakeWindowPlatform();
    ProcessStamp timedOut = Stamp(5010, identity);
    var timeoutClock = new FakeInstanceClock();
    int timeoutDispatches = 0;
    var timeoutCoordinator = Coordinator(timeoutPlatform, _ =>
    {
        timeoutDispatches++;
        timeoutPlatform.AddProcess(timedOut);
        return new LaunchResult(LaunchStatus.Started, timedOut.ProcessId, ProcessCreationFileTimeUtc: timedOut.CreationFileTimeUtc);
    }, timeoutClock, TimeSpan.FromSeconds(15));
    InstanceResult timeoutResult = timeoutCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target).GetAwaiter().GetResult();
    Record("window wait times out at 15 seconds and leaves process running", timeoutResult.Outcome == InstanceOutcome.TimedOut &&
        timeoutResult.ElapsedWait == TimeSpan.FromSeconds(15) && timeoutDispatches == 1 && timeoutPlatform.ProcessCount == 1,
        ref failures);

    var exitedPlatform = new FakeWindowPlatform();
    ProcessStamp exited = Stamp(5011, identity);
    var exitClock = new FakeInstanceClock { DelayHandler = (_, _) => { exitedPlatform.RemoveProcess(exited.ProcessId); return Task.CompletedTask; } };
    var exitCoordinator = Coordinator(exitedPlatform, _ =>
    {
        exitedPlatform.AddProcess(exited);
        return new LaunchResult(LaunchStatus.Started, exited.ProcessId, ProcessCreationFileTimeUtc: exited.CreationFileTimeUtc);
    }, exitClock);
    InstanceResult exitResult = exitCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target).GetAwaiter().GetResult();
    Record("exited launched process reports ProcessExited", exitResult.Outcome == InstanceOutcome.ProcessExited, ref failures);

    var changedPlatform = new FakeWindowPlatform();
    ProcessStamp changed = Stamp(5012, identity);
    var changedClock = new FakeInstanceClock
    {
        DelayHandler = (_, _) =>
    { changedPlatform.ForcedVerificationStatus = ProcessVerificationStatus.IdentityChanged; return Task.CompletedTask; }
    };
    var changedCoordinator = Coordinator(changedPlatform,
        _ =>
        {
            changedPlatform.AddProcess(changed);
            return new LaunchResult(LaunchStatus.Started, changed.ProcessId, ProcessCreationFileTimeUtc: changed.CreationFileTimeUtc);
        }, changedClock);
    InstanceResult changedResult = changedCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target).GetAwaiter().GetResult();
    Record("identity change during polling is rejected", changedResult.Outcome == InstanceOutcome.IdentityChanged, ref failures);

    var canceledPlatform = new FakeWindowPlatform();
    ProcessStamp canceled = Stamp(5013, identity);
    using var cancelSource = new CancellationTokenSource();
    var cancelClock = new FakeInstanceClock { DelayHandler = (_, _) => { cancelSource.Cancel(); return Task.CompletedTask; } };
    var cancelCoordinator = Coordinator(canceledPlatform,
        _ =>
        {
            canceledPlatform.AddProcess(canceled);
            return new LaunchResult(LaunchStatus.Started, canceled.ProcessId, ProcessCreationFileTimeUtc: canceled.CreationFileTimeUtc);
        }, cancelClock);
    InstanceResult canceledResult = cancelCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target, cancelSource.Token).GetAwaiter().GetResult();
    Record("cancellation stops waiting without killing process", canceledResult.Outcome == InstanceOutcome.Canceled &&
        canceledPlatform.ProcessCount == 1, ref failures);

    var noStampPlatform = new FakeWindowPlatform();
    ProcessStamp noStamp = Stamp(5015, identity);
    var noStampCoordinator = Coordinator(noStampPlatform, _ =>
    {
        noStampPlatform.AddProcess(noStamp);
        return new LaunchResult(LaunchStatus.Started, noStamp.ProcessId);
    });
    InstanceResult noStampResult = noStampCoordinator.OpenAsync(Guid.NewGuid().ToString("N"), target).GetAwaiter().GetResult();
    Record("missing dispatch creation time fails closed", noStampResult.Outcome == InstanceOutcome.IdentityUnavailable &&
        noStampPlatform.ProcessCount == 1, ref failures);

    var concurrentPlatform = new FakeWindowPlatform();
    ProcessStamp concurrent = Stamp(5014, identity);
    int concurrentDispatches = 0;
    var concurrentCoordinator = Coordinator(concurrentPlatform, _ =>
    {
        Interlocked.Increment(ref concurrentDispatches);
        concurrentPlatform.AddProcess(concurrent, Candidate(concurrent, 114));
        return new LaunchResult(LaunchStatus.Started, concurrent.ProcessId, ProcessCreationFileTimeUtc: concurrent.CreationFileTimeUtc);
    });
    string concurrentAppId = Guid.NewGuid().ToString("N");
    Task<InstanceResult> first = concurrentCoordinator.OpenAsync(concurrentAppId, target);
    Task<InstanceResult> second = concurrentCoordinator.OpenAsync(concurrentAppId, target);
    Task.WaitAll(first, second);
    Record("same AppId concurrent requests dispatch once and rescan", concurrentDispatches == 1 &&
        new[] { first.Result.Outcome, second.Result.Outcome }.All(outcome => outcome is InstanceOutcome.WindowReady or InstanceOutcome.ActivatedExisting) &&
        first.Result.Process?.ProcessId == second.Result.Process?.ProcessId && first.Result.WindowHandle == second.Result.WindowHandle,
        ref failures);

    var independentPlatform = new FakeWindowPlatform();
    int independentDispatches = 0;
    int independentProcessId = 5020;
    using var independentLaunches = new CountdownEvent(2);
    var independentCoordinator = Coordinator(independentPlatform, _ =>
    {
        Interlocked.Increment(ref independentDispatches);
        independentLaunches.Signal();
        if (!independentLaunches.Wait(TimeSpan.FromSeconds(5))) return new LaunchResult(LaunchStatus.StartFailed);
        int processId = Interlocked.Increment(ref independentProcessId);
        ProcessStamp process = Stamp(processId, identity);
        independentPlatform.AddProcess(process, Candidate(process, processId + 1000));
        return new LaunchResult(LaunchStatus.Started, processId, ProcessCreationFileTimeUtc: process.CreationFileTimeUtc);
    });
    Task<InstanceResult> independentA = independentCoordinator.OpenAsync($"{Guid.NewGuid():N}-a", target);
    Task<InstanceResult> independentB = independentCoordinator.OpenAsync($"{Guid.NewGuid():N}-b", target);
    Task.WaitAll(independentA, independentB);
    Record("different AppIds can dispatch independently", independentDispatches == 2 &&
        independentA.Result.Outcome == InstanceOutcome.WindowReady && independentB.Result.Outcome == InstanceOutcome.WindowReady,
        ref failures);

    var gatePlatform = new FakeWindowPlatform();
    ProcessStamp gateProcess = Stamp(5030, identity);
    using var dispatchEntered = new ManualResetEventSlim();
    var gateHolder = Coordinator(gatePlatform, _ =>
    {
        dispatchEntered.Set();
        Thread.Sleep(300);
        gatePlatform.AddProcess(gateProcess, Candidate(gateProcess, 5031));
        return new LaunchResult(LaunchStatus.Started, gateProcess.ProcessId,
            ProcessCreationFileTimeUtc: gateProcess.CreationFileTimeUtc);
    });
    string gateAppId = Guid.NewGuid().ToString("N");
    Task<InstanceResult> gateHolderTask = Task.Run(() => gateHolder.OpenAsync(gateAppId, target).GetAwaiter().GetResult());
    bool enteredDispatch = dispatchEntered.Wait(TimeSpan.FromSeconds(5));
    var gateWaiter = new ApplicationInstanceCoordinator(gatePlatform, _ => new LaunchResult(LaunchStatus.Started, 5032),
        gateWait: TimeSpan.FromMilliseconds(50));
    InstanceResult gateTimeoutResult = gateWaiter.OpenAsync(gateAppId, target).GetAwaiter().GetResult();
    bool holderCompleted = gateHolderTask.Wait(TimeSpan.FromSeconds(5));
    Record("same-AppId gate wait has a bounded timeout", enteredDispatch && holderCompleted &&
        gateTimeoutResult.Outcome == InstanceOutcome.GateTimedOut && gateHolderTask.Result.Outcome == InstanceOutcome.WindowReady,
        ref failures);

    string processGateDirectory = Path.Combine(Path.GetTempPath(), $"dms-f05-process-gate-{Guid.NewGuid():N}");
    Directory.CreateDirectory(processGateDirectory);
    try
    {
        string appId = Guid.NewGuid().ToString("N");
        string? processPath = Environment.ProcessPath;
        if (processPath is null) throw new InvalidOperationException("Test process executable path is unavailable.");
        ProcessStartInfo MakeWorker(string role, string id)
        {
            bool isDotnetHost = string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
            var start = new ProcessStartInfo(processPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (isDotnetHost) start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--f05-instance-worker");
            start.ArgumentList.Add(processGateDirectory);
            start.ArgumentList.Add(id);
            start.ArgumentList.Add(role);
            start.Environment["DOTNET_ROOT"] = "C:\\Users\\AAA\\.dotnet";
            return start;
        }

        using Process workerA = Process.Start(MakeWorker("a", appId)) ?? throw new InvalidOperationException("Worker A did not start.");
        using Process workerB = Process.Start(MakeWorker("b", appId)) ?? throw new InvalidOperationException("Worker B did not start.");
        Task<string> outputA = workerA.StandardOutput.ReadToEndAsync();
        Task<string> errorA = workerA.StandardError.ReadToEndAsync();
        Task<string> outputB = workerB.StandardOutput.ReadToEndAsync();
        Task<string> errorB = workerB.StandardError.ReadToEndAsync();
        bool exitedA = workerA.WaitForExit(10_000);
        bool exitedB = workerB.WaitForExit(10_000);
        string outcomeA = File.Exists(Path.Combine(processGateDirectory, "result-a.txt"))
            ? File.ReadAllText(Path.Combine(processGateDirectory, "result-a.txt")) : "missing";
        string outcomeB = File.Exists(Path.Combine(processGateDirectory, "result-b.txt"))
            ? File.ReadAllText(Path.Combine(processGateDirectory, "result-b.txt")) : "missing";
        bool separateProcessPass = exitedA && exitedB && workerA.ExitCode == 0 && workerB.ExitCode == 0 &&
            File.Exists(Path.Combine(processGateDirectory, "dispatched.once")) &&
            outcomeA is nameof(InstanceOutcome.WindowReady) or nameof(InstanceOutcome.ActivatedExisting) &&
            outcomeB is nameof(InstanceOutcome.WindowReady) or nameof(InstanceOutcome.ActivatedExisting);
        if (!separateProcessPass)
            Console.WriteLine($"  worker details: A={workerA.ExitCode}/{outcomeA} {outputA.Result}{errorA.Result}; B={workerB.ExitCode}/{outcomeB} {outputB.Result}{errorB.Result}");
        Record("same AppId is serialized across separate coordinator processes", separateProcessPass, ref failures);

        string abandonedAppId = Guid.NewGuid().ToString("N");
        using Process crashed = Process.Start(MakeWorker("crash", abandonedAppId)) ??
            throw new InvalidOperationException("Crash worker did not start.");
        try
        {
            bool held = SpinWait.SpinUntil(() => File.Exists(Path.Combine(processGateDirectory, "owner-ready")) || crashed.HasExited,
                TimeSpan.FromSeconds(5)) && !crashed.HasExited;
            using WaitHandle retained = ApplicationInstanceCoordinator.CreateGate(abandonedAppId);
            if (!crashed.HasExited) crashed.Kill();
            bool stopped = crashed.WaitForExit(5000);
            var recoveredPlatform = new FakeWindowPlatform();
            ProcessStamp recoveredProcess = Stamp(5040, identity);
            int recoveryDispatches = 0;
            var recovery = new ApplicationInstanceCoordinator(recoveredPlatform, _ =>
            {
                recoveryDispatches++;
                recoveredPlatform.AddProcess(recoveredProcess, Candidate(recoveredProcess, 5041));
                return new LaunchResult(LaunchStatus.Started, recoveredProcess.ProcessId,
                    ProcessCreationFileTimeUtc: recoveredProcess.CreationFileTimeUtc);
            }, gateWait: TimeSpan.FromSeconds(2));
            InstanceResult afterCrash = recovery.OpenAsync(abandonedAppId, target).GetAwaiter().GetResult();
            Record("abandoned AppId gate recovers with another process holding its handle",
                held && stopped && afterCrash.Outcome == InstanceOutcome.WindowReady && recoveryDispatches == 1, ref failures);
        }
        finally
        {
            if (!crashed.HasExited)
            {
                crashed.Kill();
                crashed.WaitForExit(5000);
            }
        }
    }
    finally { Directory.Delete(processGateDirectory, recursive: true); }

    return failures;
}

static int RunInstanceWorker(string stateDirectory, string appId, string role)
{
    try
    {
        var platform = new FileBackedWindowPlatform(stateDirectory);
        var coordinator = new ApplicationInstanceCoordinator(role == "crash" ? new FakeWindowPlatform() : platform,
            _ => platform.DispatchOnce());
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        InstanceResult result = coordinator.OpenAsync(appId, new ExecutableTarget(executable, []),
            beforeDispatch: role == "crash" ? () =>
            {
                File.WriteAllText(Path.Combine(stateDirectory, "owner-ready"), "");
                Thread.Sleep(Timeout.Infinite);
            } : null).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(stateDirectory, $"result-{role}.txt"), result.Outcome.ToString());
        Console.WriteLine(result.Outcome);
        return result.Outcome is InstanceOutcome.WindowReady or InstanceOutcome.ActivatedExisting ? 0 : 1;
    }
    catch (Exception ex)
    {
        File.WriteAllText(Path.Combine(stateDirectory, $"result-{role}.txt"), $"error:{ex.GetType().Name}");
        Console.Error.WriteLine(ex);
        return 1;
    }
}

static int RunF06DisconnectWorker(string hostExecutablePath, string visibleDelayText)
{
    if (!OperatingSystem.IsWindows() || !int.TryParse(visibleDelayText, out int visibleDelayMs) ||
        visibleDelayMs is < 0 or > 10_000)
        return 2;
    var factory = new DesktopMotionStudio.Core.MotionHost.WindowsMotionHostProcessFactory(hostExecutablePath);
    _ = factory.Start(new DesktopMotionStudio.Core.MotionHost.MotionHostStartRequest(
        Guid.NewGuid(), "dragon-portal", "1.0.0", SceneDisplayMode.Centered));
    Thread.Sleep(visibleDelayMs);
    Environment.Exit(0);
    return 0;
}

static int RunWindowHostIntegration()
{
    if (!OperatingSystem.IsWindows()) return 0;
    string publishedHost = Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "src", "DesktopMotionStudio.WindowHost.Poc", "bin", "Release",
        "net10.0-windows", "win-x64", "publish", "DesktopMotionStudio.WindowHost.Poc.exe");
    publishedHost = Path.GetFullPath(publishedHost);
    if (!File.Exists(publishedHost))
    {
        Console.WriteLine("SKIP real WindowHost integration (published POC is not built)");
        return 0;
    }

    string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"dms-f05-window-{Guid.NewGuid():N}");
    Directory.CreateDirectory(temporaryDirectory);
    string testExecutable = Path.Combine(temporaryDirectory, "WindowHost.Poc.exe");
    File.Copy(publishedHost, testExecutable);
    var launcher = new ApplicationLauncher();
    var platform = new WindowsWindowPlatform();
    var coordinator = new ApplicationInstanceCoordinator(platform, launcher.Launch);
    InstanceResult? result = null;
    InstanceResult? reopened = null;
    Process? host = null;
    bool exited = false;
    int failures = 0;
    try
    {
        var target = new ExecutableTarget(testExecutable, [], temporaryDirectory);
        string appId = Guid.NewGuid().ToString("N");
        result = coordinator.OpenAsync(appId, target).GetAwaiter().GetResult();
        if (result.Process is not null)
        {
            try { host = Process.GetProcessById(result.Process.ProcessId); }
            catch (ArgumentException) { }
        }

        if (host is not null && result.WindowHandle is nint handle)
        {
            _ = NativeTestMethods.ShowWindowAsync(handle, 6); // SW_MINIMIZE
            reopened = coordinator.OpenAsync(appId, target).GetAwaiter().GetResult();
            Thread.Sleep(250); // Allow asynchronous restore to reach the window manager.
            bool restored = !NativeTestMethods.IsIconic(handle);
            bool selectedSameWindow = reopened.Outcome is InstanceOutcome.ActivatedExisting or InstanceOutcome.ForegroundDenied &&
                reopened.Process?.ProcessId == result.Process?.ProcessId && reopened.WindowHandle == result.WindowHandle && restored;
            Record($"real WindowHost restore and existing-instance activation ({reopened.Outcome})", selectedSameWindow,
                ref failures);

            _ = NativeTestMethods.PostMessage(handle, 0x0010, 0, 0); // WM_CLOSE; exercise the application's normal close path.
            exited = host.WaitForExit(5_000);
        }
        if (!exited && host is not null)
        {
            _ = host.CloseMainWindow();
            exited = host.WaitForExit(3_000);
        }

        bool passed = result.Outcome == InstanceOutcome.WindowReady && result.Process is not null &&
            string.Equals(result.Process.Identity.Value, Path.GetFullPath(testExecutable), StringComparison.OrdinalIgnoreCase) &&
            result.WindowHandle is not null && host is not null && host.Id == result.Process.ProcessId && exited;
        Record($"real WindowHost coordinator integration (PID {result.Process?.ProcessId}, HWND {result.WindowHandle})", passed,
            ref failures);
        if (!passed && failures == 0) failures++;
        return failures;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL real WindowHost coordinator integration: {ex.GetType().Name}: {ex.Message}");
        return 1;
    }
    finally
    {
        host?.Dispose();
        if ((exited || host is null) && Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
        else if (Directory.Exists(temporaryDirectory)) Console.WriteLine($"  test host cleanup pending at {temporaryDirectory}");
    }
}

static int RunPackagedInstanceIntegration()
{
    if (!OperatingSystem.IsWindows()) return 0;
    string? aumid = Environment.GetEnvironmentVariable("DMS_F05_TEST_AUMID");
    if (string.IsNullOrWhiteSpace(aumid))
    {
        Console.WriteLine("SKIP real packaged-instance integration (DMS_F05_TEST_AUMID is unset)");
        return 0;
    }

    var identity = new PackagedAppLaunchIdentity(aumid);
    var platform = new WindowsWindowPlatform();
    ProcessScanResult before = platform.FindMatchingProcesses(identity);
    if (!before.IsComplete)
    {
        Console.WriteLine("FAIL real packaged-instance integration: process scan unavailable");
        return 1;
    }
    if (before.Matches.Count > 0)
    {
        Console.WriteLine("SKIP real packaged-instance integration (matching app is already running)");
        return 0;
    }

    var launcher = new ApplicationLauncher();
    var coordinator = new ApplicationInstanceCoordinator(platform, launcher.Launch);
    string appId = $"f05-packaged-{Guid.NewGuid():N}";
    InstanceResult? result = null;
    Process? targetProcess = null;
    bool exited = false;
    try
    {
        result = coordinator.OpenAsync(appId, new PackagedAppTarget(aumid)).GetAwaiter().GetResult();
        if (result.Process is not null)
        {
            try { targetProcess = Process.GetProcessById(result.Process.ProcessId); }
            catch (ArgumentException) { }
        }

        if (targetProcess is not null)
        {
            if (result.WindowHandle is nint handle)
                _ = NativeTestMethods.PostMessage(handle, 0x0010, 0, 0);
            else
                _ = targetProcess.CloseMainWindow();
            exited = targetProcess.WaitForExit(5_000);
            if (!exited)
            {
                _ = targetProcess.CloseMainWindow();
                exited = targetProcess.WaitForExit(3_000);
            }
        }

        bool passed = result.Outcome == InstanceOutcome.WindowReady && result.Process is not null &&
            result.Process.Identity is PackagedAppLaunchIdentity returnedIdentity &&
            string.Equals(returnedIdentity.Aumid, aumid, StringComparison.OrdinalIgnoreCase) &&
            result.WindowHandle is not null && targetProcess?.Id == result.Process.ProcessId && exited;
        int failures = 0;
        Record($"real packaged AUMID instance integration ({result.Outcome}, PID {result.Process?.ProcessId})", passed,
            ref failures);
        return failures;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL real packaged-instance integration: {ex.GetType().Name}: {ex.Message}");
        return 1;
    }
    finally
    {
        targetProcess?.Dispose();
    }
}

static ApplicationInstanceCoordinator Coordinator(
    FakeWindowPlatform platform,
    Func<ResolvedLaunchTarget, LaunchResult> launch,
    IInstanceClock? clock = null,
    TimeSpan? windowWait = null) =>
    new(platform, launch, clock: clock, windowWait: windowWait);

static ProcessStamp Stamp(int processId, LaunchTargetIdentity identity, long creation = 10) =>
    new(processId, creation, 1, identity);

static WindowCandidate Candidate(
    ProcessStamp process,
    nint handle,
    bool visible = true,
    bool cloaked = false,
    bool minimized = false,
    bool owned = false,
    bool tool = false,
    bool foreground = false,
    bool main = true) =>
    new(handle, process, visible, cloaked, minimized, owned, tool, foreground, main);

static void Record(string name, bool passed, ref int failures)
{
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
    if (!passed) failures++;
}

static bool TryRejectMissingPackagedTarget(ApplicationLauncher launcher, string knownAumid)
{
    int delimiter = knownAumid.IndexOf('!');
    if (delimiter <= 0) return false;
    string missingAumid = $"{knownAumid[..(delimiter + 1)]}DMSMissing{Guid.NewGuid():N}";
    LaunchResult result = launcher.Launch(new PackagedAppTarget(missingAumid));
    return result.Status == LaunchStatus.StartFailed && result.NativeErrorCode is not null;
}

static bool TryRejectNonExecutableShortcut(ApplicationLauncher launcher)
{
    if (!OperatingSystem.IsWindows()) return false;
    string root = Path.Combine(Path.GetTempPath(), $"dms-link-{Guid.NewGuid():N}");
    string targetPath = Path.Combine(root, "readme.txt");
    string shortcutPath = Path.Combine(root, "readme.lnk");
    object? shell = null;
    object? shortcut = null;
    try
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(targetPath, "not an executable");
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: false);
        if (shellType is null) return false;
        shell = Activator.CreateInstance(shellType);
        if (shell is null) return false;
        shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
            null, shell, [shortcutPath]);
        if (shortcut is null) return false;
        SetComProperty(shortcut.GetType(), shortcut, "TargetPath", targetPath);
        shortcut.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        return launcher.Launch(new ShortcutTarget(shortcutPath)).Status == LaunchStatus.InvalidTarget;
    }
    catch (Exception) { return false; }
    finally
    {
        if (shortcut is not null && System.Runtime.InteropServices.Marshal.IsComObject(shortcut))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        if (shell is not null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static bool TryLaunchTestShortcut(ApplicationLauncher launcher, string target, string workingDirectory)
{
    if (!OperatingSystem.IsWindows()) return false;
    string shortcutPath = Path.Combine(Path.GetTempPath(), $"dms-launch-{Guid.NewGuid():N}.lnk");
    object? shell = null;
    object? shortcut = null;
    try
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: false);
        if (shellType is null) return false;
        shell = Activator.CreateInstance(shellType);
        if (shell is null) return false;
        shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
            null, shell, [shortcutPath]);
        if (shortcut is null) return false;
        Type shortcutType = shortcut.GetType();
        SetComProperty(shortcutType, shortcut, "TargetPath", target);
        SetComProperty(shortcutType, shortcut, "Arguments", "/d /c \"exit 0\"");
        SetComProperty(shortcutType, shortcut, "WorkingDirectory", workingDirectory);
        shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        LaunchResult result = launcher.Launch(new ShortcutTarget(shortcutPath));
        return result.Succeeded && result.ProcessId is not null;
    }
    catch (Exception) { return false; }
    finally
    {
        if (shortcut is not null && System.Runtime.InteropServices.Marshal.IsComObject(shortcut))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        if (shell is not null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
    }
}

static bool TryPreserveShortcutArguments(string target, string workingDirectory)
{
    if (!OperatingSystem.IsWindows()) return false;
    string shortcutPath = Path.Combine(Path.GetTempPath(), $"dms-args-{Guid.NewGuid():N}.lnk");
    object? shell = null;
    object? shortcut = null;
    string[]? receivedArguments = null;
    int dispatches = 0;
    var fakeLauncher = new ApplicationLauncher(startInfo =>
    {
        dispatches++;
        receivedArguments = startInfo.ArgumentList.ToArray();
        return 4242;
    });
    string[] expectedArguments = ["/d", "/c", "text with spaces and \"quotes\""];
    try
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: false);
        if (shellType is null) return false;
        shell = Activator.CreateInstance(shellType);
        if (shell is null) return false;
        shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
            null, shell, [shortcutPath]);
        if (shortcut is null) return false;
        Type shortcutType = shortcut.GetType();
        SetComProperty(shortcutType, shortcut, "TargetPath", target);
        SetComProperty(shortcutType, shortcut, "Arguments", "/d /c \"text with spaces and \\\"quotes\\\"\"");
        SetComProperty(shortcutType, shortcut, "WorkingDirectory", workingDirectory);
        shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        var resolver = new LaunchTargetIdentityResolver();
        LaunchTargetResolution resolution = resolver.Resolve(new ShortcutTarget(shortcutPath));
        bool sideEffectFree = resolution.Succeeded && dispatches == 0 &&
            resolution.Target is ResolvedExecutableLaunch resolved &&
            string.Equals(resolved.ExecutableIdentity.Path, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
        if (!sideEffectFree || resolution.Target is null) return false;
        LaunchResult result = fakeLauncher.Launch(resolution.Target);
        return sideEffectFree && result.Status == LaunchStatus.Started && result.ProcessId == 4242 && dispatches == 1 &&
               receivedArguments is not null && receivedArguments.SequenceEqual(expectedArguments);
    }
    catch (Exception) { return false; }
    finally
    {
        if (shortcut is not null && System.Runtime.InteropServices.Marshal.IsComObject(shortcut))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        if (shell is not null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
    }
}

static void SetComProperty(Type type, object target, string property, string value) =>
    type.InvokeMember(property, System.Reflection.BindingFlags.SetProperty, null, target, [value]);


return failures == 0 ? 0 : 1;
