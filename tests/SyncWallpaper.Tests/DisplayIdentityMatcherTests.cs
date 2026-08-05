using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public class DisplayIdentityMatcherTests
{
    [TestMethod]
    public void UniqueEdidSerialIsExactAndExplainable()
    {
        var expected = Monitor("AOC", "B426", "SER-A", @"\\?\DISPLAY#A", 0);
        var actual = expected.Clone();
        actual.WindowsDisplayName = @"\\.\DISPLAY7";
        actual.MonitorDevicePath = @"\\?\DISPLAY#A-RECONNECTED";
        var result = new DisplayIdentityMatcher().Match(expected, new[] { actual });

        Assert.AreEqual(DisplayIdentityMatchStatus.ExactMatch, result.Status);
        Assert.IsTrue(result.CanAutoApply);
        StringAssert.Contains(result.Basis, "EDID");
        Assert.AreSame(actual, result.Monitor);
    }

    [TestMethod]
    public void SameModelDifferentSerialsRemainDistinct()
    {
        var expected = Monitor("AOC", "B426", "SER-A", "PATH-A", 0);
        var wrong = Monitor("AOC", "B426", "SER-B", "PATH-B", 1920);
        var result = new DisplayIdentityMatcher().Match(expected, new[] { wrong });

        Assert.AreEqual(DisplayIdentityMatchStatus.Unknown, result.Status);
        Assert.IsFalse(result.CanAutoApply);
        CollectionAssert.Contains(result.ConflictingFields.ToArray(), "EDID 序列号");
    }

    [TestMethod]
    public void EmptySerialIdenticalModelsAreAmbiguous()
    {
        var expected = Monitor("AOC", "B426", "0", "", 0);
        var first = Monitor("AOC", "B426", "0", "", 0);
        var second = Monitor("AOC", "B426", "0", "", 0);
        var result = new DisplayIdentityMatcher().Match(expected, new[] { first, second });

        Assert.AreEqual(DisplayIdentityMatchStatus.Ambiguous, result.Status);
        Assert.IsFalse(result.CanAutoApply);
        Assert.AreEqual(2, result.TiedCandidates.Count);
    }

    [TestMethod]
    public void ContainerIdSurvivesPathAndInterfaceChange()
    {
        var expected = Monitor("DELL", "U2720", "", "PATH-A", 0);
        expected.ContainerId = "{CONTAINER-1}";
        var actual = Monitor("DELL", "U2720", "", "PATH-B", 0);
        actual.ContainerId = "{CONTAINER-1}";
        actual.OutputTechnology = expected.OutputTechnology + 1;
        var result = new DisplayIdentityMatcher().Match(expected, new[] { actual });

        Assert.AreEqual(DisplayIdentityMatchStatus.StrongMatch, result.Status);
        Assert.IsTrue(result.CanAutoApply);
        StringAssert.Contains(result.Basis, "Container");
    }

    [TestMethod]
    public void TemporaryDisplayNumberIsIgnored()
    {
        var expected = Monitor("TMA", "0803", "L-1", "PATH-L", 0, true);
        expected.WindowsDisplayName = @"\\.\DISPLAY1";
        var actual = expected.Clone();
        actual.WindowsDisplayName = @"\\.\DISPLAY3";
        var result = new DisplayIdentityMatcher().Match(expected, actual);

        Assert.AreEqual(DisplayIdentityMatchStatus.ExactMatch, result.Status);
        Assert.IsTrue(result.CanAutoApply);
    }

    [TestMethod]
    public void StableIdBuilderDoesNotAssignAmbiguousIdToDuplicateGeometry()
    {
        var first = Monitor("AOC", "B426", "0", "", 0);
        var second = Monitor("AOC", "B426", "0", "", 0);
        MonitorIdentityBuilder.AssignStableIds(new[] { first, second });

        Assert.AreEqual(MonitorIdentitySource.Ambiguous, first.StableIdSource);
        Assert.AreEqual(MonitorIdentitySource.Ambiguous, second.StableIdSource);
        Assert.IsTrue(string.IsNullOrWhiteSpace(first.StableId));
        Assert.IsTrue(string.IsNullOrWhiteSpace(second.StableId));
    }

    [TestMethod]
    public void StableIdBuilderUsesSerialBeforePath()
    {
        var monitor = Monitor("AOC", "B426", "SER-A", "PATH-A", 0);
        MonitorIdentityBuilder.AssignStableIds(new[] { monitor });

        Assert.AreEqual(MonitorIdentitySource.EdidSerial, monitor.StableIdSource);
        StringAssert.StartsWith(monitor.StableId, "edid:");
    }

    private static MonitorIdentity Monitor(string manufacturer, string product, string serial, string path, int x, bool internalDisplay = false) => new()
    {
        ManufacturerName = manufacturer,
        ProductCodeId = product,
        EdidManufactureId = manufacturer,
        EdidProductCodeId = product,
        EdidSerialNumber = serial,
        MonitorDevicePath = path,
        AdapterId = "GPU",
        TargetId = (uint)(x / 1920 + 1),
        OutputTechnology = 10,
        ConnectorInstance = (uint)(x / 1920 + 1),
        Width = 1920,
        Height = 1080,
        Rotation = 1,
        DesktopX = x,
        DesktopY = 0,
        IsInternal = internalDisplay
    };
}
