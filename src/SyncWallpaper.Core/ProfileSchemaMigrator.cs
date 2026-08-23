namespace SyncWallpaper.Core;

/// <summary>
/// Non-destructive migrations for wallpaper role/profile documents. Older
/// files are upgraded in memory first; the caller decides when to persist the
/// migrated document. Unknown future fields are left to System.Text.Json and
/// are therefore not rewritten unless the user saves the configuration.
/// </summary>
public static class ProfileSchemaMigrator
{
    public const int CurrentSchemaVersion = 4;

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
            var roleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in profile.Roles)
            {
                if (role is null) continue;
                if (string.IsNullOrWhiteSpace(role.RoleId) || !roleIds.Add(role.RoleId))
                {
                    do role.RoleId = Guid.NewGuid().ToString("N");
                    while (!roleIds.Add(role.RoleId));
                }
                if (string.IsNullOrWhiteSpace(role.LastKnownMonitorDevicePath)) role.LastKnownMonitorDevicePath = role.Fingerprint?.MonitorDevicePath ?? string.Empty;
                role.Fingerprint ??= new MonitorIdentity();
            }
            // Rebuild all role identities together so uniqueness is evaluated
            // across the whole topology. This also removes legacy/sentinel
            // ContainerIds and restores the documented serial -> path ->
            // hardware -> weak-fallback order.
            MonitorIdentityBuilder.AssignStableIds(profile.Roles.Where(x => x?.Fingerprint is not null).Select(x => x.Fingerprint));
            foreach (var role in profile.Roles.Where(x => x is not null))
            {
                role.SchemaVersion = Math.Max(role.SchemaVersion, CurrentSchemaVersion);
                role.Fingerprint.SchemaVersion = Math.Max(role.Fingerprint.SchemaVersion, CurrentSchemaVersion);
            }
            if (profile.SchemaVersion < 4)
            {
                // Earlier blank-profile editing could leave AutoApply=false
                // even after all monitor and wallpaper fields were completed.
                // Repair that stale state; incomplete profiles remain safe.
                profile.AutoApply = WallpaperProfileApplyPolicy.IsComplete(profile);
                log?.Invoke($"壁纸 Profile 自动应用规则已修复：{profile.Name}");
            }
            if (profile.SchemaVersion < CurrentSchemaVersion)
                log?.Invoke($"壁纸 Profile 身份规则已升级：{profile.Name}");
            profile.SchemaVersion = Math.Max(profile.SchemaVersion, CurrentSchemaVersion);
        }
        document.SchemaVersion = Math.Max(document.SchemaVersion, CurrentSchemaVersion);
        return document;
    }
}
