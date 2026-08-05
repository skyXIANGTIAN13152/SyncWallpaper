namespace SyncWallpaper.Core;

public sealed record WallpaperChange(string MonitorDevicePath, string PreviousPath, string DesiredPath);

public sealed record WallpaperTransactionPlan(IReadOnlyList<WallpaperChange> Changes, IReadOnlyList<string> MissingAssets)
{
    public bool HasChanges => Changes.Any(x => !string.Equals(x.PreviousPath, x.DesiredPath, StringComparison.OrdinalIgnoreCase));
}

public static class WallpaperTransactionPlanner
{
    public static WallpaperTransactionPlan Plan(IEnumerable<WallpaperChange> changes, IEnumerable<string>? missingAssets = null)
    {
        var unique = changes.Where(x => !string.IsNullOrWhiteSpace(x.MonitorDevicePath))
            .GroupBy(x => x.MonitorDevicePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last()).ToArray();
        return new WallpaperTransactionPlan(unique, (missingAssets ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
