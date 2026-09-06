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
        var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<WallpaperTarget>();
        foreach (var role in match.Profile.Roles)
        {
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
        targets = items;
        return true;
    }
}

public sealed record WallpaperTarget(MonitorRoleBinding Binding, MonitorIdentity Monitor);
