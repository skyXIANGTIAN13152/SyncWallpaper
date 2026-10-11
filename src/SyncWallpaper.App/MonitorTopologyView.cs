using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SyncWallpaper.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using FlowDirection = System.Windows.FlowDirection;

namespace SyncWallpaper.App;

public sealed record DesktopMonitorPreview(MonitorIdentity Monitor, string SlotLabel, ImageSource? Image, string WallpaperName);

/// <summary>Displays the detected desktop geometry. This view never changes a display or wallpaper.</summary>
public sealed class MonitorTopologyView : FrameworkElement
{
    public static readonly DependencyProperty MonitorsProperty = DependencyProperty.Register(
        nameof(Monitors), typeof(IReadOnlyList<DesktopMonitorPreview>), typeof(MonitorTopologyView),
        new FrameworkPropertyMetadata(Array.Empty<DesktopMonitorPreview>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<DesktopMonitorPreview> Monitors
    {
        get => (IReadOnlyList<DesktopMonitorPreview>)GetValue(MonitorsProperty);
        set => SetValue(MonitorsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var monitors = Monitors.Where(x => x.Monitor.Width > 0 && x.Monitor.Height > 0).ToArray();
        if (ActualWidth < 40 || ActualHeight < 80) return;
        if (monitors.Length == 0)
        {
            DrawText(dc, "尚未检测到活动显示器", new Rect(0, 0, ActualWidth, ActualHeight), 15, "TextSecondary", true);
            return;
        }

        var left = monitors.Min(x => (double)x.Monitor.DesktopX);
        var top = monitors.Min(x => (double)x.Monitor.DesktopY);
        var width = monitors.Max(x => (double)x.Monitor.DesktopX + x.Monitor.Width) - left;
        var height = monitors.Max(x => (double)x.Monitor.DesktopY + x.Monitor.Height) - top;
        var scale = Math.Min((ActualWidth - 36) / Math.Max(width, 1), (ActualHeight - 70) / Math.Max(height, 1));
        var origin = new Point((ActualWidth - width * scale) / 2, (ActualHeight - 55 - height * scale) / 2);

        foreach (var preview in monitors)
        {
            var monitor = preview.Monitor;
            var rect = new Rect(origin.X + (monitor.DesktopX - left) * scale + 3,
                origin.Y + (monitor.DesktopY - top) * scale + 3,
                Math.Max(4, monitor.Width * scale - 6), Math.Max(4, monitor.Height * scale - 6));
            var edge = ResourceBrush(monitor.IsPrimary ? "Cyan" : "TextSecondary");
            dc.DrawRoundedRectangle(ResourceBrush("InputBackground"), new Pen(edge, monitor.IsPrimary ? 1.4 : 0.8), rect, 4, 4);
            var inset = Math.Min(5, Math.Min(rect.Width, rect.Height) / 5);
            var picture = new Rect(rect.X + inset, rect.Y + inset, Math.Max(1, rect.Width - inset * 2), Math.Max(1, rect.Height - inset * 2));
            if (preview.Image is { } image)
            {
                var cover = Math.Max(picture.Width / image.Width, picture.Height / image.Height);
                var target = new Rect(picture.X + (picture.Width - image.Width * cover) / 2,
                    picture.Y + (picture.Height - image.Height * cover) / 2, image.Width * cover, image.Height * cover);
                dc.PushClip(new RectangleGeometry(picture, 2, 2));
                dc.DrawImage(image, target);
                dc.Pop();
            }
            else if (picture.Width > 90 && picture.Height > 48)
                DrawText(dc, "当前壁纸\n预览不可用", picture, 11, "TextSecondary", true);

            if (rect.Width > 80 && rect.Height > 40)
            {
                var badge = new Rect(rect.X + 9, rect.Y + 9, monitor.IsPrimary ? 37 : 27, 21);
                dc.DrawRoundedRectangle(ResourceBrush("TranslucentSidebar"), null, badge, 3, 3);
                DrawText(dc, monitor.IsPrimary ? "主屏" : monitor.Width >= monitor.Height ? "横" : "竖", badge, 10, "TextPrimary", true);
            }

            var bottom = rect.Bottom + 4;
            if (monitor.IsInternal)
                dc.DrawLine(new Pen(edge, 2), new Point(rect.Left - 3, bottom), new Point(rect.Right + 3, bottom));
            else
            {
                dc.DrawLine(new Pen(edge, 2), new Point(rect.X + rect.Width / 2, bottom), new Point(rect.X + rect.Width / 2, bottom + 7));
                dc.DrawLine(new Pen(edge, 1.2), new Point(rect.X + rect.Width * .34, bottom + 8), new Point(rect.X + rect.Width * .66, bottom + 8));
            }
            if (rect.Width > 52)
            {
                DrawText(dc, preview.SlotLabel + (monitor.IsInternal ? " · 内置" : ""), new Rect(rect.X, bottom + 14, rect.Width, 18), 11, "TextPrimary", true);
                DrawText(dc, $"{monitor.Width} × {monitor.Height}", new Rect(rect.X, bottom + 32, rect.Width, 15), 10, "TextSecondary", true);
            }
        }
    }

    private Brush ResourceBrush(string name) => TryFindResource(name) as Brush ?? Brushes.Gray;

    private void DrawText(DrawingContext dc, string text, Rect bounds, double size, string color, bool centered)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), size, ResourceBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, bounds.Width), MaxTextHeight = Math.Max(1, bounds.Height),
            Trimming = TextTrimming.CharacterEllipsis, TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left
        };
        dc.DrawText(formatted, new Point(bounds.X, bounds.Y + Math.Max(0, (bounds.Height - formatted.Height) / 2)));
    }
}

/// <summary>Small UI-only image cache. Images are decoded once and never keep a source file open.</summary>
internal sealed class WallpaperPreviewCache
{
    private readonly Dictionary<string, (DateTime Modified, ImageSource Image)> _images = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource? Load(string? path, int decodeWidth = 384)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (!File.Exists(path)) return null;
            var key = path + "|" + decodeWidth;
            var modified = File.GetLastWriteTimeUtc(path);
            if (_images.TryGetValue(key, out var existing) && existing.Modified == modified) return existing.Image;
            // Own the stream so a decoder failure cannot leave WPF's URI loader holding the file.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = decodeWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            if (decodeWidth > 640) return image;
            if (_images.Count >= 120) _images.Remove(_images.Keys.First());
            _images[key] = (modified, image);
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    public static IReadOnlyDictionary<string, string> ReadCurrentWallpapers(IEnumerable<MonitorIdentity> monitors)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IDesktopWallpaper? wallpaper = null;
        try
        {
            wallpaper = (IDesktopWallpaper)new DesktopWallpaper();
            foreach (var monitor in monitors)
            {
                if (string.IsNullOrWhiteSpace(monitor.MonitorDevicePath)) continue;
                try { result[monitor.MonitorDevicePath] = wallpaper.GetWallpaper(monitor.MonitorDevicePath); }
                catch (COMException) { }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException) { }
        finally { if (wallpaper is not null && Marshal.IsComObject(wallpaper)) Marshal.FinalReleaseComObject(wallpaper); }
        return result;
    }

    [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaper { }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        // The first vtable slot must be declared to reach the read-only second slot.
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
    }
}
