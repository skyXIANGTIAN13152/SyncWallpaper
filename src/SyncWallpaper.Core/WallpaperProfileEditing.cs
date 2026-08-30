namespace SyncWallpaper.Core;

public sealed class WallpaperProfileEditDraft
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    // Kept for source compatibility with older callers. Automatic apply is
    // now derived from whether every configured role has a monitor and a
    // wallpaper, so a stale checkbox value cannot block a valid profile.
    public bool AutoApply { get; set; }
    public bool AllowCompatibleMatch { get; set; } = true;
    public int MinimumConfidence { get; set; } = 80;
    public List<WallpaperRoleEditDraft> Roles { get; set; } = new();
}

public sealed class WallpaperRoleEditDraft
{
    public string RoleId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MonitorIdentity? Fingerprint { get; set; }
    public string WallpaperAssetId { get; set; } = string.Empty;
    public string WallpaperPath { get; set; } = string.Empty;
    public WallpaperFitMode FitMode { get; set; } = WallpaperFitMode.Fill;
    public string BackgroundColor { get; set; } = "#050B18";
}

public static class WallpaperProfileEditingService
{
    public static WallpaperProfile CreateBlank(string? name, int priority)
    {
        var now = DateTime.UtcNow;
        return new WallpaperProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Blank profile {now.ToLocalTime():MM-dd HHmm}" : name.Trim(),
            Combination = DisplayCombinationKind.Custom,
            CreatedAt = now,
            ModifiedAt = now,
            Enabled = true,
            AutoApply = false,
            AllowCompatibleMatch = true,
            ExpectedMonitorCount = 0,
            MinimumConfidence = 80,
            Priority = priority
        };
    }

    public static void Apply(WallpaperProfile profile, WallpaperProfileEditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(draft);
        var name = draft.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Profile name cannot be empty.");
        draft.Roles ??= new List<WallpaperRoleEditDraft>();
        if (draft.Roles.Count > 8) throw new InvalidOperationException("A profile supports at most 8 logical roles.");

        var roleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var monitorKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roles = new List<MonitorRoleBinding>();
        foreach (var item in draft.Roles)
        {
            var roleName = item.Role?.Trim();
            if (string.IsNullOrWhiteSpace(roleName)) throw new InvalidOperationException("Every monitor needs a logical role name.");
            if (!roleNames.Add(roleName)) throw new InvalidOperationException($"Logical role \"{roleName}\" is duplicated.");
            if (item.Fingerprint is null) throw new InvalidOperationException($"Logical role \"{roleName}\" has no monitor selected.");
            var monitorKey = StrongMonitorKey(item.Fingerprint);
            if (string.IsNullOrWhiteSpace(monitorKey))
                throw new InvalidOperationException($"The monitor for logical role \"{roleName}\" lacks reliable hardware identity.");
            if (!monitorKeys.Add(monitorKey)) throw new InvalidOperationException("A monitor cannot be assigned to multiple logical roles.");

            roles.Add(new MonitorRoleBinding
            {
                RoleId = string.IsNullOrWhiteSpace(item.RoleId) ? Guid.NewGuid().ToString("N") : item.RoleId,
                Role = roleName,
                DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? roleName : item.DisplayName.Trim(),
                Fingerprint = item.Fingerprint.Clone(),
                WallpaperAssetId = item.WallpaperAssetId?.Trim() ?? string.Empty,
                WallpaperPath = item.WallpaperPath?.Trim() ?? string.Empty,
                FitMode = item.FitMode,
                BackgroundColor = string.IsNullOrWhiteSpace(item.BackgroundColor) ? "#050B18" : item.BackgroundColor,
                LastKnownMonitorDevicePath = item.Fingerprint.MonitorDevicePath,
                Notes = "Configured by the profile editor"
            });
        }

        profile.Name = name;
        profile.Enabled = draft.Enabled;
        profile.AutoApply = WallpaperProfileApplyPolicy.IsComplete(roles);
        profile.AllowCompatibleMatch = draft.AllowCompatibleMatch;
        profile.MinimumConfidence = Math.Clamp(draft.MinimumConfidence, 80, 100);
        profile.ExpectedMonitorCount = roles.Count;
        profile.Combination = roles.Count == 1 && roles[0].Fingerprint.IsInternal
            ? DisplayCombinationKind.LaptopOnly
            : roles.Count == 3 ? DisplayCombinationKind.ThreeMonitorSetup : DisplayCombinationKind.Custom;
        profile.Roles = roles;
        profile.ModifiedAt = DateTime.UtcNow;
        profile.LastSuccessfulMatchAt = null;
    }

    private static string StrongMonitorKey(MonitorIdentity monitor)
    {
        if (monitor.HasUsableSerial) return "edid:" + monitor.SerialKey;
        if (!string.IsNullOrWhiteSpace(monitor.MonitorDevicePath)
            && !monitor.MonitorDevicePath.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase)
            && !monitor.MonitorDevicePath.StartsWith("fallback://", StringComparison.OrdinalIgnoreCase))
            return "path:" + monitor.MonitorDevicePath;
        if (!string.IsNullOrWhiteSpace(monitor.InstanceName)) return "instance:" + monitor.InstanceName;
        if (!string.IsNullOrWhiteSpace(monitor.AdapterId)) return "topology:" + monitor.HardwareKey;
        return string.Empty;
    }
}

/// <summary>
/// A saved display combination is automatically applicable only after every
/// role has a stable monitor identity and a wallpaper reference. The policy
/// is deliberately data-driven: global AutoMatchEnabled is the one user-facing
/// pause switch, while a stale per-profile flag can no longer suppress startup.
/// </summary>
public static class WallpaperProfileApplyPolicy
{
    public static bool IsComplete(WallpaperProfile? profile)
        => profile is not null
            && profile.Roles is { Count: > 0 }
            && (profile.ExpectedMonitorCount <= 0 || profile.ExpectedMonitorCount == profile.Roles.Count)
            && IsComplete(profile.Roles);

    public static bool IsComplete(IReadOnlyCollection<MonitorRoleBinding>? roles)
        => roles is { Count: > 0 }
            && roles.All(role => role.Fingerprint is not null
                && !string.IsNullOrWhiteSpace(role.Role)
                && (!string.IsNullOrWhiteSpace(role.WallpaperAssetId)
                    || !string.IsNullOrWhiteSpace(role.WallpaperPath)));
}
