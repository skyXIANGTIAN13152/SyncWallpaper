namespace SyncWallpaper.Core;

public sealed record ManualWallpaperAssignment(MonitorIdentity Monitor, string Role, string WallpaperPath);

public static class ManualWallpaperBindingService
{
    public static MonitorRoleBinding? FindExisting(IEnumerable<MonitorRoleBinding> roles, MonitorIdentity monitor)
    {
        var matcher = new DisplayIdentityMatcher();
        var candidates = roles.Select(role => (Role: role, Match: matcher.Match(role.Fingerprint, monitor)))
            .Where(x => x.Match.CanAutoApply).OrderByDescending(x => x.Match.Score).ToArray();
        if (candidates.Length == 0 || (candidates.Length > 1 && candidates[0].Match.Score == candidates[1].Match.Score))
            return null;
        return candidates[0].Role;
    }

    public static void Apply(WallpaperProfile profile, IReadOnlyList<ManualWallpaperAssignment> assignments)
    {
        if (assignments.Count == 0 || assignments.Count > 8)
            throw new InvalidOperationException("Select between 1 and 8 monitors.");
        var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = profile.Roles.ToList();
        var roles = new List<MonitorRoleBinding>();
        foreach (var assignment in assignments)
        {
            var monitor = assignment.Monitor;
            if (string.IsNullOrWhiteSpace(monitor.MonitorDevicePath) || !devices.Add(monitor.MonitorDevicePath))
                throw new InvalidOperationException("The same monitor cannot be assigned more than once. Identify monitors again.");
            if (string.IsNullOrWhiteSpace(assignment.Role))
                throw new InvalidOperationException("Choose a logical role for every monitor.");
            // Never recover an old binding by a label such as Portrait: several
            // physical displays can legitimately have that label.
            var existing = FindExisting(remaining, monitor);
            if (existing is not null) remaining.Remove(existing);
            var role = existing?.Clone() ?? new MonitorRoleBinding();
            role.Role = assignment.Role.Trim();
            role.DisplayName = existing is not null && existing.Role.Equals(role.Role, StringComparison.OrdinalIgnoreCase)
                ? existing.DisplayName : role.Role switch
                {
                    "Laptop" => "Laptop", "Landscape" => "Landscape", "Portrait" => "Portrait", _ => role.Role
                };
            role.Fingerprint = monitor.Clone();
            role.LastKnownMonitorDevicePath = monitor.MonitorDevicePath;
            var path = assignment.WallpaperPath.Trim();
            if (!string.IsNullOrWhiteSpace(path) && !path.Equals(role.WallpaperPath, StringComparison.OrdinalIgnoreCase))
            {
                role.WallpaperPath = path;
                role.WallpaperAssetId = string.Empty;
            }
            // A blank field in the identification dialog means unchanged, for
            // both managed assets and previously selected external files.
            role.LastSuccessfulMatchAt = null;
            role.Notes = "Confirmed by the user in the A/B/C identification screen";
            roles.Add(role);
        }
        WallpaperBindingSafety.EnsureUniqueRoleIds(roles);
        profile.Roles = roles;
        profile.ExpectedMonitorCount = roles.Count;
        profile.Combination = roles.Count == 1 && roles[0].Fingerprint.IsInternal
            ? DisplayCombinationKind.LaptopOnly
            : roles.Count == 3 ? DisplayCombinationKind.ThreeMonitorSetup : DisplayCombinationKind.Custom;
        profile.AutoApply = WallpaperProfileApplyPolicy.IsComplete(profile);
        profile.ModifiedAt = DateTime.UtcNow;
        profile.LastSuccessfulMatchAt = null;
    }
}
