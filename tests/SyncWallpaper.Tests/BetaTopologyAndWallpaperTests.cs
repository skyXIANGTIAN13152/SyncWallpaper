using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class BetaTopologyAndWallpaperTests
{
    [TestMethod]
    public void LaptopOnlyTemplateHasStableLogicalRole()
    {
        var profile = ProfileTemplates.LaptopOnly();
        Assert.AreEqual(DisplayCombinationKind.LaptopOnly, profile.Combination);
        Assert.AreEqual("Laptop", profile.Roles.Single().Role);
        Assert.AreEqual(1, profile.ExpectedMonitorCount);
    }

    [TestMethod]
    public void ThreeMonitorTemplateUsesRolesNotWindowsNumbers()
    {
        var profile = ProfileTemplates.ThreeMonitorSetup();
        CollectionAssert.AreEquivalent(new[] { "Laptop", "Landscape", "Portrait" }, profile.Roles.Select(x => x.Role).ToArray());
        Assert.IsFalse(profile.Roles.Any(x => x.Fingerprint.WindowsDisplayName.StartsWith("\\\\.\\DISPLAY", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void SameModelWithoutSerialCannotAutoApply()
    {
        var a = Monitor("", serial: "");
        var b = Monitor("", serial: "");
        var profile = new WallpaperProfile { Name = "same", ExpectedMonitorCount = 2, AllowCompatibleMatch = true,
            Roles = new() { new MonitorRoleBinding { Role = "Left", Fingerprint = a }, new MonitorRoleBinding { Role = "Right", Fingerprint = b } } };
        var result = new ProfileMatcher().Match(new[] { a.Clone(), b.Clone() }, new[] { profile });
        Assert.AreEqual(MatchStatus.Ambiguous, result.Status);
        Assert.IsFalse(result.CanAutoApply);
    }

    [TestMethod]
    public void WallpaperPlanDeduplicatesAndKeepsMissingAssetsSeparate()
    {
        var plan = WallpaperTransactionPlanner.Plan(new[]
        {
            new WallpaperChange("monitor-a", "old-a", "new-a"),
            new WallpaperChange("monitor-a", "old-a", "newer-a"),
            new WallpaperChange("monitor-b", "old-b", "old-b")
        }, new[] { "asset-missing", "asset-missing" });
        Assert.AreEqual(2, plan.Changes.Count);
        Assert.AreEqual("newer-a", plan.Changes.Single(x => x.MonitorDevicePath == "monitor-a").DesiredPath);
        Assert.AreEqual(1, plan.MissingAssets.Count);
        Assert.IsTrue(plan.HasChanges);
    }

    [TestMethod]
    public void FallbackGeometryIdentityIsExplainableAndNotWindowsNumber()
    {
        var monitor = Monitor("fallback://geometry/1920x1080/0,0/1", "");
        MonitorIdentityBuilder.AssignStableIds(new[] { monitor });
        Assert.AreEqual(MonitorIdentitySource.Geometry, monitor.StableIdSource);
        Assert.IsFalse(monitor.StableId.Contains("DISPLAY", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LocalLogRedactsUserPathByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "SyncWallpaperBetaLog", Guid.NewGuid().ToString("N"));
        try
        {
            var log = new SyncWallpaper.Windows.LogService(new DataPaths(root));
            var userPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "private", "wallpaper.jpg");
            log.Info("Test", "source=" + userPath);
            Assert.IsFalse(log.Recent[0].Message.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static MonitorIdentity Monitor(string path, string serial)
        => new() { MonitorDevicePath = path, ManufacturerName = "ACME", ProductCodeId = "MODEL", EdidManufactureId = "ACME", EdidProductCodeId = "MODEL", EdidSerialNumber = serial, Width = 1920, Height = 1080, Rotation = 1, DesktopX = path.Contains("A") ? 0 : 1920 };
}
