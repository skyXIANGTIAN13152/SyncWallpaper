namespace SyncWallpaper.Core;

public static class ProfileTemplates
{
    public static WallpaperProfile LaptopOnly() => new()
    {
        Name = "Laptop Only", Combination = DisplayCombinationKind.LaptopOnly,
        ExpectedMonitorCount = 1, Priority = 100, AutoApply = true,
        Roles = new() { new MonitorRoleBinding { Role = "Laptop", DisplayName = "笔记本本体" } }
    };

    public static WallpaperProfile ThreeMonitorSetup() => new()
    {
        Name = "Three Monitor Setup", Combination = DisplayCombinationKind.ThreeMonitorSetup,
        ExpectedMonitorCount = 3, Priority = 50, AutoApply = true,
        Roles = new()
        {
            new MonitorRoleBinding { Role = "Portrait", DisplayName = "竖屏" },
            new MonitorRoleBinding { Role = "Laptop", DisplayName = "笔记本本体" },
            new MonitorRoleBinding { Role = "Landscape", DisplayName = "横屏" }
        }
    };

    public static WallpaperProfile Custom(string name, IEnumerable<string> roles)
    {
        var items = roles.Select(role => new MonitorRoleBinding { Role = role, DisplayName = role }).ToList();
        return new WallpaperProfile { Name = name, Combination = DisplayCombinationKind.Custom, ExpectedMonitorCount = items.Count, Roles = items };
    }
}
