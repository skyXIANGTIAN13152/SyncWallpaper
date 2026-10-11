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
    private readonly object _cacheGate = new();
    private HashSet<string> _activeCachePaths = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _transactionCachePaths;
    private bool _canTrim = true;
    public long MaxCacheBytes { get; private set; } = 512L * 1024 * 1024;
    public WallpaperRenderService(DataPaths paths) { _paths = paths; _paths.Ensure(); }

    public void ConfigureCacheLimit(long bytes, bool trimImmediately = true)
    {
        lock (_cacheGate)
        {
            MaxCacheBytes = Math.Max(1, bytes);
            if (trimImmediately) TrimCache();
        }
    }

    internal void UpdateActiveCacheProtection(IEnumerable<string> currentWallpapers, bool completeSnapshot)
    {
        lock (_cacheGate)
        {
            var current = NormalizePaths(currentWallpapers);
            if (completeSnapshot) _activeCachePaths = current;
            else _activeCachePaths.UnionWith(current);
            _canTrim = completeSnapshot;
        }
    }

    internal void BeginCacheProtection(IEnumerable<string> currentWallpapers, bool completeSnapshot)
    {
        lock (_cacheGate)
        {
            if (_transactionCachePaths is not null) throw new InvalidOperationException("已有壁纸缓存事务正在进行。");
            UpdateActiveCacheProtection(currentWallpapers, completeSnapshot);
            _transactionCachePaths = new HashSet<string>(_activeCachePaths, StringComparer.OrdinalIgnoreCase);
        }
    }

    internal void EndCacheProtection(IEnumerable<string> currentWallpapers, bool completeSnapshot)
    {
        lock (_cacheGate)
        {
            UpdateActiveCacheProtection(currentWallpapers, completeSnapshot);
            _transactionCachePaths = null;
            TrimCache();
        }
    }

    private static HashSet<string> NormalizePaths(IEnumerable<string> paths)
        => paths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public string Render(string sourcePath, string sourceHash, int width, int height, WallpaperFitMode mode, string background)
    {
        width = Math.Max(1, width); height = Math.Max(1, height);
        var key = WallpaperCacheKey.Create(sourceHash, width, height, mode, background);
        var output = Path.Combine(_paths.Rendered, key + ".png");
        lock (_cacheGate) _transactionCachePaths?.Add(Path.GetFullPath(output));
        if (File.Exists(output))
        {
            try { File.SetLastWriteTimeUtc(output, DateTime.UtcNow); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return output;
        }
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
        var temp = output + ".tmp";
        using (var file = File.Create(temp))
        {
            encoder.Save(file);
            file.Flush(true);
        }
        File.Move(temp, output, true);
        TrimCache();
        return output;
    }

    public void TrimCache()
    {
        lock (_cacheGate)
        try
        {
            if (!_canTrim) return;
            var files = new DirectoryInfo(_paths.Rendered).EnumerateFiles("*.png")
                .OrderByDescending(x => x.LastWriteTimeUtc).ToList();
            var protectedFiles = files.Where(file => _activeCachePaths.Contains(file.FullName)
                || _transactionCachePaths?.Contains(file.FullName) == true).ToHashSet();
            // Active images are indispensable even when they exceed the limit.
            // Only the remaining budget may retain ordinary LRU cache entries.
            long total = protectedFiles.Sum(file => file.Length);
            foreach (var file in files.Where(file => !protectedFiles.Contains(file)))
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
