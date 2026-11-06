namespace DesktopMotionStudio.Core.State;

public enum SettingsStatus { Ready, Applied, Unchanged, Conflict, Invalid, StorageError }

public sealed record SettingsResult(
    SettingsStatus Status,
    StudioSettings? Settings = null,
    RepositoryError RepositoryError = RepositoryError.None);

public sealed class StudioSettingsService(IStudioStateStore store)
{
    public SettingsResult Read()
    {
        RepositoryResult loaded = store.Load();
        return loaded.IsSuccess
            ? new(SettingsStatus.Ready, loaded.State!.Settings)
            : new(SettingsStatus.StorageError, RepositoryError: loaded.Error);
    }

    public SettingsResult Apply(StudioSettings expected, StudioSettings desired)
    {
        if (!StudioSettings.IsValid(expected) || !StudioSettings.IsValid(desired))
            return new(SettingsStatus.Invalid);

        SettingsStatus status = SettingsStatus.Unchanged;
        RepositoryResult saved = store.Update(current =>
        {
            if (current.Settings == desired) return current;
            if (current.Settings != expected)
            {
                status = SettingsStatus.Conflict;
                return current;
            }

            status = SettingsStatus.Applied;
            return current with { Settings = desired };
        });

        return saved.IsSuccess
            ? new(status, saved.State!.Settings)
            : new(SettingsStatus.StorageError, Read().Settings, saved.Error);
    }
}
