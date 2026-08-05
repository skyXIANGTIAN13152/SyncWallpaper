namespace SyncWallpaper.Core;

public sealed record DpiLayoutDisplay(string StableId, Int32Rect Bounds, double DpiScale, bool IsPortrait);

public sealed record DpiLayoutValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public static class MixedDpiLayoutValidator
{
    public static DpiLayoutValidationResult Validate(IEnumerable<DpiLayoutDisplay> displays)
    {
        var items = displays.ToArray();
        var errors = new List<string>();
        var warnings = new List<string>();
        foreach (var display in items)
        {
            if (string.IsNullOrWhiteSpace(display.StableId)) errors.Add("显示器缺少稳定身份。");
            if (display.Bounds.Width <= 0 || display.Bounds.Height <= 0) errors.Add(display.StableId + " 的桌面矩形无效。");
            if (display.DpiScale < 1.0 || display.DpiScale > 3.0) errors.Add(display.StableId + " 的 DPI 缩放超出支持范围（100%–300%）。");
            if (display.IsPortrait && display.Bounds.Height < display.Bounds.Width) warnings.Add(display.StableId + " 标记为竖屏但几何宽高为横向。");
        }
        for (var i = 0; i < items.Length; i++)
            for (var j = i + 1; j < items.Length; j++)
                if (Intersects(items[i].Bounds, items[j].Bounds)) errors.Add(items[i].StableId + " 与 " + items[j].StableId + " 的桌面区域重叠。");
        if (items.Select(x => x.DpiScale).Distinct().Count() > 1) warnings.Add("检测到混合 DPI；窗口恢复必须经过每显示器坐标换算。");
        return new(errors.Count == 0, errors, warnings);
    }

    public static int ScaleLogicalCoordinate(int logical, double sourceScale, double targetScale)
    {
        if (sourceScale <= 0 || targetScale <= 0) throw new ArgumentOutOfRangeException();
        return (int)Math.Round(logical * sourceScale / targetScale, MidpointRounding.AwayFromZero);
    }

    private static bool Intersects(Int32Rect left, Int32Rect right)
        => left.Left < right.Left + right.Width && right.Left < left.Left + left.Width
            && left.Top < right.Top + right.Height && right.Top < left.Top + left.Height;
}
