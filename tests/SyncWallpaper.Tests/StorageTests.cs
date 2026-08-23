using SyncWallpaper.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.Tests;

[TestClass]
public class StorageTests
{
    [TestMethod]
    public void ConfigurationSaveIsAtomicAndDefaultKeepsNoHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "SyncWallpaperTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigurationStore(new DataPaths(root));
            store.Save("settings.json", new AppSettings { EditingProfileId = "first" });
            store.Save("settings.json", new AppSettings { EditingProfileId = "second" });
            var loaded = store.Load("settings.json", new AppSettings());
            Assert.AreEqual("second", loaded.EditingProfileId);
            Assert.IsFalse(File.Exists(Path.Combine(root, "Backups", "settings.json.bak")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "Config", "settings.json.tmp")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void ConfigurationStoreKeepsFiveRecoveryPointsAndRestoresExplicitly()
    {
        var root = Path.Combine(Path.GetTempPath(), "SyncWallpaperTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigurationStore(new DataPaths(root), recoveryVersions: 5);
            for (var i = 0; i < 7; i++) store.Save("settings.json", new AppSettings { EditingProfileId = "v" + i });
            var recovery = store.ListRecoveryPoints("settings.json");
            Assert.IsTrue(recovery.Count >= 5);
            Assert.IsTrue(recovery.Count <= 6);
            store.Restore("settings.json", 2);
            Assert.AreEqual("v4", store.Load("settings.json", new AppSettings()).EditingProfileId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void ConfigurationStoreRejectsTraversalAndOversizedJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "SyncWallpaperTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigurationStore(new DataPaths(root));
            Assert.ThrowsException<ArgumentException>(() => store.Save("../settings.json", new AppSettings()));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void CacheKeyChangesWithSizeAndFitMode()
    {
        var first = WallpaperCacheKey.Create("hash", 1920, 1080, WallpaperFitMode.Fill, "#000000");
        var second = WallpaperCacheKey.Create("hash", 1080, 1920, WallpaperFitMode.Fit, "#000000");
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void LegacyActiveProfileMigratesOnlyToEditingSelection()
    {
        var settings = new AppSettings { SchemaVersion = 1, ActiveProfileId = "legacy-profile", LowPerformanceMode = false };

        var changed = AppSettingsMigrator.Migrate(settings);

        Assert.IsTrue(changed);
        Assert.AreEqual(2, settings.SchemaVersion);
        Assert.AreEqual("legacy-profile", settings.EditingProfileId);
        Assert.IsNull(settings.LastMatchedProfileId);
        Assert.IsNull(settings.ActiveProfileId);
    }

    [TestMethod]
    public void RenderCacheUsesBoundedLeastRecentlyWrittenFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "SyncWallpaperTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new DataPaths(root);
            paths.Ensure();
            var oldest = Path.Combine(paths.Rendered, "oldest.png");
            var newest = Path.Combine(paths.Rendered, "newest.png");
            File.WriteAllBytes(oldest, new byte[8]);
            File.WriteAllBytes(newest, new byte[8]);
            File.SetLastWriteTimeUtc(oldest, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(newest, DateTime.UtcNow.AddMinutes(-1));

            new WallpaperRenderService(paths).ConfigureCacheLimit(10);

            Assert.IsFalse(File.Exists(oldest));
            Assert.IsTrue(File.Exists(newest));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task DebouncerRunsOnlyOnceForBurst()
    {
        var count = 0;
        using var done = new ManualResetEventSlim(false);
        using var debouncer = new EventDebouncer(TimeSpan.FromMilliseconds(80), _ => { Interlocked.Increment(ref count); done.Set(); return Task.CompletedTask; });
        debouncer.Signal(); debouncer.Signal(); debouncer.Signal();
        Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(2)));
        await Task.Delay(120);
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void DirectorySizeIncludesNestedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "SyncWallpaperTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "a"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a", "one.bin"), new byte[7]);
            Assert.AreEqual(7, FileUtilities.DirectorySize(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
