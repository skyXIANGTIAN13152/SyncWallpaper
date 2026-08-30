namespace SyncWallpaper.Core;

public static class ProfileTemplates
{
    public static WallpaperProfile LaptopOnly() => new()
    {
        Name = "Laptop Only", Combination = DisplayCombinationKind.LaptopOnly,
        ExpectedMonitorCount = 1, Priority = 100, AutoApply = true,
        Roles = new() { new MonitorRoleBinding { Role = "Laptop", DisplayName = "Laptop" } }
    };

    public static WallpaperProfile ThreeMonitorSetup() => new()
    {
        Name = "Three Monitor Setup", Combination = DisplayCombinationKind.ThreeMonitorSetup,
        ExpectedMonitorCount = 3, Priority = 50, AutoApply = true,
        Roles = new()
        {
            new MonitorRoleBinding { Role = "Portrait", DisplayName = "Portrait" },
            new MonitorRoleBinding { Role = "Laptop", DisplayName = "Laptop" },
            new MonitorRoleBinding { Role = "Landscape", DisplayName = "Landscape" }
        }
    };

    public static WallpaperProfile Custom(string name, IEnumerable<string> roles)
    {
        var items = roles.Select(role => new MonitorRoleBinding { Role = role, DisplayName = role }).ToList();
        return new WallpaperProfile { Name = name, Combination = DisplayCombinationKind.Custom, ExpectedMonitorCount = items.Count, Roles = items };
    }
}
