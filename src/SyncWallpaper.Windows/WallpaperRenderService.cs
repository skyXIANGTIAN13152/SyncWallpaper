using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class WallpaperRenderService
{
    private readonly DataPaths _paths;
    public long MaxCacheBytes { get; init; } = 512L * 1024 * 1024;
    public WallpaperRenderService(DataPaths paths) { _paths = paths; _paths.Ensure(); }

    public string Render(string sourcePath, string sourceHash, int width, int height, WallpaperFitMode mode, string background)
    {
        width = Math.Max(1, width); height = Math.Max(1, height);
        var key = WallpaperCacheKey.Create(sourceHash, width, height, mode, background);
        var output = Path.Combine(_paths.Rendered, key + ".png");
        if (File.Exists(output)) return output;
        Directory.CreateDirectory(_paths.Rendered);

        var source = new BitmapImage();
        using (var stream = File.OpenRead(sourcePath))
        {
            source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.StreamSource = stream; source.EndInit(); source.Freeze();
        }
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(background) ? "#050B18" : background);
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
            var sw = source.PixelWidth; var sh = source.PixelHeight;
            if (mode == WallpaperFitMode.Tile)
            {
                for (var y = 0.0; y < height; y += sh)
                    for (var x = 0.0; x < width; x += sw) dc.DrawImage(source, new Rect(x, y, sw, sh));
            }
            else
            {
                var scale = mode switch
                {
                    WallpaperFitMode.Stretch => new System.Windows.Size((double)width / sw, (double)height / sh),
                    WallpaperFitMode.Fit => new System.Windows.Size(Math.Min((double)width / sw, (double)height / sh), Math.Min((double)width / sw, (double)height / sh)),
                    WallpaperFitMode.Center => new System.Windows.Size(1, 1),
                    WallpaperFitMode.Span => new System.Windows.Size(Math.Max((double)width / sw, (double)height / sh), Math.Max((double)width / sw, (double)height / sh)),
                    _ => new System.Windows.Size(Math.Max((double)width / sw, (double)height / sh), Math.Max((double)width / sw, (double)height / sh))
                };
                var dw = sw * scale.Width; var dh = sh * scale.Height;
                dc.DrawImage(source, new Rect((width - dw) / 2, (height - dh) / 2, dw, dh));
            }
        }
        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual); rendered.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var file = File.Create(output); encoder.Save(file);
        TrimCache();
        return output;
    }

    private void TrimCache()
    {
        try
        {
            var files = new DirectoryInfo(_paths.Rendered).EnumerateFiles("*.png")
                .OrderByDescending(x => x.LastWriteTimeUtc).ToList();
            long total = 0;
            foreach (var file in files)
            {
                total += file.Length;
                if (total <= MaxCacheBytes) continue;
                try { file.Delete(); } catch (IOException ex) { System.Diagnostics.Debug.WriteLine($"Wallpaper cache cleanup skipped: {ex.Message}"); }
                catch (UnauthorizedAccessException ex) { System.Diagnostics.Debug.WriteLine($"Wallpaper cache cleanup denied: {ex.Message}"); }
            }
        }
        catch (IOException ex) { System.Diagnostics.Debug.WriteLine($"Wallpaper cache enumeration skipped: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { System.Diagnostics.Debug.WriteLine($"Wallpaper cache enumeration denied: {ex.Message}"); }
    }
}
