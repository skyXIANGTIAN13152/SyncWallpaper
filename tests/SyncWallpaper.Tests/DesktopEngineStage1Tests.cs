using SyncWallpaper.Core;
using SyncWallpaper.DesktopEngine;

namespace SyncWallpaper.Tests;

[TestClass]
public class DesktopEngineStage1Tests
{
    [TestMethod]
    public void CaptureUsesStableParsingNameAsKey()
    {
        var provider = new FakeDesktopIconProvider();
        provider.Positions.Add(new DesktopIconPosition { ParsingName = "C:\\Users\\z\\Desktop\\one.lnk", DisplayName = "one", X = 10, Y = 20 });
        var profile = new DesktopIconLayoutEngine(provider).Capture("desktop");
        Assert.AreEqual(1, profile.Positions.Count); Assert.IsTrue(profile.Positions.ContainsKey("C:\\Users\\z\\Desktop\\one.lnk"));
    }

    [TestMethod]
    public void MissingIconIsSkipped()
    {
        var provider = new FakeDesktopIconProvider { SetResult = false };
        var profile = new DesktopIconProfile { Name = "test" };
        profile.Positions["missing"] = new DesktopIconPosition { ParsingName = "missing", DisplayName = "missing", X = 10, Y = 10 };
        var result = new DesktopIconLayoutEngine(provider).Restore(profile, new Int32Rect(0, 0, 1000, 700));
        Assert.AreEqual(0, result.Applied); Assert.AreEqual(1, result.Skipped);
    }

    [TestMethod]
    public void OutOfBoundsIconIsClamped()
    {
        var provider = new FakeDesktopIconProvider();
        var profile = new DesktopIconProfile();
        profile.Positions["one"] = new DesktopIconPosition { ParsingName = "one", X = -500, Y = 900 };
        var result = new DesktopIconLayoutEngine(provider).Restore(profile, new Int32Rect(0, 0, 1000, 700));
        Assert.AreEqual(1, result.Applied); Assert.AreEqual(0, provider.Applied[0].X); Assert.AreEqual(699, provider.Applied[0].Y);
    }

    [TestMethod]
    public void OneIconFailureDoesNotStopOtherIcons()
    {
        var provider = new SelectiveDesktopIconProvider("missing");
        var profile = new DesktopIconProfile();
        profile.Positions["one"] = new DesktopIconPosition { ParsingName = "one", X = 10, Y = 10 };
        profile.Positions["missing"] = new DesktopIconPosition { ParsingName = "missing", X = 20, Y = 20 };
        var result = new DesktopIconLayoutEngine(provider).Restore(profile, new Int32Rect(0, 0, 1000, 700));
        Assert.AreEqual(1, result.Applied); Assert.AreEqual(1, result.Skipped);
    }

    [TestMethod]
    public void ViewSettingsFailureIsRecorded()
    {
        var provider = new FakeDesktopIconProvider { SettingsResult = false };
        var result = new DesktopIconLayoutEngine(provider).Restore(new DesktopIconProfile(), new Int32Rect(0, 0, 1000, 700));
        Assert.IsTrue(result.Reasons.Any(x => x.Contains("视图设置")));
    }

    private sealed class SelectiveDesktopIconProvider : IDesktopIconProvider
    {
        private readonly string _missing;
        public SelectiveDesktopIconProvider(string missing) => _missing = missing;
        public IReadOnlyList<DesktopIconPosition> Capture() => Array.Empty<DesktopIconPosition>();
        public bool TrySetPosition(DesktopIconPosition position) => !position.ParsingName.Equals(_missing, StringComparison.OrdinalIgnoreCase);
        public bool TrySetViewSettings(int iconSize, bool autoArrange, bool alignToGrid) => true;
    }
}
