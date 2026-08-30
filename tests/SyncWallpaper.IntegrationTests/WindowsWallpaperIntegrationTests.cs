using System.Diagnostics;
using SyncWallpaper.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.IntegrationTests;

[TestClass]
public sealed class WindowsWallpaperIntegrationTests
{
    [TestMethod]
    public void QueryDisplayConfigAndWmiReturnActiveMonitorIdentities()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows APIs are unavailable.");
        var monitors = new MonitorDiscoveryService().Discover();
        if (monitors.Count == 0) Assert.Inconclusive("The current session has no active display paths.");
        Assert.IsTrue(monitors.All(x => !string.IsNullOrWhiteSpace(x.MonitorDevicePath)));
        Assert.IsTrue(monitors.All(x => x.Width > 0 && x.Height > 0));
        Assert.IsTrue(monitors.All(x => x.Rotation is >= 1 and <= 4));
        Assert.IsTrue(monitors.All(x => x.Dpi >= 96));
    }

    [TestMethod]
    public void ReadOnlyDisplayFactsIncludeConnectorGeometryAndStableIdentity()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows APIs are unavailable.");
        var monitors = new MonitorDiscoveryService().Discover();
        if (monitors.Count == 0) Assert.Inconclusive("The current session has no active display paths.");
        foreach (var monitor in monitors)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(monitor.AdapterId));
            Assert.IsTrue(monitor.RefreshRateDenominator > 0);
            Assert.AreNotEqual(MonitorIdentitySource.Unknown, monitor.StableIdSource);
            Assert.IsFalse(string.IsNullOrWhiteSpace(monitor.StableId));
        }
    }

    [TestMethod]
    public void WallpaperSnapshotIsReadOnlyAndCoversActiveMonitors()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows APIs are unavailable.");
        var snapshot = new WallpaperSnapshotService().Capture();
        Assert.IsFalse(snapshot.SystemMutation);
        if (snapshot.Error is not null) Assert.Inconclusive("Explorer wallpaper API is unavailable: " + snapshot.Error);
        Assert.IsTrue(snapshot.Monitors.Count >= snapshot.ActiveMonitorCount);
    }

    [TestMethod]
    public void RepeatedMonitorDiscoveryDoesNotLeakHandles()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows APIs are unavailable.");
        var discovery = new MonitorDiscoveryService();
        _ = discovery.Discover();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var before = process.HandleCount;
        for (var i = 0; i < 50; i++) _ = discovery.Discover();
        process.Refresh();
        Assert.IsTrue(process.HandleCount - before < 100, $"50 monitor discoveries added {process.HandleCount - before} handles.");
    }
}
