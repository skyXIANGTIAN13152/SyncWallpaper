namespace SyncWallpaper.Core;

/// <summary>
/// Non-destructive migrations for wallpaper role/profile documents. Older
/// files are upgraded in memory first; the caller decides when to persist the
/// migrated document. Unknown future fields are left to System.Text.Json and
/// are therefore not rewritten unless the user saves the configuration.
/// </summary>
public static class ProfileSchemaMigrator
{
    public const int CurrentSchemaVersion = 2;

    public static ProfilesDocument Migrate(ProfilesDocument? document, Action<string>? log = null)
    {
        document ??= new ProfilesDocument();
        document.Profiles ??= new List<WallpaperProfile>();
        foreach (var profile in document.Profiles)
        {
            if (profile is null) continue;
            profile.Roles ??= new List<MonitorRoleBinding>();
            if (profile.SchemaVersion < 2)
            {
                profile.ExpectedMonitorCount = profile.ExpectedMonitorCount > 0 ? profile.ExpectedMonitorCount : profile.Roles.Count;
                profile.AutoApply = true;
                profile.MinimumConfidence = profile.AllowCompatibleMatch ? 60 : 80;
                profile.SchemaVersion = 2;
                log?.Invoke($"壁纸 Profile 已从旧版本迁移：{profile.Name}");
            }

            if (profile.ExpectedMonitorCount <= 0) profile.ExpectedMonitorCount = profile.Roles.Count;
            if (profile.Combination == DisplayCombinationKind.Custom)
            {
                if (profile.ExpectedMonitorCount == 1 && profile.Roles.Any(x => x.Role.Equals("Laptop", StringComparison.OrdinalIgnoreCase)))
                    profile.Combination = DisplayCombinationKind.LaptopOnly;
                else if (profile.ExpectedMonitorCount == 3 && profile.Roles.Any(x => x.Role.Equals("Landscape", StringComparison.OrdinalIgnoreCase))
                    && profile.Roles.Any(x => x.Role.Equals("Portrait", StringComparison.OrdinalIgnoreCase)))
                    profile.Combination = DisplayCombinationKind.ThreeMonitorSetup;
            }
            foreach (var role in profile.Roles)
            {
                if (role is null) continue;
                if (role.SchemaVersion < 2) role.SchemaVersion = 2;
                if (string.IsNullOrWhiteSpace(role.RoleId)) role.RoleId = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(role.LastKnownMonitorDevicePath)) role.LastKnownMonitorDevicePath = role.Fingerprint?.MonitorDevicePath ?? string.Empty;
                role.Fingerprint ??= new MonitorIdentity();
                role.Fingerprint.SchemaVersion = Math.Max(role.Fingerprint.SchemaVersion, 2);
                if (string.IsNullOrWhiteSpace(role.Fingerprint.StableId))
                    MonitorIdentityBuilder.AssignStableIds(new[] { role.Fingerprint });
            }
        }
        document.SchemaVersion = Math.Max(document.SchemaVersion, CurrentSchemaVersion);
        return document;
    }
}
