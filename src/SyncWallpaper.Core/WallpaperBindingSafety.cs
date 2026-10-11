namespace SyncWallpaper.Core;

/// <summary>Role labels may repeat; binding keys and target devices must not.</summary>
public static class WallpaperBindingSafety
{
    public static bool EnsureUniqueRoleIds(IEnumerable<MonitorRoleBinding> roles)
    {
        var changed = false;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            if (!string.IsNullOrWhiteSpace(role.RoleId) && ids.Add(role.RoleId)) continue;
            do role.RoleId = Guid.NewGuid().ToString("N");
            while (!ids.Add(role.RoleId));
            changed = true;
        }
        return changed;
    }

    public static bool HasUniqueKeys(IEnumerable<MonitorRoleBinding> roles)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return roles.All(role => (!string.IsNullOrWhiteSpace(role.RoleId) || !string.IsNullOrWhiteSpace(role.Role))
            && keys.Add(string.IsNullOrWhiteSpace(role.RoleId) ? role.Role : role.RoleId));
    }

    public static bool TrySnapshotTargets(MatchResult match, out IReadOnlyList<WallpaperTarget> targets, out string error)
    {
        targets = Array.Empty<WallpaperTarget>();
        error = string.Empty;
        if (match.Profile is null || match.Profile.Roles.Count == 0 || !HasUniqueKeys(match.Profile.Roles))
        {
            error = "显示器绑定编号重复或配置为空，请重新保存组合。";
            return false;
        }
        if (match.IsGeneric && !HasCompleteGenericLayout(match))
        {
            error = "通用组合的整套横竖屏数量或位置已不匹配，保持当前壁纸。";
            return false;
        }
        var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<WallpaperTarget>();
        foreach (var role in match.Profile.Roles)
        {
            // The entire generic layout has already been validated. Slots
            // deliberately left without a wallpaper do not need a write.
            if (match.IsGeneric && !match.TryGetMonitor(role, out _)) continue;
            if (!match.TryGetMonitor(role, out var monitor) || string.IsNullOrWhiteSpace(monitor.MonitorDevicePath))
            {
                error = $"“{role.DisplayName}”缺少目标显示器，未应用壁纸。";
                return false;
            }
            if (!devices.Add(monitor.MonitorDevicePath))
            {
                error = $"多个壁纸绑定指向同一台显示器“{monitor.DisplayLabel}”，未应用壁纸。";
                return false;
            }
            // Applying awaits Explorer. Later edits must not change this transaction.
            items.Add(new WallpaperTarget(role.Clone(), monitor.Clone()));
        }
        if (items.Count == 0)
        {
            error = "当前显示器没有可应用的壁纸配置，保持现有壁纸。";
            return false;
        }
        targets = items;
        return true;
    }

    private static bool HasCompleteGenericLayout(MatchResult match)
    {
        var layout = match.GenericLayoutMonitors;
        if (layout.Count == 0 || layout.Any(m => !m.IsConnected || string.IsNullOrWhiteSpace(m.MonitorDevicePath))
            || layout.Select(m => m.MonitorDevicePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != layout.Count) return false;
        var verified = new GenericProfileMatcher().Match(layout, new[] { match.Profile! });
        return verified.CanAutoApply && verified.RoleMatches.Count == match.RoleMatches.Count
            && verified.RoleMatches.All(pair => match.RoleMatches.TryGetValue(pair.Key, out var actual)
                && actual.MonitorDevicePath.Equals(pair.Value.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)
                && actual.Width == pair.Value.Width && actual.Height == pair.Value.Height
                && actual.DesktopX == pair.Value.DesktopX && actual.DesktopY == pair.Value.DesktopY);
    }
}

public sealed record WallpaperTarget(MonitorRoleBinding Binding, MonitorIdentity Monitor);
