using System.Text.Json;
using DesktopMotionStudio.Core.State;

internal static class F11SettingsChecks
{
    internal static int Run()
    {
        DirectoryInfo fixture = Directory.CreateTempSubdirectory("dms-f11-settings-");
        int failures = 0;
        void Check(string name, bool passed) => F09Checks.Check("F11 settings " + name, passed, ref failures);
        try { RunCases(fixture.FullName, Check); }
        finally
        {
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (string.Equals(fixture.Parent?.FullName, parent, StringComparison.OrdinalIgnoreCase) &&
                fixture.Name.StartsWith("dms-f11-settings-", StringComparison.Ordinal))
                fixture.Delete(recursive: true);
        }
        return failures;
    }

    private static void RunCases(string root, Action<string, bool> check)
    {
        string path = Path.Combine(root, "state.json");
        var store = new StudioStateRepository(path);
        var settings = new StudioSettingsService(store);
        StudioSettings defaults = StudioSettings.Default;
        var dark = new StudioSettings("dark", true);
        var light = new StudioSettings("light", false);
        check("fresh read shows defaults without creating files", settings.Read() is { Status: SettingsStatus.Ready, Settings: { Theme: "system", Muted: false } } &&
            !File.Exists(path));
        check("invalid theme is refused before writing", settings.Apply(defaults, new("Dark", false)).Status == SettingsStatus.Invalid &&
            !File.Exists(path));

        StudioState seed = StudioState.Empty with
        {
            Applications = [new("app", "Saved fixture", "legacy-target")],
            Bindings = [new("binding", "app", "scene-one", true)]
        };
        if (store.Save(seed) != RepositoryError.None) throw new IOException("Could not create settings fixture.");
        byte[] initial = File.ReadAllBytes(path);
        check("explicit save changes settings and keeps previous bytes in backup",
            settings.Apply(defaults, dark) is { Status: SettingsStatus.Applied, Settings: { Theme: "dark", Muted: true } } &&
            File.ReadAllBytes(path + ".bak").SequenceEqual(initial));
        StudioState saved = store.Load().State!;
        check("unrelated app and binding records are exact",
            JsonSerializer.Serialize(saved.Applications) == JsonSerializer.Serialize(seed.Applications) &&
            JsonSerializer.Serialize(saved.Bindings) == JsonSerializer.Serialize(seed.Bindings) && saved.SchemaVersion == 2);
        byte[] changed = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
        check("repeat desired preferences avoid state and backup writes",
            settings.Apply(defaults, dark) is { Status: SettingsStatus.Unchanged } &&
            File.ReadAllBytes(path).SequenceEqual(changed) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup));
        check("stale preference snapshot yields current values without writing",
            settings.Apply(defaults, light) is { Status: SettingsStatus.Conflict, Settings: { Theme: "dark", Muted: true } } &&
            File.ReadAllBytes(path).SequenceEqual(changed) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup));
        check("fresh snapshot applies independent light and unmute",
            settings.Apply(dark, light).Status == SettingsStatus.Applied &&
            new StudioSettingsService(new StudioStateRepository(path)).Read().Settings == light);

        const string old = "{\"schemaVersion\":1,\"applications\":[],\"bindings\":[],\"settings\":{\"theme\":\"light\",\"muted\":false}}";
        File.WriteAllText(path, old);
        check("legacy profile reads existing settings without migration write", settings.Read().Settings == light &&
            File.ReadAllText(path) == old);
        check("explicit legacy save upgrades with original backup",
            settings.Apply(light, dark).Status == SettingsStatus.Applied &&
            store.Load().State?.SchemaVersion == 2 && File.ReadAllText(path + ".bak") == old);

        foreach (string invalid in new[] { "{\"schemaVersion\":9,\"future\":true}", "{" })
        {
            File.WriteAllText(path, invalid);
            backup = File.ReadAllBytes(path + ".bak");
            check("unsupported or malformed profile is preserved " + (invalid.Length > 1 ? "future" : "malformed"),
                settings.Read().Status == SettingsStatus.StorageError &&
                settings.Apply(defaults, dark).Status == SettingsStatus.StorageError &&
                File.ReadAllText(path) == invalid && File.ReadAllBytes(path + ".bak").SequenceEqual(backup));
        }

        var busy = new StudioSettingsService(new BusyStore());
        check("repository busy remains a structured error",
            busy.Apply(defaults, dark) is { Status: SettingsStatus.StorageError, RepositoryError: RepositoryError.Busy });
    }

    private sealed class BusyStore : IStudioStateStore
    {
        public RepositoryResult Load() => new(StudioState.Empty, RepositoryError.None);
        public RepositoryResult Update(Func<StudioState, StudioState> transition) => new(null, RepositoryError.Busy);
    }
}
