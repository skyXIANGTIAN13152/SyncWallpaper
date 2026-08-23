using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class WallpaperProfileEditingTests
{
    [TestMethod]
    public void BlankProfileIsSafeAndDoesNotAutoApply()
    {
        var profile = WallpaperProfileEditingService.CreateBlank("", 120);
        Assert.IsFalse(profile.AutoApply);
        Assert.AreEqual(0, profile.ExpectedMonitorCount);
        Assert.AreEqual(0, profile.Roles.Count);
        StringAssert.StartsWith(profile.Name, "空白组合");
    }

    [TestMethod]
    public void BlankProfileNeverMatchesAnActiveTopology()
    {
        var profile = WallpaperProfileEditingService.CreateBlank("later", 120);
        var result = new ProfileMatcher().Match(new[] { TestData.Monitor() }, new[] { profile });

        Assert.AreEqual(MatchStatus.NoMatch, result.Status);
        Assert.IsNull(result.Profile);
        Assert.IsFalse(result.CanAutoApply);
    }

    [TestMethod]
    public void BlankProfileCannotBecomeAutomaticEvenWhenRequested()
    {
        var profile = WallpaperProfileEditingService.CreateBlank("test", 100);
        var draft = new WallpaperProfileEditDraft { Name = "test", AutoApply = true };
        WallpaperProfileEditingService.Apply(profile, draft);
        Assert.IsFalse(profile.AutoApply);
    }

    [TestMethod]
    public void DuplicateRolesAndDuplicateMonitorsAreRejected()
    {
        var monitor = TestData.Monitor();
        var profile = WallpaperProfileEditingService.CreateBlank("test", 100);
        var duplicateRole = Draft(Role("Laptop", monitor), Role("Laptop", TestData.Monitor("PATH-B", "SERIAL-B")));
        StringAssert.Contains(Assert.ThrowsException<InvalidOperationException>(() => WallpaperProfileEditingService.Apply(profile, duplicateRole)).Message, "重复");

        var duplicateMonitor = Draft(Role("Laptop", monitor), Role("Portrait", monitor));
        StringAssert.Contains(Assert.ThrowsException<InvalidOperationException>(() => WallpaperProfileEditingService.Apply(profile, duplicateMonitor)).Message, "同一台显示器");
    }

    [TestMethod]
    public void IncompleteRoleRemainsNonAutomatic()
    {
        var profile = WallpaperProfileEditingService.CreateBlank("test", 100);
        var draft = Draft(Role("Laptop", TestData.Monitor()));
        draft.AutoApply = true;
        WallpaperProfileEditingService.Apply(profile, draft);
        Assert.IsFalse(profile.AutoApply);
    }

    [TestMethod]
    public void ValidEditBuildsCompleteThreeMonitorProfile()
    {
        var profile = WallpaperProfileEditingService.CreateBlank("old", 100);
        var draft = Draft(
            Role("Laptop", TestData.Monitor("PATH-L", "SERIAL-L"), "asset-l", @"D:\Wallpapers\l.jpg"),
            Role("Landscape", TestData.Monitor("PATH-A", "SERIAL-A", 1920), "asset-a", @"D:\Wallpapers\a.jpg"),
            Role("Portrait", TestData.Monitor("PATH-B", "SERIAL-B", 3840), "asset-b", @"D:\Wallpapers\b.jpg"));
        draft.Name = "  Three screens  ";
        draft.AutoApply = false;
        draft.MinimumConfidence = 10;

        WallpaperProfileEditingService.Apply(profile, draft);

        Assert.AreEqual("Three screens", profile.Name);
        Assert.AreEqual(3, profile.ExpectedMonitorCount);
        Assert.AreEqual(DisplayCombinationKind.ThreeMonitorSetup, profile.Combination);
        Assert.IsTrue(profile.AutoApply);
        Assert.AreEqual(80, profile.MinimumConfidence);
        Assert.IsTrue(profile.Roles.All(x => !string.IsNullOrWhiteSpace(x.RoleId)));
    }

    [TestMethod]
    public void ExistingPathOnlyProfileIsComplete()
    {
        var profile = WallpaperProfileEditingService.CreateBlank("path-only", 100);
        var draft = Draft(Role("Laptop", TestData.Monitor(), path: @"D:\Wallpapers\l.jpg"));

        WallpaperProfileEditingService.Apply(profile, draft);

        Assert.IsTrue(profile.AutoApply);
        Assert.IsTrue(WallpaperProfileApplyPolicy.IsComplete(profile));
    }

    [TestMethod]
    public void GeometryOnlyMonitorCannotBeAssigned()
    {
        var monitor = new MonitorIdentity { Width = 1920, Height = 1080, StableId = "geometry:test", StableIdSource = MonitorIdentitySource.Geometry };
        var profile = WallpaperProfileEditingService.CreateBlank("test", 100);
        var error = Assert.ThrowsException<InvalidOperationException>(() => WallpaperProfileEditingService.Apply(profile, Draft(Role("Custom", monitor))));
        StringAssert.Contains(error.Message, "可靠硬件身份");
    }

    private static WallpaperProfileEditDraft Draft(params WallpaperRoleEditDraft[] roles)
        => new() { Name = "test", Enabled = true, Roles = roles.ToList() };

    private static WallpaperRoleEditDraft Role(string role, MonitorIdentity monitor, string asset = "", string path = "")
        => new() { Role = role, DisplayName = role, Fingerprint = monitor, WallpaperAssetId = asset, WallpaperPath = path };
}
