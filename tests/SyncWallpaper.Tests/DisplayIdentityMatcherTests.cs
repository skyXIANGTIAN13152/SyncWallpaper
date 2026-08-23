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
    public void ContainerIdAloneIsOnlyProbableAndCannotAutoApply()
    {
        var expected = Monitor("DELL", "U2720", "", "PATH-A", 0);
        expected.ContainerId = "{11111111-2222-3333-4444-555555555555}";
        var actual = Monitor("DELL", "U2720", "", "PATH-B", 0);
        actual.ContainerId = "{11111111-2222-3333-4444-555555555555}";
        actual.OutputTechnology = expected.OutputTechnology + 1;
        var result = new DisplayIdentityMatcher().Match(expected, new[] { actual });

        Assert.AreEqual(DisplayIdentityMatchStatus.ProbableMatch, result.Status);
        Assert.IsFalse(result.CanAutoApply);
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

    [TestMethod]
    public void StableIdBuilderUsesPathBeforeContainerId()
    {
        var monitor = Monitor("AOC", "B426", "0", "PATH-A", 0);
        monitor.ContainerId = "{11111111-2222-3333-4444-555555555555}";

        MonitorIdentityBuilder.AssignStableIds(new[] { monitor });

        Assert.AreEqual(MonitorIdentitySource.MonitorDevicePath, monitor.StableIdSource);
        StringAssert.StartsWith(monitor.StableId, "path:");
    }

    [TestMethod]
    public void SentinelContainerIdIsRejected()
    {
        var monitor = Monitor("TMA", "0803", "0", string.Empty, 0, true);
        monitor.AdapterId = string.Empty;
        monitor.ContainerId = "{00000000-0000-0000-ffff-ffffffffffff}";

        MonitorIdentityBuilder.AssignStableIds(new[] { monitor });

        Assert.AreNotEqual(MonitorIdentitySource.ContainerId, monitor.StableIdSource);
    }

    [TestMethod]
    public void GeometryStableIdRemainsProbableAndCannotAutoApply()
    {
        var expected = Monitor("", "", "0", string.Empty, 0);
        expected.AdapterId = string.Empty;
        var actual = expected.Clone();
        MonitorIdentityBuilder.AssignStableIds(new[] { expected });
        MonitorIdentityBuilder.AssignStableIds(new[] { actual });

        var result = new DisplayIdentityMatcher().Match(expected, actual);

        Assert.AreEqual(DisplayIdentityMatchStatus.ProbableMatch, result.Status);
        Assert.IsFalse(result.CanAutoApply);
    }

    [TestMethod]
    public void ConnectorInstanceZeroCanStillFormHardwareIdentity()
    {
        var monitor = Monitor("AOC", "B426", "0", string.Empty, 0);
        monitor.ConnectorInstance = 0;

        MonitorIdentityBuilder.AssignStableIds(new[] { monitor });

        Assert.AreEqual(MonitorIdentitySource.HardwareTopology, monitor.StableIdSource);
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
