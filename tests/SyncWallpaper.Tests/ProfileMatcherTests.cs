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
        var profile = Profile("Two monitors", ("Landscape", left), ("Landscape2", right));
        var result = new ProfileMatcher().Match(new[] { right, left }, new[] { profile });
        Assert.AreEqual(MatchStatus.Exact, result.Status);
        Assert.AreEqual("SERIAL-A", result.RoleMatches[profile.Roles[0].RoleId].EdidSerialNumber);
        Assert.AreEqual("SERIAL-B", result.RoleMatches[profile.Roles[1].RoleId].EdidSerialNumber);
    }

    [TestMethod]
    public void NoSerialButDifferentDevicePaths_CanUsePath()
    {
        var left = Monitor("AOC", "B426", "0", "PATH-A", 0);
        var right = Monitor("AOC", "B426", "0", "PATH-B", 1);
        var result = new ProfileMatcher().Match(new[] { left, right }, new[] { Profile("Two monitors", ("A", left), ("B", right)) });
        Assert.AreEqual(MatchStatus.Exact, result.Status);
    }

    [TestMethod]
    public void IndistinguishableIdenticalDisplays_AreAmbiguous()
    {
        var a = Monitor("AOC", "B426", "0", "", 0);
        var b = Monitor("AOC", "B426", "0", "", 0);
        var result = new ProfileMatcher().Match(new[] { a, b }, new[] { Profile("Two monitors", ("A", a), ("B", b)) });
        Assert.AreEqual(MatchStatus.Ambiguous, result.Status);
    }

    [TestMethod]
    public void ReorderingCurrentDisplays_DoesNotChangeRoles()
    {
        var laptop = Monitor("TMA", "0803", "L-1", "L", 0, true);
        var landscape = Monitor("AOC", "B426", "A-1", "A", 1);
        var profile = Profile("Mode", ("Laptop", laptop), ("Landscape", landscape));
        var result = new ProfileMatcher().Match(new[] { landscape, laptop }, new[] { profile });
        Assert.AreEqual("L-1", result.RoleMatches[profile.Roles[0].RoleId].EdidSerialNumber);
        Assert.AreEqual("A-1", result.RoleMatches[profile.Roles[1].RoleId].EdidSerialNumber);
    }

    [TestMethod]
    public void DuplicateLogicalRoleNames_KeepDistinctRoleIdAssignments()
    {
        var first = Monitor("HPN", "3481", "SERIAL-A", "PATH-A", 0);
        var second = Monitor("HWP", "309E", "SERIAL-B", "PATH-B", 1);
        var profile = Profile("Two landscape monitors", ("Landscape", first), ("Landscape", second));

        var result = new ProfileMatcher().Match(new[] { second, first }, new[] { profile });

        Assert.AreEqual(MatchStatus.Exact, result.Status);
        Assert.AreEqual(2, result.RoleMatches.Count);
        Assert.IsTrue(result.TryGetMonitor(profile.Roles[0], out var firstMatch));
        Assert.IsTrue(result.TryGetMonitor(profile.Roles[1], out var secondMatch));
        Assert.AreEqual("SERIAL-A", firstMatch.EdidSerialNumber);
        Assert.AreEqual("SERIAL-B", secondMatch.EdidSerialNumber);
    }

    [TestMethod]
    public void WindowsTemporaryNumbersAreNotUsed()
    {
        var first = Monitor("A", "1", "S1", "PATH", 0); first.SourceId = 99; first.TargetId = 99;
        var second = first.Clone(); second.SourceId = 1; second.TargetId = 1;
        var result = new ProfileMatcher().Match(new[] { second }, new[] { Profile("Single monitor", ("Laptop", first)) });
        Assert.AreEqual(MatchStatus.Exact, result.Status);
    }

    [TestMethod]
    public void SameTopology_CanSelectTheHigherPriorityWallpaperCombination()
    {
        var monitor = Monitor("TMA", "0803", "L-1", "L", 0, true);
        var morning = Profile("Morning wallpaper", ("Laptop", monitor));
        morning.Priority = 100;
        morning.Roles[0].WallpaperAssetId = "morning";
        var evening = Profile("Evening wallpaper", ("Laptop", monitor));
        evening.Priority = 200;
        evening.Roles[0].WallpaperAssetId = "evening";

        var result = new ProfileMatcher().Match(new[] { monitor.Clone() }, new[] { morning, evening });

        Assert.AreEqual(MatchStatus.Exact, result.Status);
        Assert.AreEqual("Evening wallpaper", result.Profile?.Name);
        Assert.AreEqual("evening", result.Profile?.Roles[0].WallpaperAssetId);
    }

    [TestMethod]
    public void Matching_DoesNotPromoteOrRewriteSavedProfiles()
    {
        var monitor = Monitor("TMA", "0803", "L-1", "L", 0, true);
        var profile = Profile("Fixed profile", ("Laptop", monitor));
        profile.Priority = 17;
        var modified = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        profile.ModifiedAt = modified;

        var result = new ProfileMatcher().Match(new[] { monitor.Clone() }, new[] { profile });

        Assert.AreEqual(MatchStatus.Exact, result.Status);
        Assert.AreEqual(17, profile.Priority);
        Assert.AreEqual(modified, profile.ModifiedAt);
    }

    [TestMethod]
    public void AddingProbableDisplaysDoesNotInflateConfidenceOrEnableAutoApply()
    {
        var monitors = Enumerable.Range(0, 3).Select(index =>
        {
            var monitor = Monitor("AOC", "B426", "0", string.Empty, index);
            monitor.AdapterId = string.Empty;
            return monitor;
        }).ToArray();
        var profile = Profile("Weak-evidence three monitors",
            ("A", monitors[0]), ("B", monitors[1]), ("C", monitors[2]));

        var result = new ProfileMatcher().Match(monitors.Select(x => x.Clone()).ToArray(), new[] { profile });

        Assert.AreEqual(MatchStatus.Ambiguous, result.Status);
        Assert.IsFalse(result.CanAutoApply);
        Assert.IsTrue(result.Confidence < profile.MinimumConfidence);
    }

    [TestMethod]
    public void HardwareTopologyWithConnectorZeroRemainsStrong()
    {
        var expected = Monitor("AOC", "B426", "0", string.Empty, 0);
        expected.ConnectorInstance = 0;
        var actual = expected.Clone();

        var result = new ProfileMatcher().Match(new[] { actual }, new[] { Profile("Hardware identity", ("Landscape", expected)) });

        Assert.AreEqual(MatchStatus.Exact, result.Status);
        Assert.IsTrue(result.CanAutoApply);
        Assert.IsTrue(result.Confidence >= 80);
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
