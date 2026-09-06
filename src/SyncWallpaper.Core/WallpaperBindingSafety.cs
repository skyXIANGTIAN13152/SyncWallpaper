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
            error = "Monitor binding IDs are duplicated or the profile is empty. Save the profile again.";
            return false;
        }
        var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<WallpaperTarget>();
        foreach (var role in match.Profile.Roles)
        {
            if (!match.TryGetMonitor(role, out var monitor) || string.IsNullOrWhiteSpace(monitor.MonitorDevicePath))
            {
                error = $"No target monitor for '{role.DisplayName}'; wallpapers were not applied.";
                return false;
            }
            if (!devices.Add(monitor.MonitorDevicePath))
            {
                error = $"Multiple wallpaper bindings target the same monitor '{monitor.DisplayLabel}'; wallpapers were not applied.";
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
