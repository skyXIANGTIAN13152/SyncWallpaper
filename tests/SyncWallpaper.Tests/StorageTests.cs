using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public class StorageTests
{
    [TestMethod]
    public void ConfigurationSaveIsReadableAndKeepsBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), "SyncWallpaperTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigurationStore(new DataPaths(root));
            store.Save("settings.json", new AppSettings { ActiveProfileId = "first" });
            store.Save("settings.json", new AppSettings { ActiveProfileId = "second" });
            File.WriteAllText(Path.Combine(root, "Config", "settings.json"), "{broken");
            var loaded = store.Load("settings.json", new AppSettings());
            Assert.AreEqual("first", loaded.ActiveProfileId);
            Assert.IsTrue(File.Exists(Path.Combine(root, "Backups", "settings.json.bak")));
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
