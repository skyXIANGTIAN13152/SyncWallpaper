namespace SyncWallpaper.Core;

public sealed class WallpaperProfileEditDraft
{
    public string Name { get; set; } = string.Empty;
    public WallpaperProfileMatchMode MatchMode { get; set; } = WallpaperProfileMatchMode.Specific;
    public bool Enabled { get; set; } = true;
    // Kept for source compatibility with older callers. Automatic apply is
    // derived from the profile's matching policy, so a stale checkbox value
    // cannot block a valid profile.
    public bool AutoApply { get; set; }
    public bool AllowCompatibleMatch { get; set; } = true;
    public int MinimumConfidence { get; set; } = 80;
    public int? Priority { get; set; }
    public List<WallpaperRoleEditDraft> Roles { get; set; } = new();
}

public sealed class WallpaperRoleEditDraft
{
    public string RoleId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public GenericMonitorOrientation GenericOrientation { get; set; }
    public int GenericIndex { get; set; } = 1;
    public MonitorIdentity? Fingerprint { get; set; }
    public string WallpaperAssetId { get; set; } = string.Empty;
    public string WallpaperPath { get; set; } = string.Empty;
    public WallpaperFitMode FitMode { get; set; } = WallpaperFitMode.Fill;
    public string BackgroundColor { get; set; } = "#050B18";
}

public static class WallpaperProfileEditingService
{
    public static WallpaperProfile CreateBlank(string? name, int priority,
        WallpaperProfileMatchMode matchMode = WallpaperProfileMatchMode.Specific)
    {
        var now = DateTime.UtcNow;
        return new WallpaperProfile
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? $"{(matchMode == WallpaperProfileMatchMode.Generic ? "通用组合" : "空白组合")} {now.ToLocalTime():MM-dd HHmm}"
                : name.Trim(),
            MatchMode = matchMode,
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
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("组合名称不能为空。");
        if (!Enum.IsDefined(draft.MatchMode)) throw new InvalidOperationException("组合匹配方式无效。");
        draft.Roles ??= new List<WallpaperRoleEditDraft>();
        var generic = draft.MatchMode == WallpaperProfileMatchMode.Generic;
        if (!generic && draft.Roles.Count > 8)
            throw new InvalidOperationException("一套组合最多支持 8 个逻辑角色。");

        var monitorKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slotPositions = new HashSet<(GenericMonitorOrientation, int)>();
        var roles = new List<MonitorRoleBinding>();
        foreach (var item in draft.Roles)
        {
            var roleName = generic
                ? GenericProfileMatcher.GetSlotDisplayName(item.GenericOrientation, item.GenericIndex)
                : item.Role?.Trim();
            if (string.IsNullOrWhiteSpace(roleName)) throw new InvalidOperationException("每个显示器都需要逻辑角色名称。");
            if (generic)
            {
                if (!Enum.IsDefined(item.GenericOrientation) || item.GenericIndex < 1)
                    throw new InvalidOperationException("通用屏幕需要有效方向和从 1 开始的正整数编号。");
                if (item.FitMode == WallpaperFitMode.Span)
                    throw new InvalidOperationException("通用组合不支持跨屏显示，请选择其他壁纸适应方式。");
                if (!slotPositions.Add((item.GenericOrientation, item.GenericIndex)))
                    throw new InvalidOperationException($"通用屏幕位置“{roleName}”重复。");
            }
            else
            {
                if (item.Fingerprint is null) throw new InvalidOperationException($"逻辑角色“{roleName}”尚未选择显示器。");
                var monitorKey = StrongMonitorKey(item.Fingerprint);
                if (string.IsNullOrWhiteSpace(monitorKey))
                    throw new InvalidOperationException($"逻辑角色“{roleName}”的显示器缺少可靠硬件身份。");
                if (!monitorKeys.Add(monitorKey)) throw new InvalidOperationException("同一台显示器不能重复分配给多个逻辑角色。");
            }

            roles.Add(new MonitorRoleBinding
            {
                RoleId = string.IsNullOrWhiteSpace(item.RoleId) ? Guid.NewGuid().ToString("N") : item.RoleId,
                Role = roleName,
                DisplayName = generic || string.IsNullOrWhiteSpace(item.DisplayName) ? roleName : item.DisplayName.Trim(),
                GenericOrientation = item.GenericOrientation,
                GenericIndex = item.GenericIndex,
                Fingerprint = generic ? new MonitorIdentity() : item.Fingerprint!.Clone(),
                WallpaperAssetId = item.WallpaperAssetId?.Trim() ?? string.Empty,
                WallpaperPath = item.WallpaperPath?.Trim() ?? string.Empty,
                FitMode = item.FitMode,
                BackgroundColor = string.IsNullOrWhiteSpace(item.BackgroundColor) ? "#050B18" : item.BackgroundColor,
                LastKnownMonitorDevicePath = generic ? string.Empty : item.Fingerprint!.MonitorDevicePath,
                Notes = "用户在组合编辑器中配置"
            });
        }

        WallpaperBindingSafety.EnsureUniqueRoleIds(roles);
        profile.Name = name;
        profile.MatchMode = draft.MatchMode;
        profile.SchemaVersion = Math.Max(profile.SchemaVersion, ProfileSchemaMigrator.CurrentSchemaVersion);
        profile.Enabled = draft.Enabled;
        profile.AllowCompatibleMatch = draft.AllowCompatibleMatch;
        profile.MinimumConfidence = Math.Clamp(draft.MinimumConfidence, 80, 100);
        if (draft.Priority is int priority) profile.Priority = priority;
        profile.ExpectedMonitorCount = generic ? 0 : roles.Count;
        profile.Combination = generic ? DisplayCombinationKind.Custom
            : roles.Count == 1 && roles[0].Fingerprint.IsInternal
            ? DisplayCombinationKind.LaptopOnly
            : roles.Count == 3 ? DisplayCombinationKind.ThreeMonitorSetup : DisplayCombinationKind.Custom;
        profile.Roles = roles;
        profile.AutoApply = WallpaperProfileApplyPolicy.IsComplete(profile);
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
/// Specific combinations require every role to have a monitor and wallpaper.
/// Generic combinations need valid unique positional slots and at least one
/// wallpaper; remaining slots may stay blank. Global AutoMatchEnabled is the
/// pause switch, so a stale per-profile flag cannot suppress valid profiles.
/// </summary>
public static class WallpaperProfileApplyPolicy
{
    public static bool IsComplete(WallpaperProfile? profile)
        => profile is not null
            && (profile.IsGeneric
                ? GenericProfileMatcher.HasValidSlots(profile.Roles)
                    && profile.Roles.Any(GenericProfileMatcher.HasWallpaper)
                : profile.Roles is { Count: > 0 }
                    && (profile.ExpectedMonitorCount <= 0 || profile.ExpectedMonitorCount == profile.Roles.Count)
                    && IsComplete(profile.Roles));

    public static bool IsComplete(IReadOnlyCollection<MonitorRoleBinding>? roles)
        => roles is { Count: > 0 }
            && roles.All(role => role.Fingerprint is not null
                && !string.IsNullOrWhiteSpace(role.Role)
                && (!string.IsNullOrWhiteSpace(role.WallpaperAssetId)
                    || !string.IsNullOrWhiteSpace(role.WallpaperPath)));
}
