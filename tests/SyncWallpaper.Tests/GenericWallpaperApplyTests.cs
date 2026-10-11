using SyncWallpaper.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class GenericWallpaperApplyTests
{
    [TestMethod]
    public void TinyCacheLimitPreservesOriginalImagesForSecondScreenRollback()
    {
        RunSta(() =>
        {
            using var fixture = new GenericApplyFixture();
            var originals = fixture.UseRealSourcesAndCachedOriginals();
            var service = fixture.CreateService();
            Assert.IsTrue(service.TryRefreshCacheProtection());
            fixture.Renderer.ConfigureCacheLimit(1);
            fixture.Desktop.AfterSet = () =>
            {
                Assert.IsTrue(originals.Values.All(File.Exists), "Preparing new images must not evict rollback images.");
                if (fixture.Desktop.Writes.Count == 2) throw new IOException("Second screen failed after writing");
            };

            var result = service.ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.IsTrue(service.LastTransaction.RollbackSucceeded);
            fixture.Renderer.TrimCache();
            foreach (var original in originals)
            {
                Assert.AreEqual(original.Value, fixture.Desktop.Wallpapers[original.Key]);
                Assert.IsTrue(File.Exists(original.Value));
            }
        });
    }

    [TestMethod]
    public void TinyCacheLimitKeepsActiveImagesButReleasesSupersededProtection()
    {
        RunSta(() =>
        {
            using var fixture = new GenericApplyFixture();
            var originals = fixture.UseRealSourcesAndCachedOriginals();
            var service = fixture.CreateService();
            Assert.IsTrue(service.TryRefreshCacheProtection());
            fixture.Renderer.ConfigureCacheLimit(1);

            var result = service.ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, result.Message);
            fixture.Renderer.TrimCache();
            Assert.IsTrue(fixture.Desktop.Wallpapers.Values.All(File.Exists), "Active images may exceed the limit.");
            Assert.IsTrue(File.Exists(originals["RIGHT-LANDSCAPE"]), "The deliberately untouched screen remains protected.");
            Assert.IsFalse(File.Exists(originals["CENTER-LANDSCAPE"]));
            Assert.IsFalse(File.Exists(originals["LEFT-PORTRAIT"]));

            // A later complete snapshot replaces the active set instead of
            // permanently accumulating every wallpaper ever applied.
            var external = Path.Combine(fixture.Paths.Wallpapers, "now-used-externally.png");
            WriteTestPng(external);
            foreach (var path in fixture.Desktop.Wallpapers.Keys.ToArray()) fixture.Desktop.Wallpapers[path] = external;
            Assert.IsTrue(service.TryRefreshCacheProtection());
            fixture.Renderer.TrimCache();
            Assert.AreEqual(0, Directory.GetFiles(fixture.Paths.Rendered, "*.png").Length);
        });
    }

    [TestMethod]
    public void IncompleteCurrentWallpaperReadDefersCacheCleanup()
    {
        RunSta(() =>
        {
            using var fixture = new GenericApplyFixture();
            var originals = fixture.UseRealSourcesAndCachedOriginals();
            var service = fixture.CreateService();
            Assert.IsTrue(service.TryRefreshCacheProtection());
            fixture.Desktop.Unreadable.Add("RIGHT-LANDSCAPE");

            Assert.IsFalse(service.TryRefreshCacheProtection());
            fixture.Renderer.ConfigureCacheLimit(1);
            fixture.Renderer.TrimCache();

            Assert.IsTrue(originals.Values.All(File.Exists), "A partial snapshot must not discard earlier protection.");
        });
    }

    private static void WriteTestPng(string path)
    {
        var pixels = Enumerable.Repeat((byte)255, 16).ToArray();
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, pixels, 8);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void RunSta(Action test)
        => WpfTestThread.Run(test, timeoutSeconds: 30);

    [TestMethod]
    public async Task ExactGenericLayoutWritesConfiguredScreensAndPreservesBlankSlot()
    {
        using var fixture = new GenericApplyFixture();
        var service = fixture.CreateService();
        var result = await service.ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(2, result.AppliedCount);
        Assert.AreEqual(2, service.LastTransaction.ExpectedCount);
        CollectionAssert.AreEquivalent(new[] { "LEFT-PORTRAIT", "CENTER-LANDSCAPE" }, fixture.Desktop.Writes.ToArray());
        Assert.AreEqual("old-RIGHT-LANDSCAPE", fixture.Desktop.Wallpapers["RIGHT-LANDSCAPE"]);
        Assert.AreEqual(0, fixture.Desktop.SpanChanges);
        Assert.AreEqual(3, fixture.Match.Profile!.Roles.Count, "An intentionally blank slot must remain saved.");
        StringAssert.Contains(result.Message, "1 台保留当前壁纸");
    }

    [TestMethod]
    public async Task MissingConfiguredImagePreventsPartialWrites()
    {
        using var fixture = new GenericApplyFixture();
        File.Delete(fixture.Match.Profile!.Roles.Single(x => x.GenericOrientation == GenericMonitorOrientation.Portrait).WallpaperPath);
        var result = await fixture.CreateService().ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.Desktop.Writes.Count);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task CorruptSecondImagePreventsEveryWallpaperWrite()
    {
        using var fixture = new GenericApplyFixture();
        var role = fixture.Match.Profile!.Roles.Single(x => x.GenericOrientation == GenericMonitorOrientation.Portrait);
        var monitor = fixture.Match.RoleMatches[role.RoleId];
        // The fixture's text source is intentionally not a decodable image.
        // Leave the first screen's valid cache hit, but force the second to decode.
        var cache = Path.Combine(fixture.Paths.Rendered,
            WallpaperCacheKey.Create(FileUtilities.Sha256(role.WallpaperPath), monitor.Width, monitor.Height, role.FitMode, role.BackgroundColor) + ".png");
        File.Delete(cache);
        var service = fixture.CreateService();

        var result = await service.ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.Desktop.Writes.Count);
        Assert.AreEqual(WallpaperTransactionState.Failed, service.LastTransaction.State);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task NonComFailureAfterWriteRestoresEveryChangedScreen()
    {
        using var fixture = new GenericApplyFixture();
        fixture.Desktop.AfterSet = () =>
        {
            if (fixture.Desktop.Writes.Count == 2) throw new IOException("Unexpected desktop read/write failure");
        };
        var service = fixture.CreateService();

        var result = await service.ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(service.LastTransaction.RollbackSucceeded);
        Assert.AreEqual(WallpaperTransactionState.Failed, service.LastTransaction.State);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task NonComRollbackFailureDoesNotPreventOtherScreensFromRestoring()
    {
        using var fixture = new GenericApplyFixture();
        fixture.Desktop.AfterSet = () =>
        {
            if (fixture.Desktop.Writes.Count == 2) throw new IOException("Unexpected desktop read/write failure");
        };
        fixture.Desktop.FailureBeforeWrite = (path, wallpaper) => path == "LEFT-PORTRAIT" && wallpaper == "old-LEFT-PORTRAIT"
            ? new IOException("Cannot restore this screen") : null;
        var service = fixture.CreateService();

        var result = await service.ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);

        Assert.IsFalse(result.Success);
        Assert.IsFalse(service.LastTransaction.RollbackSucceeded);
        Assert.AreEqual(WallpaperTransactionState.RollbackFailed, service.LastTransaction.State);
        Assert.AreEqual("old-CENTER-LANDSCAPE", fixture.Desktop.Wallpapers["CENTER-LANDSCAPE"]);
        Assert.AreEqual("old-RIGHT-LANDSCAPE", fixture.Desktop.Wallpapers["RIGHT-LANDSCAPE"]);
        Assert.AreNotEqual("old-LEFT-PORTRAIT", fixture.Desktop.Wallpapers["LEFT-PORTRAIT"]);
    }

    [TestMethod]
    public async Task GenericSpanCannotChangeTheGlobalWallpaperPosition()
    {
        using var fixture = new GenericApplyFixture();
        fixture.Match.Profile!.Roles[0].FitMode = WallpaperFitMode.Span;
        var opens = 0;
        var service = new WallpaperApplyService(fixture.Renderer, desktopFactory: () => { opens++; return fixture.Desktop; });
        var result = await service.ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, opens);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task DuplicateGenericTargetIsRejectedBeforeOpeningDesktop()
    {
        using var fixture = new GenericApplyFixture();
        var keys = fixture.Match.RoleMatches.Keys.ToArray();
        fixture.Match.RoleMatches[keys[1]] = fixture.Match.RoleMatches[keys[0]];
        var opens = 0;
        var service = new WallpaperApplyService(fixture.Renderer, desktopFactory: () => { opens++; return fixture.Desktop; });
        var result = await service.ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, opens);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task FailedGenericVerificationRestoresOnlyItsChangedSubset()
    {
        using var fixture = new GenericApplyFixture();
        fixture.Desktop.AfterSet = () =>
        {
            if (fixture.Desktop.Writes.Count == 2)
                fixture.Desktop.Wallpapers[fixture.Desktop.Writes[0]] = "unexpected.png";
        };
        var result = await fixture.CreateService().ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.IsFalse(fixture.Desktop.Writes.Contains("RIGHT-LANDSCAPE"));
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task ChangedProfileCountsRejectTheOldMatchBeforeOpeningDesktop()
    {
        using var fixture = new GenericApplyFixture();
        fixture.Match.Profile!.Roles.Add(new MonitorRoleBinding { GenericOrientation = GenericMonitorOrientation.Landscape, GenericIndex = 3 });
        var opens = 0;
        var service = new WallpaperApplyService(fixture.Renderer, desktopFactory: () => { opens++; return fixture.Desktop; });
        var result = await service.ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, opens);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task LegacyPartialMatchWithoutFullLayoutIsRejected()
    {
        using var fixture = new GenericApplyFixture();
        var legacy = new MatchResult { Profile = fixture.Match.Profile, Status = MatchStatus.Generic, CanAutoApply = true, RoleMatches = fixture.Match.RoleMatches };
        var opens = 0;
        var service = new WallpaperApplyService(fixture.Renderer, desktopFactory: () => { opens++; return fixture.Desktop; });
        var result = await service.ApplyAsync(legacy, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, opens);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task ExtraConnectedScreenPreventsAllGenericWrites()
    {
        using var fixture = new GenericApplyFixture();
        fixture.Desktop.Wallpapers.Add("NEW-SCREEN", "old-NEW-SCREEN");
        var result = await fixture.CreateService().ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.Desktop.Writes.Count);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task DisconnectingAnUnconfiguredScreenStillInvalidatesTheWholeLayout()
    {
        using var fixture = new GenericApplyFixture();
        fixture.Desktop.Wallpapers.Remove("RIGHT-LANDSCAPE");
        var result = await fixture.CreateService().ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.Desktop.Writes.Count);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task RotatingAnUnconfiguredScreenInvalidatesTheOrientationCounts()
    {
        using var fixture = new GenericApplyFixture();
        var original = fixture.Desktop.Bounds["RIGHT-LANDSCAPE"];
        fixture.Desktop.Bounds["RIGHT-LANDSCAPE"] = original with { Width = original.Height, Height = original.Width };
        var result = await fixture.CreateService().ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.Desktop.Writes.Count);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task ScreenAddedDuringTheTransactionRestoresTheChangedWallpaper()
    {
        using var fixture = new GenericApplyFixture();
        fixture.Desktop.AfterSet = () =>
        {
            if (fixture.Desktop.Writes.Count == 1) fixture.Desktop.Wallpapers.Add("NEW-SCREEN", "old-NEW-SCREEN");
        };
        var result = await fixture.CreateService().ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task ExplorerFailureRestoresEveryPreviouslyWrittenScreen()
    {
        using var fixture = new GenericApplyFixture();
        fixture.Desktop.ShouldThrowAfterWrite = (path, wallpaper) => path == "LEFT-PORTRAIT" && !wallpaper.StartsWith("old-", StringComparison.Ordinal);
        var service = fixture.CreateService();
        var result = await service.ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(service.LastTransaction.RollbackSucceeded);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task UnreadableOriginalWallpaperPreventsAllWrites()
    {
        using var fixture = new GenericApplyFixture();
        fixture.Desktop.Unreadable.Add("CENTER-LANDSCAPE");
        var result = await fixture.CreateService().ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.Desktop.Writes.Count);
        fixture.AssertAllOriginal();
    }

    [TestMethod]
    public async Task ExplorerFailureAlsoRestoresAnOriginallyEmptyWallpaper()
    {
        using var fixture = new GenericApplyFixture();
        fixture.Desktop.Wallpapers["CENTER-LANDSCAPE"] = string.Empty;
        fixture.Desktop.ShouldThrowAfterWrite = (path, wallpaper) => path == "LEFT-PORTRAIT" && !wallpaper.StartsWith("old-", StringComparison.Ordinal);
        var service = fixture.CreateService();
        var result = await service.ApplyAsync(fixture.Match, Array.Empty<WallpaperAsset>(), fixture.Paths);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(service.LastTransaction.RollbackSucceeded);
        Assert.AreEqual(string.Empty, fixture.Desktop.Wallpapers["CENTER-LANDSCAPE"]);
        Assert.AreEqual("old-LEFT-PORTRAIT", fixture.Desktop.Wallpapers["LEFT-PORTRAIT"]);
        Assert.AreEqual("old-RIGHT-LANDSCAPE", fixture.Desktop.Wallpapers["RIGHT-LANDSCAPE"]);
    }

    private sealed class GenericApplyFixture : IDisposable
    {
        public DataPaths Paths { get; } = new(Path.Combine(Path.GetTempPath(), "SyncWallpaperGenericApplyTests", Guid.NewGuid().ToString("N")));
        public WallpaperRenderService Renderer { get; }
        public FakeDesktop Desktop { get; } = new();
        public MatchResult Match { get; }

        public GenericApplyFixture()
        {
            Renderer = new WallpaperRenderService(Paths);
            var portrait = TestData.Monitor("LEFT-PORTRAIT", "P", -1080);
            portrait.Width = 1080;
            portrait.Height = 1920;
            var landscape = TestData.Monitor("CENTER-LANDSCAPE", "L1", 0);
            var extra = TestData.Monitor("RIGHT-LANDSCAPE", "L2", 1920);
            var profile = new WallpaperProfile { MatchMode = WallpaperProfileMatchMode.Generic, Name = "通用测试" };
            profile.Roles.Add(CreateRole(GenericMonitorOrientation.Landscape, 1, landscape));
            profile.Roles.Add(CreateRole(GenericMonitorOrientation.Portrait, 1, portrait));
            profile.Roles.Add(new MonitorRoleBinding { GenericOrientation = GenericMonitorOrientation.Landscape, GenericIndex = 2 });
            foreach (var monitor in new[] { extra, portrait, landscape })
            {
                Desktop.Wallpapers.Add(monitor.MonitorDevicePath, "old-" + monitor.MonitorDevicePath);
                Desktop.Bounds.Add(monitor.MonitorDevicePath, new WallpaperMonitorBounds(monitor.DesktopX, monitor.DesktopY, monitor.Width, monitor.Height));
            }
            Match = new ProfileMatcher().Match(new[] { extra, portrait, landscape }, new[] { profile });
            Assert.AreEqual(MatchStatus.Generic, Match.Status);
        }

        private MonitorRoleBinding CreateRole(GenericMonitorOrientation orientation, int index, MonitorIdentity monitor)
        {
            var source = Path.Combine(Paths.Wallpapers, $"{orientation}-{index}.png");
            File.WriteAllText(source, $"generic source {orientation}-{index}");
            var role = new MonitorRoleBinding
            {
                GenericOrientation = orientation, GenericIndex = index,
                Role = orientation + index.ToString(), DisplayName = orientation + index.ToString(), WallpaperPath = source
            };
            var rendered = Path.Combine(Paths.Rendered, WallpaperCacheKey.Create(FileUtilities.Sha256(source), monitor.Width, monitor.Height, role.FitMode, role.BackgroundColor) + ".png");
            File.WriteAllText(rendered, "cached generic test image");
            return role;
        }

        public WallpaperApplyService CreateService() => new(Renderer, desktopFactory: () => Desktop);
        public IReadOnlyDictionary<string, string> UseRealSourcesAndCachedOriginals()
        {
            foreach (var role in Match.Profile!.Roles.Where(role => !string.IsNullOrWhiteSpace(role.WallpaperPath)))
                WriteTestPng(role.WallpaperPath);
            foreach (var device in Desktop.Wallpapers.Keys.ToArray())
            {
                var cached = Path.Combine(Paths.Rendered, "original-" + device + ".png");
                WriteTestPng(cached);
                File.SetLastWriteTimeUtc(cached, DateTime.UtcNow.AddDays(-1));
                Desktop.Wallpapers[device] = cached;
            }
            Desktop.RequireExistingImages = true;
            return new Dictionary<string, string>(Desktop.Wallpapers);
        }
        public void AssertAllOriginal()
        {
            foreach (var pair in Desktop.Wallpapers)
                Assert.AreEqual("old-" + pair.Key, pair.Value);
        }
        public void Dispose() { if (Directory.Exists(Paths.Root)) Directory.Delete(Paths.Root, true); }
    }

    private sealed class FakeDesktop : IWallpaperDesktop
    {
        public Dictionary<string, string> Wallpapers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, WallpaperMonitorBounds> Bounds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Writes { get; } = new();
        public int SpanChanges { get; private set; }
        public Action? AfterSet { get; set; }
        public Func<string, string, Exception?>? FailureBeforeWrite { get; set; }
        public bool RequireExistingImages { get; set; }
        public Func<string, string, bool>? ShouldThrowAfterWrite { get; set; }
        public IReadOnlyList<string> GetMonitorDevicePaths() => Wallpapers.Keys.Reverse().ToArray();
        public WallpaperMonitorBounds? GetMonitorBounds(string path) => Bounds.GetValueOrDefault(path);
        public string GetWallpaper(string path) => Unreadable.Contains(path)
            ? throw new System.Runtime.InteropServices.COMException("Cannot read original wallpaper") : Wallpapers[path];
        public void SetWallpaper(string path, string wallpaper)
        {
            if (RequireExistingImages && !File.Exists(wallpaper)) throw new FileNotFoundException("Wallpaper image was evicted", wallpaper);
            if (FailureBeforeWrite?.Invoke(path, wallpaper) is { } failure) throw failure;
            Wallpapers[path] = wallpaper;
            Writes.Add(path);
            AfterSet?.Invoke();
            if (ShouldThrowAfterWrite?.Invoke(path, wallpaper) == true) throw new System.Runtime.InteropServices.COMException("Explorer unavailable");
        }
        public void SetSpanPosition() => SpanChanges++;
        public void Dispose() { }
    }
}
