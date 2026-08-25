namespace SyncWallpaper.Core;

public static class AppSettingsMigrator
{
    public const int CurrentSchemaVersion = 3;

    public static bool Migrate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var changed = false;
        if (settings.SchemaVersion < CurrentSchemaVersion)
        {
            settings.SchemaVersion = CurrentSchemaVersion;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(settings.ActiveProfileId))
        {
            settings.EditingProfileId ??= settings.ActiveProfileId;
            settings.ActiveProfileId = null;
            changed = true;
        }
        return changed;
    }
}
