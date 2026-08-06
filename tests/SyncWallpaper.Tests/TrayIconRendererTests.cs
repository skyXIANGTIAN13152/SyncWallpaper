using System.Drawing;
using SyncWallpaper.App;

namespace SyncWallpaper.Tests;

[TestClass]
public sealed class TrayIconRendererTests
{
    [TestMethod]
    public void EachStateRendersAtAllTraySizes()
    {
        foreach (var state in Enum.GetValues<TrayIconState>())
        {
            foreach (var size in new[] { 16, 20, 24, 32, 48, 64 })
            {
                using var bitmap = TrayIconRenderer.Render(state, size);
                Assert.AreEqual(size, bitmap.Width);
                Assert.AreEqual(size, bitmap.Height);
                Assert.IsTrue(HasVisiblePixel(bitmap), $"{state} at {size}px is empty");
            }
        }
    }

    [TestMethod]
    public void EachStateProducesAValidMultiFrameIcon()
    {
        foreach (var state in Enum.GetValues<TrayIconState>())
        {
            using var icon = TrayIconRenderer.Create(state);
            Assert.IsTrue(icon.Width >= 16);
            Assert.IsTrue(icon.Height >= 16);
        }
    }

    private static bool HasVisiblePixel(Bitmap bitmap)
    {
        for (var x = 0; x < bitmap.Width; x++)
            for (var y = 0; y < bitmap.Height; y++)
                if (bitmap.GetPixel(x, y).A > 0) return true;
        return false;
    }
}
