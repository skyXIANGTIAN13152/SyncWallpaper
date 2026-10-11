namespace SyncWallpaper.Core;

/// <summary>
/// Matches complete orientation counts and explicit position slots, without
/// claiming hardware identity. Numbering ignores Windows enumeration order.
/// </summary>
public sealed class GenericProfileMatcher
{
    public MatchResult Match(IReadOnlyList<MonitorIdentity> monitors, IReadOnlyList<WallpaperProfile> profiles)
    {
        var activeMonitors = monitors.Where(monitor => monitor.IsConnected).ToArray();
        var ordered = activeMonitors
            .GroupBy(monitor => monitor.Width >= monitor.Height
                ? GenericMonitorOrientation.Landscape : GenericMonitorOrientation.Portrait)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(monitor => monitor.DesktopX)
                .ThenBy(monitor => monitor.DesktopY)
                .ThenBy(monitor => monitor.MonitorDevicePath, StringComparer.Ordinal)
                .ToArray());
        var landscapeCount = ordered.TryGetValue(GenericMonitorOrientation.Landscape, out var landscape) ? landscape.Length : 0;
        var portraitCount = ordered.TryGetValue(GenericMonitorOrientation.Portrait, out var portrait) ? portrait.Length : 0;

        foreach (var profile in profiles.Where(profile => profile.Enabled && profile.IsGeneric)
                     .OrderByDescending(profile => profile.Priority)
                     .ThenBy(profile => profile.Id, StringComparer.Ordinal))
        {
            if (!WallpaperProfileApplyPolicy.IsComplete(profile)) continue;
            // An omitted index is an intentionally unconfigured screen, not
            // permission to renumber later slots or match a smaller setup.
            if (GetRequiredMonitorCount(profile.Roles, GenericMonitorOrientation.Landscape) != landscapeCount
                || GetRequiredMonitorCount(profile.Roles, GenericMonitorOrientation.Portrait) != portraitCount) continue;
            var mapping = new Dictionary<string, MonitorIdentity>(StringComparer.OrdinalIgnoreCase);
            var evidence = new List<MatchEvidence>();
            foreach (var slot in profile.Roles)
            {
                if (!HasWallpaper(slot)
                    || !ordered.TryGetValue(slot.GenericOrientation, out var screens)
                    || slot.GenericIndex > screens.Length) continue;
                var monitor = screens[slot.GenericIndex - 1];
                mapping.Add(slot.RoleId, monitor);
                evidence.Add(new MatchEvidence
                {
                    Role = GetSlotDisplayName(slot.GenericOrientation, slot.GenericIndex),
                    Monitor = monitor.DisplayLabel,
                    Score = 0,
                    Reason = "横竖屏数量完全一致，按桌面位置匹配：从左到右，同列从上到下",
                    IdentityStatus = DisplayIdentityMatchStatus.Unknown
                });
            }

            if (mapping.Count == 0) continue;
            var unassigned = activeMonitors.Length - mapping.Count;
            return new MatchResult
            {
                Status = MatchStatus.Generic,
                Profile = profile,
                RoleMatches = mapping,
                GenericLayoutMonitors = activeMonitors.Select(monitor => monitor.Clone()).ToArray(),
                Evidence = evidence,
                Confidence = 0,
                IdentityStatus = DisplayIdentityMatchStatus.Unknown,
                CanAutoApply = true,
                UnassignedMonitorCount = unassigned,
                Message = unassigned > 0
                    ? $"已通用匹配 {landscapeCount} 横屏、{portraitCount} 竖屏；{mapping.Count} 台应用壁纸，{unassigned} 台保留当前壁纸"
                    : $"已通用匹配 {landscapeCount} 横屏、{portraitCount} 竖屏，应用 {mapping.Count} 台壁纸"
            };
        }

        return new MatchResult
        {
            Status = MatchStatus.NoMatch,
            UnassignedMonitorCount = activeMonitors.Length,
            Message = $"当前 {landscapeCount} 横屏、{portraitCount} 竖屏，没有数量完全一致且已配置壁纸的通用组合"
        };
    }

    public static string GetSlotDisplayName(GenericMonitorOrientation orientation, int index)
        => $"{(orientation == GenericMonitorOrientation.Portrait ? "竖屏" : "横屏")}{index}";

    /// <summary>Holes retain their positions; the final index defines the required count.</summary>
    public static int GetRequiredMonitorCount(IEnumerable<MonitorRoleBinding> slots, GenericMonitorOrientation orientation)
        => slots.Where(slot => slot.GenericOrientation == orientation)
            .Select(slot => slot.GenericIndex).DefaultIfEmpty(0).Max();

    /// <summary>Finds the first hole without allocating or scanning up to the highest saved index.</summary>
    public static int GetNextAvailableIndex(IEnumerable<int> existingIndexes)
    {
        var next = 1;
        foreach (var index in existingIndexes.Where(index => index > 0).Distinct().OrderBy(index => index))
        {
            if (index > next) break;
            if (next == int.MaxValue) throw new InvalidOperationException("没有可用的屏幕编号。");
            next++;
        }
        return next;
    }

    public static bool HasWallpaper(MonitorRoleBinding slot)
        => !string.IsNullOrWhiteSpace(slot.WallpaperAssetId)
            || !string.IsNullOrWhiteSpace(slot.WallpaperPath);

    public static bool HasValidSlots(IReadOnlyCollection<MonitorRoleBinding>? slots)
    {
        if (slots is null) return false;
        var positions = new HashSet<(GenericMonitorOrientation, int)>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return slots.All(slot => slot is not null
            && Enum.IsDefined(slot.GenericOrientation)
            && slot.GenericIndex >= 1
            && slot.FitMode != WallpaperFitMode.Span
            && positions.Add((slot.GenericOrientation, slot.GenericIndex))
            && !string.IsNullOrWhiteSpace(slot.RoleId)
            && keys.Add(slot.RoleId));
    }
}
