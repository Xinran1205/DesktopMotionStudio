using System.Text.Json;

namespace DesktopMotionStudio.Core.State;

public sealed class StudioStateRepository(string filePath)
{
    private static readonly object SaveGate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly string _filePath = Path.GetFullPath(filePath);

    public static StudioStateRepository ForCurrentUser()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new StudioStateRepository(Path.Combine(root, "DesktopMotionStudio", "state", "studio-state.json"));
    }

    public RepositoryResult Load()
    {
        if (!File.Exists(_filePath)) return new RepositoryResult(StudioState.Empty, RepositoryError.None);
        try
        {
            if (new FileInfo(_filePath).Length > 4 * 1024 * 1024)
                return Fail(RepositoryError.InvalidState);
            byte[] bytes = File.ReadAllBytes(_filePath);
            if (bytes.Length > 4 * 1024 * 1024) return Fail(RepositoryError.InvalidState);
            StudioState? state = JsonSerializer.Deserialize<StudioState>(bytes, JsonOptions);
            if (state is null) return Fail(RepositoryError.InvalidJson);
            if (state.SchemaVersion > StudioState.CurrentSchemaVersion) return Fail(RepositoryError.UnsupportedSchema);
            if (state.SchemaVersion != StudioState.CurrentSchemaVersion) return Fail(RepositoryError.InvalidState);
            return Validate(state) ? new RepositoryResult(state, RepositoryError.None) : Fail(RepositoryError.InvalidState);
        }
        catch (JsonException) { return Fail(RepositoryError.InvalidJson); }
        catch (IOException) { return Fail(RepositoryError.IoError); }
        catch (UnauthorizedAccessException) { return Fail(RepositoryError.IoError); }
    }

    public RepositoryError Save(StudioState state)
    {
        lock (SaveGate) return SaveCore(state);
    }

    private RepositoryError SaveCore(StudioState state)
    {
        if (!Validate(state)) return RepositoryError.InvalidState;
        if (File.Exists(_filePath))
        {
            RepositoryResult current = Load();
            if (!current.IsSuccess) return current.Error;
        }
        string? tempPath = null;
        try
        {
            string directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
            if (bytes.Length > 4 * 1024 * 1024) return RepositoryError.InvalidState;
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_filePath))
                File.Replace(tempPath, _filePath, _filePath + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(tempPath, _filePath);
            tempPath = null;
            return RepositoryError.None;
        }
        catch (IOException) { return RepositoryError.IoError; }
        catch (UnauthorizedAccessException) { return RepositoryError.IoError; }
        finally
        {
            if (tempPath is not null)
                try { File.Delete(tempPath); } catch (IOException) { }
        }
    }

    private static bool Validate(StudioState state)
    {
        if (state is null || state.SchemaVersion != StudioState.CurrentSchemaVersion ||
            state.Applications is null || state.Bindings is null || state.Settings is null ||
            state.Applications.Count > 1000 || state.Bindings.Count > 5000 ||
            state.Settings.Theme is not ("system" or "light" or "dark")) return false;

        var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ApplicationProfile? app in state.Applications)
            if (app is null || string.IsNullOrWhiteSpace(app.AppId) || app.AppId.Length > 128 ||
                string.IsNullOrWhiteSpace(app.DisplayName) || app.DisplayName.Length > 120 ||
                string.IsNullOrWhiteSpace(app.LaunchTarget) || app.LaunchTarget.Length > 2048 || !apps.Add(app.AppId)) return false;

        var bindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var boundApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ApplicationBinding? binding in state.Bindings)
            if (binding is null || string.IsNullOrWhiteSpace(binding.BindingId) || binding.BindingId.Length > 128 ||
                string.IsNullOrWhiteSpace(binding.AppId) || !apps.Contains(binding.AppId) ||
                !DesktopMotionStudio.Core.Scenes.SceneManifestReader.IsValidSceneId(binding.SceneId) ||
                !bindings.Add(binding.BindingId) || !boundApps.Add(binding.AppId)) return false;
        return true;
    }

    private static RepositoryResult Fail(RepositoryError error) => new(null, error);
}
