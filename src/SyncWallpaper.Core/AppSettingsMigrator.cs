namespace SyncWallpaper.Core;

public static class AppSettingsMigrator
{
    public const int CurrentSchemaVersion = 4;

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
        var themeId = AppThemeCatalog.Get(settings.UiTheme).Id;
        if (!string.Equals(settings.UiTheme, themeId, StringComparison.Ordinal))
        {
            settings.UiTheme = themeId;
            changed = true;
        }
        return changed;
    }
}
