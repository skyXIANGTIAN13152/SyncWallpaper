using SyncWallpaper.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.Tests;

[TestClass]
public class WallpaperLibraryRefreshTests
{
    [TestMethod]
    public void RefreshMarksDeletedManagedFilesAndRecoversRestoredFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "SyncWallpaperLibrary", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigurationStore(new DataPaths(root));
            var managedPath = Path.Combine(store.Paths.Wallpapers, "wp-present.jpg");
            File.WriteAllBytes(managedPath, new byte[] { 1, 2, 3 });
            var document = new LibraryDocument();
            document.Assets.Add(new WallpaperAsset { Id = "present", DisplayName = "Present", ManagedRelativePath = "Wallpapers/wp-present.jpg" });
            document.Assets.Add(new WallpaperAsset { Id = "missing", DisplayName = "Missing", ManagedRelativePath = "Wallpapers/wp-missing.jpg" });
            store.Save("library.json", document);

            var service = new WallpaperLibraryService(store);
            var first = service.Refresh();
            Assert.AreEqual(1, first.MissingCount);
            Assert.IsTrue(first.Document.Assets.Single(x => x.Id == "missing").IsMissing);
            Assert.IsFalse(first.Document.Assets.Single(x => x.Id == "present").IsMissing);

            File.WriteAllBytes(Path.Combine(store.Paths.Wallpapers, "wp-missing.jpg"), new byte[] { 4 });
            var second = service.Refresh();
            Assert.AreEqual(1, second.RecoveredCount);
            Assert.IsFalse(second.Document.Assets.Single(x => x.Id == "missing").IsMissing);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
