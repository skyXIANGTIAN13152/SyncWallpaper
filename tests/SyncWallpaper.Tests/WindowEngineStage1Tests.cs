using SyncWallpaper.Core;
using SyncWallpaper.WindowEngine;
using CoreRect = SyncWallpaper.Core.Int32Rect;

namespace SyncWallpaper.Tests;

[TestClass]
public class WindowEngineStage1Tests
{
    [TestMethod]
    public void ExecutablePathIsTheStrongWindowIdentity()
    {
        var matcher = new WindowIdentityMatcher();
        var ok = matcher.IsMatch(new WindowIdentity { ExecutablePath = @"C:\Apps\one.exe", WindowClass = "Main" }, new WindowIdentity { ExecutablePath = @"C:\Apps\one.exe", WindowClass = "Main", WindowTitle = "A" }, out var by);
        Assert.IsTrue(ok); Assert.AreEqual(WindowMatchKind.ExecutablePath, by);
    }

    [TestMethod]
    public void AppUserModelIdMatchesWhenPathIsUnavailable()
    {
        var matcher = new WindowIdentityMatcher();
        Assert.IsTrue(matcher.IsMatch(new WindowIdentity { AppUserModelId = "Contoso.App" }, new WindowIdentity { AppUserModelId = "Contoso.App", IsUwp = true }, out var by));
        Assert.AreEqual(WindowMatchKind.AppUserModelId, by);
    }

    [TestMethod]
    public void SameTitleWithDifferentClassDoesNotMatch()
    {
        var matcher = new WindowIdentityMatcher();
        Assert.IsFalse(matcher.IsMatch(new WindowIdentity { ExecutablePath = "app.exe", WindowClass = "One", WindowTitle = "文档*" },
            new WindowIdentity { ExecutablePath = "app.exe", WindowClass = "Two", WindowTitle = "文档 A" }, out _));
    }

    [TestMethod]
    public void TitleOnlyMatchingIsRejected()
    {
        var matcher = new WindowIdentityMatcher();
        Assert.IsFalse(matcher.IsMatch(new WindowIdentity { WindowTitle = "文档*" }, new WindowIdentity { WindowTitle = "文档 A" }, out _));
    }

    [TestMethod]
    public void WildcardTitlePatternMatchesAfterStrongIdentity()
    {
        var matcher = new WindowIdentityMatcher();
        Assert.IsTrue(matcher.IsMatch(new WindowIdentity { ProcessName = "editor", WindowTitle = "文档*" },
            new WindowIdentity { ProcessName = "editor", WindowTitle = "文档 A" }, out var by));
        Assert.AreEqual(WindowMatchKind.TitlePattern, by);
    }

    [TestMethod]
    public void MixedDpiCoordinatesAreConvertedToPhysicalBounds()
    {
        var platform = new FakeWindowPlatform();
        platform.Windows.Add(Snapshot(@"C:\Apps\one.exe", "Main", "Editor", 144));
        var engine = new WindowLayoutEngine(platform);
        var profile = Profile(new WindowPlacement
        {
            ProcessPath = @"C:\Apps\one.exe", WindowClass = "Main", MonitorDevicePath = "PATH-A",
            SavedMonitorX = 0, SavedMonitorY = 0, SavedMonitorWidth = 1920, SavedMonitorHeight = 1080,
            Left = 480, Top = 270, Width = 960, Height = 540, Dpi = 96
        });
        var result = engine.Restore(profile, new[] { TestData.Monitor("PATH-A", x: 0, width: 3840, height: 2160) });
        Assert.AreEqual(1, result.Applied); Assert.IsTrue(platform.Applied[0].Bounds.Width > 960); Assert.IsTrue(platform.Applied[0].Bounds.Left >= 0);
    }

    [TestMethod]
    public void MissingTargetMonitorFallsBackToPrimary()
    {
        var platform = new FakeWindowPlatform(); platform.Windows.Add(Snapshot(@"C:\Apps\one.exe", "Main", "Editor", 96));
        var profile = Profile(new WindowPlacement { ProcessPath = @"C:\Apps\one.exe", WindowClass = "Main", MonitorDevicePath = "MISSING", SavedMonitorWidth = 1920, SavedMonitorHeight = 1080, Width = 800, Height = 600 });
        var result = new WindowLayoutEngine(platform).Restore(profile, new[] { TestData.Monitor("PATH-A", x: 0) });
        Assert.AreEqual(1, result.Applied); Assert.AreEqual(0, result.Skipped);
    }

    [TestMethod]
    public void MaximizeIsAppliedAfterPosition()
    {
        var platform = new FakeWindowPlatform(); platform.Windows.Add(Snapshot(@"C:\Apps\one.exe", "Main", "Editor", 96));
        var profile = Profile(new WindowPlacement { ProcessPath = @"C:\Apps\one.exe", WindowClass = "Main", MonitorDevicePath = "PATH-A", SavedMonitorWidth = 1920, SavedMonitorHeight = 1080, Width = 800, Height = 600, Maximize = true });
        new WindowLayoutEngine(platform).Restore(profile, new[] { TestData.Monitor("PATH-A") });
        Assert.IsTrue(platform.Applied[0].Maximize);
    }

    [TestMethod]
    public void RestoredWindowIsKeptVisibleOnVirtualDesktop()
    {
        var platform = new FakeWindowPlatform(); platform.Windows.Add(Snapshot(@"C:\Apps\one.exe", "Main", "Editor", 96));
        var profile = Profile(new WindowPlacement { ProcessPath = @"C:\Apps\one.exe", WindowClass = "Main", MonitorDevicePath = "PATH-A", SavedMonitorWidth = 1920, SavedMonitorHeight = 1080, Left = -10000, Top = -10000, Width = 800, Height = 600 });
        new WindowLayoutEngine(platform).Restore(profile, new[] { TestData.Monitor("PATH-A") });
        var applied = platform.Applied[0].Bounds; Assert.IsTrue(applied.Left + applied.Width > 0); Assert.IsTrue(applied.Top + applied.Height > 0);
    }

    [TestMethod]
    public void ElevatedWindowIsSkippedByDefault()
    {
        var platform = new FakeWindowPlatform(); platform.Windows.Add(Snapshot(@"C:\Apps\one.exe", "Main", "Editor", 96, elevated: true));
        var profile = Profile(new WindowPlacement { ProcessPath = @"C:\Apps\one.exe", WindowClass = "Main", MonitorDevicePath = "PATH-A", SavedMonitorWidth = 1920, SavedMonitorHeight = 1080, Width = 800, Height = 600 });
        var result = new WindowLayoutEngine(platform).Restore(profile, new[] { TestData.Monitor("PATH-A") });
        Assert.AreEqual(0, result.Applied); StringAssert.Contains(result.Reasons[0], "高权限");
    }

    [TestMethod]
    public void UnlaunchedApplicationsAreNotStartedByDefault()
    {
        var platform = new FakeWindowPlatform();
        var profile = Profile(new WindowPlacement { ProcessPath = @"C:\Apps\one.exe", WindowClass = "Main", AllowLaunch = true, SavedMonitorWidth = 1920, SavedMonitorHeight = 1080, Width = 800, Height = 600 });
        var result = new WindowLayoutEngine(platform).Restore(profile, new[] { TestData.Monitor("PATH-A") });
        Assert.AreEqual(0, platform.Started.Count); Assert.AreEqual(1, result.Skipped);
    }

    [TestMethod]
    public void UnsafeLaunchPathIsRejected()
    {
        var platform = new FakeWindowPlatform();
        var profile = Profile(new WindowPlacement { ProcessPath = Path.Combine(Path.GetTempPath(), "danger.exe"), WindowClass = "Main", AllowLaunch = true, SavedMonitorWidth = 1920, SavedMonitorHeight = 1080, Width = 800, Height = 600 });
        new WindowLayoutEngine(platform).Restore(profile, new[] { TestData.Monitor("PATH-A") }, new WindowRestoreOptions { StartUnlaunchedApplications = true });
        Assert.AreEqual(0, platform.Started.Count);
    }

    private static WindowPositionSnapshot Snapshot(string path, string klass, string title, int dpi, bool elevated = false)
        => new() { Handle = new IntPtr(1), Identity = new WindowIdentity { ExecutablePath = path, ProcessName = Path.GetFileNameWithoutExtension(path), WindowClass = klass, WindowTitle = title, IsElevated = elevated }, MonitorDevicePath = "PATH-A", PhysicalBounds = new CoreRect(100, 100, 800, 600), Dpi = dpi };

    private static WindowPositionProfile Profile(WindowPlacement placement) => new() { Name = "test", Windows = new List<WindowPlacement> { placement } };
}
