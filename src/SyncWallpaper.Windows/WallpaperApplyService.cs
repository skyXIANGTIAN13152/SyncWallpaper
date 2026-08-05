using System;
using System.IO;
using System.Runtime.InteropServices;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class WallpaperApplyService
{
    private readonly WallpaperRenderService _renderer;
    private readonly Action<string> _log;
    public WallpaperApplyService(WallpaperRenderService renderer, Action<string>? log = null) { _renderer = renderer; _log = log ?? (_ => { }); }

    public async Task<ApplyResult> ApplyAsync(MatchResult match, IReadOnlyList<WallpaperAsset> assets, DataPaths paths, CancellationToken cancellationToken = default)
    {
        if (match.Status is MatchStatus.Ambiguous or MatchStatus.NoMatch || !match.CanAutoApply || match.Profile is null)
            return new ApplyResult(false, match.Message, 0);
        IDesktopWallpaper? desktop = null;
        var currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var changed = new List<string>();
        var applied = 0;
        var failed = false;
        var missing = 0;
        try
        {
            desktop = (IDesktopWallpaper)new DesktopWallpaper();
            var count = desktop.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                var path = desktop.GetMonitorDevicePathAt(i);
                if (string.IsNullOrWhiteSpace(path)) continue;
                currentPaths.Add(path);
                try { previous[path] = desktop.GetWallpaper(path) ?? string.Empty; }
                catch (COMException) { previous[path] = string.Empty; }
            }

            foreach (var role in match.Profile.Roles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!match.RoleMatches.TryGetValue(role.Role, out var monitor)) continue;
                var asset = assets.FirstOrDefault(a => string.Equals(a.Id, role.WallpaperAssetId, StringComparison.OrdinalIgnoreCase));
                var source = asset is null ? role.WallpaperPath : Resolve(asset, paths);
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) { missing++; _log($"壁纸文件不存在：{source}"); continue; }
                if (!IsSupportedImage(source)) { missing++; _log($"不支持的壁纸格式：{source}"); continue; }
                _log($"开始渲染并应用：{role.DisplayName} {monitor.Width}x{monitor.Height}");
                var hash = asset?.Sha256 ?? FileUtilities.Sha256(source);
                var rendered = _renderer.Render(source, hash, Math.Max(1, monitor.Width), Math.Max(1, monitor.Height), role.FitMode, role.BackgroundColor);
                _log($"渲染完成：{Path.GetFileName(rendered)}");
                if (!currentPaths.Contains(monitor.MonitorDevicePath)) { missing++; _log($"当前路径未出现在 IDesktopWallpaper：{monitor.MonitorDevicePath}"); continue; }
                if (role.FitMode == WallpaperFitMode.Span) desktop.SetPosition(DesktopWallpaperPosition.Span);
                if (previous.TryGetValue(monitor.MonitorDevicePath, out var existing) && string.Equals(existing, rendered, StringComparison.OrdinalIgnoreCase)) { applied++; continue; }
                var success = false;
                for (var attempt = 0; attempt < 3 && !success; attempt++)
                {
                    try
                    {
                        desktop.SetWallpaper(monitor.MonitorDevicePath, rendered);
                        await Task.Delay(120 * (attempt + 1), cancellationToken);
                        var actual = desktop.GetWallpaper(monitor.MonitorDevicePath);
                        success = string.Equals(actual, rendered, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (COMException ex) when (attempt < 2) { _log($"Explorer 壁纸接口暂不可用，重试 {attempt + 1}/2：{ex.Message}"); }
                }
                if (success) { applied++; changed.Add(monitor.MonitorDevicePath); _log($"壁纸验证成功：{monitor.DisplayLabel}"); }
                else { failed = true; _log($"壁纸验证失败：{monitor.DisplayLabel}，开始回滚"); break; }
            }
            if (failed)
            {
                foreach (var path in changed.AsEnumerable().Reverse())
                {
                    if (!previous.TryGetValue(path, out var old) || string.IsNullOrWhiteSpace(old)) continue;
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        try { desktop.SetWallpaper(path, old); if (string.Equals(desktop.GetWallpaper(path), old, StringComparison.OrdinalIgnoreCase)) break; }
                        catch (COMException) when (attempt < 2) { await Task.Delay(120 * (attempt + 1), cancellationToken); }
                    }
                }
                _log($"壁纸事务已回滚：{changed.Count} 台");
            }
        }
        catch (COMException ex) { _log($"壁纸 COM 接口不可用：{ex.Message}"); return new ApplyResult(false, "Explorer 壁纸接口暂不可用，保留当前壁纸", applied); }
        finally { if (desktop is not null && Marshal.IsComObject(desktop)) Marshal.ReleaseComObject(desktop); }
        var successResult = !failed && missing == 0 && applied == match.Profile.Roles.Count;
        return new ApplyResult(successResult, successResult ? "壁纸事务应用并验证成功" : $"已应用 {applied}/{match.Profile.Roles.Count} 台；缺失 {missing} 台", applied);
    }

    private static string Resolve(WallpaperAsset asset, DataPaths paths)
        => asset.StorageMode.Equals("External", StringComparison.OrdinalIgnoreCase) ? asset.ExternalPath ?? string.Empty : Path.Combine(paths.Root, asset.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static bool IsSupportedImage(string path)
        => new[] { ".jpg", ".jpeg", ".png", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")] private class DesktopWallpaper { }
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(DesktopWallpaperPosition position);
        DesktopWallpaperPosition GetPosition();
        void SetSlideshow(IntPtr slideshow);
        IntPtr GetSlideshow();
        void SetSlideshowOptions(DesktopSlideshowOptions options, uint slideshowTick);
        void GetSlideshowOptions(out DesktopSlideshowOptions options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, DesktopSlideshowDirection direction);
        DesktopSlideshowState GetStatus();
        bool Enable();
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    private enum DesktopWallpaperPosition { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }
    [Flags] private enum DesktopSlideshowOptions { None = 0, ShuffleImages = 1 }
    private enum DesktopSlideshowDirection { Forward = 0, Backward = 1 }
    [Flags] private enum DesktopSlideshowState { None = 0, Enabled = 1, Slideshow = 2, DisabledByRemoteSession = 4 }
}

public sealed record ApplyResult(bool Success, string Message, int AppliedCount);
