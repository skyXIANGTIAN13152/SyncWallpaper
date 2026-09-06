using SyncWallpaper.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class WallpaperApplySafetyTests
{
    [TestMethod]
    public async Task DuplicateTargetIsRejectedBeforeOpeningTheDesktopInterface()
    {
        using var data = new ApplyFixture();
        var roles = data.Match.Profile!.Roles;
        data.Match.RoleMatches[roles[3].RoleId] = data.Match.RoleMatches[roles[1].RoleId];
        var opens = 0;
        var service = new WallpaperApplyService(data.Renderer, desktopFactory: () => { opens++; return data.Desktop; });
        var result = await service.ApplyAsync(data.Match, Array.Empty<WallpaperAsset>(), data.Paths);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, opens);
        Assert.AreEqual(0, result.AppliedCount);
        StringAssert.Contains(result.Message, "同一台显示器");
    }

    [TestMethod]
    public async Task FourScreenReapplyVerifiesFourDistinctTargetsAndKeepsEachImage()
    {
        using var data = new ApplyFixture();
        var service = data.Service();
        var first = await service.ApplyAsync(data.Match, Array.Empty<WallpaperAsset>(), data.Paths);
        Assert.IsTrue(first.Success);
        Assert.AreEqual(4, first.AppliedCount);
        Assert.AreEqual(4, data.Desktop.Writes.Select(x => x.Monitor).Distinct().Count());
        data.AssertDesired();
        data.Desktop.Writes.Clear();
        var second = await service.ApplyAsync(data.Match, Array.Empty<WallpaperAsset>(), data.Paths);
        Assert.IsTrue(second.Success);
        Assert.AreEqual(4, second.AppliedCount);
        Assert.AreEqual(0, data.Desktop.Writes.Count);
        data.AssertDesired();
    }

    [TestMethod]
    public async Task MissingFourthImageLeavesAllFourWallpapersUntouched()
    {
        using var data = new ApplyFixture();
        File.Delete(data.Match.Profile!.Roles[3].WallpaperPath);
        var result = await data.Service().ApplyAsync(data.Match, Array.Empty<WallpaperAsset>(), data.Paths);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.AppliedCount);
        Assert.AreEqual(0, data.Desktop.Writes.Count);
    }

    [TestMethod]
    public async Task FinalWholeSetVerificationCatchesAnEarlierTargetBeingOverwritten()
    {
        using var data = new ApplyFixture();
        data.Desktop.AfterSet = () =>
        {
            if (data.Desktop.Writes.Count == 4) data.Desktop.Wallpapers["PATH-0"] = "unexpected.png";
        };
        var result = await data.Service().ApplyAsync(data.Match, Array.Empty<WallpaperAsset>(), data.Paths);
        Assert.IsFalse(result.Success);
        foreach (var device in data.Desktop.Wallpapers.Keys)
            Assert.AreEqual("old-" + device, data.Desktop.Wallpapers[device]);
    }

    [TestMethod]
    public async Task EditingLiveProfileDuringApplyDoesNotChangeTheCapturedFourScreenPlan()
    {
        using var data = new ApplyFixture();
        data.Desktop.AfterSet = () =>
        {
            if (data.Desktop.Writes.Count == 1) data.Match.Profile!.Roles.Clear();
        };
        var result = await data.Service().ApplyAsync(data.Match, Array.Empty<WallpaperAsset>(), data.Paths);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(4, result.AppliedCount);
        data.AssertDesired();
    }

    private sealed class ApplyFixture : IDisposable
    {
        public DataPaths Paths { get; } = new(Path.Combine(Path.GetTempPath(), "SyncWallpaperApplyTests", Guid.NewGuid().ToString("N")));
        public WallpaperRenderService Renderer { get; }
        public FakeDesktop Desktop { get; } = new();
        public MatchResult Match { get; }
        private readonly Dictionary<string, string> _desired = new();

        public ApplyFixture()
        {
            Renderer = new WallpaperRenderService(Paths);
            var monitors = Enumerable.Range(0, 4).Select(i => TestData.Monitor("PATH-" + i, "SERIAL-" + i, i * 1920)).ToArray();
            var labels = new[] { "Laptop", "Portrait", "Landscape", "Portrait" };
            var profile = new WallpaperProfile();
            for (var i = 0; i < 4; i++)
            {
                var source = Path.Combine(Paths.Wallpapers, $"source-{i}.png");
                // These tests exercise routing using existing cache files. The
                // fake desktop never decodes or applies images to Windows.
                File.WriteAllText(source, "source fixture " + i);
                var role = new MonitorRoleBinding { Role = labels[i], Fingerprint = monitors[i], WallpaperPath = source };
                profile.Roles.Add(role);
                var cached = Path.Combine(Paths.Rendered, WallpaperCacheKey.Create(FileUtilities.Sha256(source), monitors[i].Width, monitors[i].Height, role.FitMode, role.BackgroundColor) + ".png");
                File.WriteAllText(cached, "cached fixture " + i);
                _desired.Add(monitors[i].MonitorDevicePath, cached);
                Desktop.Wallpapers.Add(monitors[i].MonitorDevicePath, "old-" + monitors[i].MonitorDevicePath);
            }
            Match = new ProfileMatcher().Match(monitors.Reverse().ToArray(), new[] { profile });
        }

        public WallpaperApplyService Service() => new(Renderer, desktopFactory: () => Desktop);
        public void AssertDesired()
        {
            foreach (var target in _desired) Assert.AreEqual(target.Value, Desktop.Wallpapers[target.Key]);
        }
        public void Dispose() { if (Directory.Exists(Paths.Root)) Directory.Delete(Paths.Root, true); }
    }

    private sealed class FakeDesktop : IWallpaperDesktop
    {
        public Dictionary<string, string> Wallpapers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Monitor, string Wallpaper)> Writes { get; } = new();
        public Action? AfterSet { get; set; }
        public IReadOnlyList<string> GetMonitorDevicePaths() => Wallpapers.Keys.Reverse().ToArray();
        public string GetWallpaper(string path) => Wallpapers[path];
        public void SetWallpaper(string path, string wallpaper)
        {
            Wallpapers[path] = wallpaper;
            Writes.Add((path, wallpaper));
            AfterSet?.Invoke();
        }
        public void SetSpanPosition() { }
        public void Dispose() { }
    }
}
