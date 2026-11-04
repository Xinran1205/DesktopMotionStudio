using System.Text;
using DesktopMotionStudio.Core.Launch;
using DesktopMotionStudio.Core.Scenes;
using DesktopMotionStudio.Core.State;

const string Valid = """
    {"schemaVersion":1,"sceneId":"dragon-portal","sceneVersion":"1.0.0","name":"Dragon Portal","displayMode":"full","assets":["cover.png","video/intro.mp4"]}
    """;

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

        var first = new StudioState(1,
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

        byte[] future = Encoding.UTF8.GetBytes("{\"schemaVersion\":2}");
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
    return validExe && missingExe && invalidWorkingDirectory && missingShortcut && nonExecutableShortcut &&
           invalidAumid && embeddedNullAumid && oversizedAumid && argumentLimit && commandLineLimit && permissionFailure && validShortcut &&
           shortcutArgumentsPreserved && argumentsPreserved && packagedTarget && missingPackagedTarget ? 0 : 1;
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
    var fakeLauncher = new ApplicationLauncher(startInfo =>
    {
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
        LaunchResult result = fakeLauncher.Launch(new ShortcutTarget(shortcutPath));
        return result.Status == LaunchStatus.Started && result.ProcessId == 4242 &&
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
