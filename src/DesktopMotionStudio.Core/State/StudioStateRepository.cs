using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DesktopMotionStudio.Core.Applications;

namespace DesktopMotionStudio.Core.State;

public sealed class StudioStateRepository(string filePath) : IStudioStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter<ApplicationTargetKind>(allowIntegerValues: false) }
    };
    private readonly string _filePath = Path.GetFullPath(filePath);

    public static StudioStateRepository ForCurrentUser() => new(CurrentUserPath());

    public static string CurrentUserPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "DesktopMotionStudio", "state", "studio-state.json");
    }

    public RepositoryResult Load()
    {
        try
        {
            using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            if (stream.Length > 4 * 1024 * 1024) return Fail(RepositoryError.InvalidState);
            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) return Fail(RepositoryError.InvalidState);
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            if (HasDuplicateMembers(document.RootElement)) return Fail(RepositoryError.InvalidJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schemaVersion", out JsonElement version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int schema))
                return Fail(RepositoryError.InvalidState);
            if (schema > StudioState.CurrentSchemaVersion) return Fail(RepositoryError.UnsupportedSchema);
            if (schema is not (1 or StudioState.CurrentSchemaVersion)) return Fail(RepositoryError.InvalidState);
            StudioState? state = JsonSerializer.Deserialize<StudioState>(bytes, JsonOptions);
            if (state is null) return Fail(RepositoryError.InvalidJson);
            if (schema == 1)
            {
                if (state.Applications is null || state.Applications.Any(app => app is null || app.Target is not null))
                    return Fail(RepositoryError.InvalidState);
                state = state with { SchemaVersion = StudioState.CurrentSchemaVersion };
            }
            return Validate(state) ? new RepositoryResult(state, RepositoryError.None) : Fail(RepositoryError.InvalidState);
        }
        catch (FileNotFoundException) { return new RepositoryResult(StudioState.Empty, RepositoryError.None); }
        catch (DirectoryNotFoundException) { return new RepositoryResult(StudioState.Empty, RepositoryError.None); }
        catch (JsonException) { return Fail(RepositoryError.InvalidJson); }
        catch (IOException) { return Fail(RepositoryError.IoError); }
        catch (UnauthorizedAccessException) { return Fail(RepositoryError.IoError); }
    }

    public RepositoryError Save(StudioState state)
    {
        return WithWriteLock(() =>
        {
            RepositoryError error = SaveCore(state);
            return error == RepositoryError.None ? new RepositoryResult(state, error) : Fail(error);
        }).Error;
    }

    public RepositoryResult Update(Func<StudioState, StudioState> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return WithWriteLock(() =>
        {
            RepositoryResult current = Load();
            if (!current.IsSuccess) return current;
            StudioState next = transition(current.State!);
            if (ReferenceEquals(next, current.State)) return current;
            RepositoryError error = SaveCore(next);
            return error == RepositoryError.None ? new RepositoryResult(next, error) : Fail(error);
        });
    }

    private RepositoryResult WithWriteLock(Func<RepositoryResult> action)
    {
        string key = OperatingSystem.IsWindows() ? _filePath.ToUpperInvariant() : _filePath;
        string prefix = OperatingSystem.IsWindows() ? "Local\\" : string.Empty;
        string name = prefix + "DesktopMotionStudio.State." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        Mutex mutex;
        try { mutex = new Mutex(initiallyOwned: false, name); }
        catch (UnauthorizedAccessException) { return Fail(RepositoryError.IoError); }
        catch (WaitHandleCannotBeOpenedException) { return Fail(RepositoryError.IoError); }
        using (mutex)
        {
            bool acquired;
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return Fail(RepositoryError.Busy);
            try { return action(); }
            finally { mutex.ReleaseMutex(); }
        }
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
                try { File.Delete(tempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        }
    }

    private static bool Validate(StudioState state)
    {
        if (state is null || state.SchemaVersion != StudioState.CurrentSchemaVersion ||
            state.Applications is null || state.Bindings is null ||
            state.Applications.Count > 1000 || state.Bindings.Count > 5000 ||
            !StudioSettings.IsValid(state.Settings)) return false;

        var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ApplicationProfile? app in state.Applications)
            if (app is null || string.IsNullOrWhiteSpace(app.AppId) || app.AppId.Length > 128 ||
                string.IsNullOrWhiteSpace(app.DisplayName) || app.DisplayName.Length > 120 ||
                string.IsNullOrWhiteSpace(app.LaunchTarget) || app.LaunchTarget.Length > 2048 ||
                (app.Target is not null && !app.Target.IsValid) || !apps.Add(app.AppId)) return false;

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

    private static bool HasDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateMembers(property.Value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
                if (HasDuplicateMembers(item)) return true;
        return false;
    }
}
