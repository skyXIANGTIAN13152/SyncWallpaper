using System.Runtime.ExceptionServices;
using SyncWallpaper.App;

namespace SyncWallpaper.Tests;

[TestClass]
public class WallpaperPreviewCacheTests
{
    [TestMethod]
    public void TruncatedPngReturnsNoPreviewWithoutChangingOrLockingTheSource()
    {
        RunSta(() =>
        {
            // A PNG header without image data makes WPF throw FileFormatException,
            // which derives from FormatException rather than IOException.
            var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAA");
            var path = Path.Combine(Path.GetTempPath(), $"SyncWallpaperPreview-{Guid.NewGuid():N}.png");
            try
            {
                File.WriteAllBytes(path, bytes);
                var cache = new WallpaperPreviewCache();

                Assert.IsNull(cache.Load(path));
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
                using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    Assert.AreEqual((long)bytes.Length, exclusive.Length);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Wallpaper preview regression test did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
