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

    [TestMethod]
    public void StateMarksRemainDistinctByShapeAtSmallestTraySize()
    {
        var states = Enum.GetValues<TrayIconState>();
        for (var first = 0; first < states.Length; first++)
            for (var second = first + 1; second < states.Length; second++)
            {
                using var left = TrayIconRenderer.Render(states[first], 16);
                using var right = TrayIconRenderer.Render(states[second], 16);
                var differentPixels = 0;
                // Ignore RGB values: recoloring the same badge cannot pass.
                for (var x = 0; x < 16; x++)
                    for (var y = 0; y < 5; y++)
                        if ((left.GetPixel(x, y).A >= 80) != (right.GetPixel(x, y).A >= 80)) differentPixels++;
                Assert.IsTrue(differentPixels >= 3, $"{states[first]} and {states[second]} need distinguishable shapes at 16 px.");
            }
    }

    [TestMethod]
    public void TrayArtworkKeepsTheBackgroundAndBothScreenInteriorsTransparent()
    {
        foreach (var state in Enum.GetValues<TrayIconState>())
        {
            using var bitmap = TrayIconRenderer.Render(state, 16);
            Assert.AreEqual(0, (int)bitmap.GetPixel(0, 0).A, $"{state} must not have a backing plate.");
            Assert.AreEqual(0, (int)bitmap.GetPixel(7, 9).A, $"{state} landscape screen must remain hollow.");
            Assert.AreEqual(0, (int)bitmap.GetPixel(12, 11).A, $"{state} portrait screen must remain hollow.");
        }
    }

    [TestMethod]
    public void ActiveMatchesUseTheApprovedWarmPalette()
    {
        using var normal = TrayIconRenderer.Render(TrayIconState.Normal, 32);
        using var generic = TrayIconRenderer.Render(TrayIconState.Generic, 32);
        Assert.IsTrue(HasColor(normal, 210, 180, 122), "Specific matching should use antique gold.");
        Assert.IsTrue(HasColor(generic, 210, 180, 122), "Generic matching should keep the same gold screen outline.");
        Assert.IsTrue(HasColor(generic, 243, 235, 220), "Generic matching should have a warm-white diamond.");
        using var error = TrayIconRenderer.Render(TrayIconState.Error, 32);
        Assert.IsTrue(HasColor(error, 213, 160, 133), "Errors should use soft copper.");
    }

    [DataTestMethod]
    [DataRow(false, true, "通用匹配", true, TrayIconState.Generic)]
    [DataRow(false, true, "通用匹配", false, TrayIconState.Normal)]
    [DataRow(false, true, "已匹配", true, TrayIconState.Normal)]
    [DataRow(false, true, "已匹配", false, TrayIconState.Normal)]
    [DataRow(false, false, "通用匹配", true, TrayIconState.Paused)]
    [DataRow(false, true, "已暂停", true, TrayIconState.Paused)]
    [DataRow(false, true, "安全模式", true, TrayIconState.Paused)]
    [DataRow(false, true, "验证模式：仅检测", true, TrayIconState.Paused)]
    [DataRow(false, true, "需要确认", true, TrayIconState.Error)]
    [DataRow(false, true, "保持当前壁纸", true, TrayIconState.Unmatched)]
    [DataRow(false, true, "应用未完成", true, TrayIconState.Error)]
    [DataRow(false, true, "应用失败", true, TrayIconState.Error)]
    [DataRow(false, true, "未发现显示器", true, TrayIconState.Unmatched)]
    [DataRow(false, true, "监测中", false, TrayIconState.Unmatched)]
    [DataRow(false, true, "等待显示器检测", false, TrayIconState.Unmatched)]
    [DataRow(false, false, "应用失败", true, TrayIconState.Paused)]
    [DataRow(true, true, "通用匹配", true, TrayIconState.Recognizing)]
    [DataRow(true, false, "应用失败", true, TrayIconState.Recognizing)]
    public void StateResolverOnlyUsesGenericForSuccessfulActiveMatches(
        bool isRecognizing, bool autoMatchEnabled, string statusText, bool isGenericMatch, TrayIconState expected)
    {
        Assert.AreEqual(expected, TrayIconStateResolver.Resolve(isRecognizing, autoMatchEnabled, statusText, isGenericMatch));
    }

    private static bool HasColor(Bitmap bitmap, int red, int green, int blue)
    {
        for (var x = 0; x < bitmap.Width; x++)
            for (var y = 0; y < bitmap.Height; y++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A >= 180 && Math.Abs(pixel.R - red) <= 2
                    && Math.Abs(pixel.G - green) <= 2 && Math.Abs(pixel.B - blue) <= 2) return true;
            }
        return false;
    }

    private static bool HasVisiblePixel(Bitmap bitmap)
    {
        for (var x = 0; x < bitmap.Width; x++)
            for (var y = 0; y < bitmap.Height; y++)
                if (bitmap.GetPixel(x, y).A > 0) return true;
        return false;
    }
}
