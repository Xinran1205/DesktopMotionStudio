namespace DesktopMotionStudio.Core.State;

public sealed record ApplicationProfile(string AppId, string DisplayName, string LaunchTarget);

public sealed record ApplicationBinding(string BindingId, string AppId, string SceneId, bool Enabled);

public sealed record StudioSettings(string Theme, bool Muted)
{
    public static StudioSettings Default { get; } = new("system", false);
}

public sealed record StudioState(
    int SchemaVersion,
    IReadOnlyList<ApplicationProfile> Applications,
    IReadOnlyList<ApplicationBinding> Bindings,
    StudioSettings Settings)
{
    public const int CurrentSchemaVersion = 1;
    public static StudioState Empty { get; } = new(CurrentSchemaVersion, [], [], StudioSettings.Default);
}

public enum RepositoryError { None, InvalidJson, UnsupportedSchema, InvalidState, IoError, Busy }

public interface IStudioStateStore
{
    RepositoryResult Load();
    RepositoryResult Update(Func<StudioState, StudioState> transition);
}

public sealed record RepositoryResult(StudioState? State, RepositoryError Error)
{
    public bool IsSuccess => State is not null && Error == RepositoryError.None;
}
