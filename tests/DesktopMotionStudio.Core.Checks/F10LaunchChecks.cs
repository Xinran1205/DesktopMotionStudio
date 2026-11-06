using DesktopMotionStudio.Core.Applications;
using DesktopMotionStudio.Core.Shortcuts;
using DesktopMotionStudio.Core.State;
using System.Runtime.InteropServices;

internal static class F10LaunchChecks
{
    private const string LegacyId = "既有 Binding / 01";

    internal static int Run(string? launcherPath)
    {
        int failures = 0;
        void Check(string description, bool passed) => F09Checks.Check("F10 " + description, passed, ref failures);

        string token = ManagedLaunchRequest.EncodeBindingId(LegacyId)!;
        Check("legacy binding ID round-trips as one shell-safe token",
            ManagedLaunchRequest.ParseArguments(["--binding", token]) == LegacyId &&
            token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
        Check("128 three-byte UTF-16 code units fit the token bound",
            ManagedLaunchRequest.EncodeBindingId(new string('中', 128)) is { Length: 512 } max &&
            ManagedLaunchRequest.ParseArguments(["--binding", max]) == new string('中', 128));
        Check("blank, too long and invalid Unicode IDs rejected",
            new[] { "", " ", new string('a', 129), "\ud800" }.All(id => ManagedLaunchRequest.EncodeBindingId(id) is null));
        Check("extra, reordered and repeated arguments rejected",
            ManagedLaunchRequest.ParseArguments(["--binding", token, "--state-file", "fixture.json"]) is null &&
            ManagedLaunchRequest.ParseArguments([token, "--binding"]) is null &&
            ManagedLaunchRequest.ParseArguments(["--binding", token, "--binding", token]) is null);
        Check("malformed, padded, oversized and noncanonical tokens rejected",
            new[] { "", "YQ=", "YR", "_w", "Y Q", new string('a', 513), "C:\\fixture.exe" }
                .All(candidate => ManagedLaunchRequest.ParseArguments(["--binding", candidate]) is null));

        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f10-launch-");
        try { CheckResolution(fixture.FullName, launcherPath, Check); }
        finally
        {
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, temp, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f10-launch-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
        return failures;
    }

    private static void CheckResolution(string root, string? launcherPath, Action<string, bool> check)
    {
        const string legacyId = LegacyId;
        string catalog = Path.Combine(root, "Scenes");
        string scene = F09Checks.WriteScene(catalog, "fixture");
        string path = Path.Combine(root, "target.exe");
        File.WriteAllBytes(path, [1]);
        var app = new ApplicationProfile("fixture-app", "Fixture", path, new(ApplicationTargetKind.Executable, path, ["--test"]));
        var binding = new ApplicationBinding(legacyId, app.AppId, "studio-pulse", true);
        StudioState state = StudioState.Empty with { Applications = [app], Bindings = [binding] };
        var store = new FixtureStore(state);
        var resolver = new ManagedLaunchRequest(store, catalog);

        check("one state snapshot selects current manifest and playback with typed target",
            resolver.Resolve(legacyId) is { Status: ManagedLaunchStatus.Ready, Scene.SceneId: "studio-pulse", Playback.IsValid: true } && store.Reads == 1);
        store.State = state with { Settings = state.Settings with { Muted = true } };
        check("audio mute keeps silent animation", resolver.Resolve(legacyId).Playback is not null);
        store.State = state with { Bindings = [binding with { Enabled = false }] };
        check("disabled motion keeps native target", resolver.Resolve(legacyId) is { Status: ManagedLaunchStatus.Ready, Target: not null, Scene: null, Playback: null });
        store.State = state;
        File.WriteAllText(Path.Combine(scene, "scene.playback.json"), "{");
        check("malformed playback keeps native target", resolver.Resolve(legacyId) is { Status: ManagedLaunchStatus.Ready, Target: not null, Scene: null, Playback: null });
        F09Checks.WriteScene(catalog, "other", "alternate-scene");
        store.State = state with { Bindings = [binding with { SceneId = "alternate-scene" }] };
        check("same binding ID resolves the replaced scene", resolver.Resolve(legacyId).Playback?.SceneId == "alternate-scene");

        check("unknown binding does not return a target", resolver.Resolve("unknown") is { Status: ManagedLaunchStatus.BindingMissing, Target: null });
        store.State = state with { Applications = [] };
        check("missing application does not return a target", resolver.Resolve(legacyId) is { Status: ManagedLaunchStatus.TargetUnavailable, Target: null });
        store.State = state;
        File.Delete(path);
        check("missing executable does not return a target", resolver.Resolve(legacyId) is { Status: ManagedLaunchStatus.TargetUnavailable, Target: null });
        store.Error = RepositoryError.UnsupportedSchema;
        check("future profile returns structured error", resolver.Resolve(legacyId) is { Status: ManagedLaunchStatus.StorageError, StorageError: RepositoryError.UnsupportedSchema, Target: null });
        store.Error = RepositoryError.None;

        string recursivePath = Path.Combine(root, "DesktopMotionStudio.Launcher.exe");
        File.WriteAllBytes(recursivePath, [1]);
        store.State = state with { Applications = [app with { Target = new(ApplicationTargetKind.Executable, recursivePath, []) }] };
        check("product executable is rejected before dispatch", resolver.Resolve(legacyId) is { Status: ManagedLaunchStatus.RecursiveTarget, Target: null });
        if (launcherPath is not null)
        {
            string alias = Path.Combine(root, "renamed apphost.exe");
            File.Copy(launcherPath, alias);
            store.State = state with { Applications = [app with { Target = new(ApplicationTargetKind.Executable, alias, []) }] };
            check("renamed copy of published Launcher rejected by apphost metadata",
                resolver.Resolve(legacyId) is { Status: ManagedLaunchStatus.RecursiveTarget, Target: null });
            if (OperatingSystem.IsWindows())
            {
                string linkPath = Path.Combine(root, "recursive link.lnk");
                WriteLink(linkPath, alias);
                store.State = state with { Applications = [app with { Target = new(ApplicationTargetKind.Shortcut, linkPath, []) }] };
                check("real shortcut resolving to a renamed Launcher is rejected",
                    resolver.Resolve(legacyId) is { Status: ManagedLaunchStatus.RecursiveTarget, Target: null });
            }
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void WriteLink(string path, string target)
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            object link = ((dynamic)shell).CreateShortcut(path);
            try
            {
                ((dynamic)link).TargetPath = target;
                ((dynamic)link).Save();
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private sealed class FixtureStore(StudioState state) : IStudioStateStore
    {
        public StudioState State { get; set; } = state;
        public RepositoryError Error { get; set; }
        public int Reads { get; private set; }

        public RepositoryResult Load()
        {
            Reads++;
            return Error == RepositoryError.None ? new(State, Error) : new(null, Error);
        }

        public RepositoryResult Update(Func<StudioState, StudioState> transition) =>
            throw new InvalidOperationException("Launch resolution must be read-only.");
    }
}
