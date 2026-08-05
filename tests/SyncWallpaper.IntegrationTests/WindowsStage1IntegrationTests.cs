using SyncWallpaper.AudioEngine;
using SyncWallpaper.Core;
using SyncWallpaper.DesktopEngine;
using SyncWallpaper.DisplayEngine;
using SyncWallpaper.Windows;

namespace SyncWallpaper.IntegrationTests;

[TestClass]
public class WindowsStage1IntegrationTests
{
    [TestMethod]
    public void QueryDisplayConfigAndWmiDiscoveryWorksOrSkips()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows API 不可用。");
        var monitors = new MonitorDiscoveryService().Discover();
        if (monitors.Count == 0) Assert.Inconclusive("当前会话没有活动显示路径。");
        Assert.IsTrue(monitors.All(x => !string.IsNullOrWhiteSpace(x.MonitorDevicePath)));
    }

    [TestMethod]
    public void CoreAudioEnumerationWorksOrSkips()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Core Audio 只在 Windows 上运行。");
        using var provider = new WindowsCoreAudioEndpointProvider();
        var endpoints = provider.Enumerate();
        if (endpoints.Count == 0) Assert.Inconclusive("当前会话没有可枚举音频端点。");
        Assert.IsTrue(endpoints.All(x => !string.IsNullOrWhiteSpace(x.DeviceId)));
    }

    [TestMethod]
    public void CoreAudioDefaultRolesAreReadableOrSkips()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Core Audio 只在 Windows 上运行。");
        using var provider = new WindowsCoreAudioEndpointProvider();
        if (provider.Enumerate().Count == 0) Assert.Inconclusive("当前会话没有可枚举音频端点。");
        var roles = new[]
        {
            AudioEndpointRole.Console,
            AudioEndpointRole.Multimedia,
            AudioEndpointRole.Communications,
            AudioEndpointRole.Recording
        };
        var defaults = roles.Select(provider.GetDefault).ToArray();
        if (defaults.Any(x => x is null)) Assert.Inconclusive("当前会话没有为所有角色提供默认端点。");
        Assert.IsTrue(defaults.All(x => !string.IsNullOrWhiteSpace(x!.DeviceId)));
    }

    [TestMethod]
    public void DesktopShellEnumerationWorksOrSkips()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Shell 接口只在 Windows 上运行。");
        var provider = new WindowsShellDesktopIconProvider(() => new MonitorDiscoveryService().Discover());
        var positions = provider.Capture();
        if (positions.Count == 0) Assert.Inconclusive("当前桌面 Shell 视图没有可读取项目或系统拒绝接口。");
        Assert.IsTrue(positions.All(x => !string.IsNullOrWhiteSpace(x.ParsingName)));
    }

    [TestMethod]
    public async Task DisplayConfigurationNoOpRoundTripRequiresManualOptIn()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SYNCWALLPAPER_REAL_DISPLAY_TEST"), "1", StringComparison.Ordinal))
            Assert.Inconclusive("设置 SYNCWALLPAPER_REAL_DISPLAY_TEST=1 后才会执行不改变当前模式的 CCD 回环验证。");
        var discovery = new MonitorDiscoveryService();
        var adapter = new WindowsDisplayConfigurationAdapter(discovery);
        var snapshot = adapter.Capture();
        var precheck = new DisplayConfigurationValidator(adapter).Validate(snapshot.Profile, snapshot);
        if (!precheck.IsValid)
        {
            var modeDump = string.Join("|", snapshot.Profile.Displays.SelectMany(x => adapter.GetModes(x.MonitorFingerprint).Select(m => $"{x.MonitorFingerprint.DisplayLabel}:{m.Width}x{m.Height}@{m.RefreshRateNumerator}/{m.RefreshRateDenominator}")));
            Assert.Fail(string.Join("；", precheck.Errors) + " modes=" + modeDump);
        }
        var transaction = new DisplayConfigurationTransactionService(
            adapter, new DisplayConfigurationValidator(adapter), adapter, adapter, adapter,
            new WindowsDisplayChangeStabilizer(adapter, TimeSpan.FromMilliseconds(250)), new IntegrationLogger());
        var result = await transaction.ApplyAsync(snapshot.Profile, new DisplayConfigurationApplyOptions { RequireConfirmation = false });
        Assert.AreEqual(DisplayConfigurationTransactionStatus.Applied, result.Status, result.Message + " " + string.Join("；", result.Validation?.Errors ?? Array.Empty<string>()));
    }

    private sealed class IntegrationLogger : IStage1Logger
    {
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message) { }
    }
}
