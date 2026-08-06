using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public class ProfileMatcherTests
{
    [TestMethod]
    public void SameModelWithDifferentSerials_IsExactAndDistinct()
    {
        var left = Monitor("AOC", "B426", "SERIAL-A", "PATH-A", 0);
        var right = Monitor("AOC", "B426", "SERIAL-B", "PATH-B", 1);
        var profile = Profile("双屏", ("Landscape", left), ("Landscape2", right));
        var result = new ProfileMatcher().Match(new[] { right, left }, new[] { profile });
        Assert.AreEqual(MatchStatus.Exact, result.Status);
        Assert.AreEqual("SERIAL-A", result.RoleMatches["Landscape"].EdidSerialNumber);
        Assert.AreEqual("SERIAL-B", result.RoleMatches["Landscape2"].EdidSerialNumber);
    }

    [TestMethod]
    public void NoSerialButDifferentDevicePaths_CanUsePath()
    {
        var left = Monitor("AOC", "B426", "0", "PATH-A", 0);
        var right = Monitor("AOC", "B426", "0", "PATH-B", 1);
        var result = new ProfileMatcher().Match(new[] { left, right }, new[] { Profile("双屏", ("A", left), ("B", right)) });
        Assert.AreEqual(MatchStatus.Exact, result.Status);
    }

    [TestMethod]
    public void IndistinguishableIdenticalDisplays_AreAmbiguous()
    {
        var a = Monitor("AOC", "B426", "0", "", 0);
        var b = Monitor("AOC", "B426", "0", "", 0);
        var result = new ProfileMatcher().Match(new[] { a, b }, new[] { Profile("双屏", ("A", a), ("B", b)) });
        Assert.AreEqual(MatchStatus.Ambiguous, result.Status);
    }

    [TestMethod]
    public void ReorderingCurrentDisplays_DoesNotChangeRoles()
    {
        var laptop = Monitor("TMA", "0803", "L-1", "L", 0, true);
        var landscape = Monitor("AOC", "B426", "A-1", "A", 1);
        var result = new ProfileMatcher().Match(new[] { landscape, laptop }, new[] { Profile("模式", ("Laptop", laptop), ("Landscape", landscape)) });
        Assert.AreEqual("L-1", result.RoleMatches["Laptop"].EdidSerialNumber);
        Assert.AreEqual("A-1", result.RoleMatches["Landscape"].EdidSerialNumber);
    }

    [TestMethod]
    public void WindowsTemporaryNumbersAreNotUsed()
    {
        var first = Monitor("A", "1", "S1", "PATH", 0); first.SourceId = 99; first.TargetId = 99;
        var second = first.Clone(); second.SourceId = 1; second.TargetId = 1;
        var result = new ProfileMatcher().Match(new[] { second }, new[] { Profile("单屏", ("Laptop", first)) });
        Assert.AreEqual(MatchStatus.Exact, result.Status);
    }

    [TestMethod]
    public void SameTopology_CanSelectTheHigherPriorityWallpaperCombination()
    {
        var monitor = Monitor("TMA", "0803", "L-1", "L", 0, true);
        var morning = Profile("晨间壁纸", ("Laptop", monitor));
        morning.Priority = 100;
        morning.Roles[0].WallpaperAssetId = "morning";
        var evening = Profile("夜间壁纸", ("Laptop", monitor));
        evening.Priority = 200;
        evening.Roles[0].WallpaperAssetId = "evening";

        var result = new ProfileMatcher().Match(new[] { monitor.Clone() }, new[] { morning, evening });

        Assert.AreEqual(MatchStatus.Exact, result.Status);
        Assert.AreEqual("夜间壁纸", result.Profile?.Name);
        Assert.AreEqual("evening", result.Profile?.Roles[0].WallpaperAssetId);
    }

    private static MonitorIdentity Monitor(string manufacturer, string product, string serial, string path, int x, bool internalDisplay = false) => new()
    {
        ManufacturerName = manufacturer, ProductCodeId = product, EdidManufactureId = manufacturer, EdidProductCodeId = product,
        EdidSerialNumber = serial, MonitorDevicePath = path, AdapterId = "GPU", TargetId = (uint)(x + 1), OutputTechnology = 10,
        ConnectorInstance = (uint)x, Width = 1920, Height = 1080, Rotation = 1, DesktopX = x * 1920, DesktopY = 0, IsInternal = internalDisplay
    };

    private static WallpaperProfile Profile(string name, params (string Role, MonitorIdentity Monitor)[] entries) => new()
    {
        Name = name, Roles = entries.Select(x => new MonitorRoleBinding { Role = x.Role, DisplayName = x.Role, Fingerprint = x.Monitor.Clone() }).ToList()
    };
}
