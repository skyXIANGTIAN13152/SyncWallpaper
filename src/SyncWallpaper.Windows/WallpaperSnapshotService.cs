using System.IO;
using System.Runtime.InteropServices;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

/// <summary>
/// Read-only capture of the wallpaper paths currently reported by
/// IDesktopWallpaper. Exported paths and monitor identifiers are redacted;
/// the service never changes the desktop.
/// </summary>
public sealed class WallpaperSnapshotService
{
    public WallpaperStateSnapshot Capture()
    {
        var capturedAt = DateTime.UtcNow;
        var monitors = new List<WallpaperMonitorSnapshot>();
        var activePaths = new MonitorDiscoveryService().Discover()
            .Select(x => x.MonitorDevicePath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IDesktopWallpaper? desktop = null;
        string? error = null;
        try
        {
            desktop = (IDesktopWallpaper)new DesktopWallpaper();
            var count = desktop.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                var path = desktop.GetMonitorDevicePathAt(i);
                if (string.IsNullOrWhiteSpace(path)) continue;
                var wallpaper = desktop.GetWallpaper(path) ?? string.Empty;
                var exists = !string.IsNullOrWhiteSpace(wallpaper) && File.Exists(wallpaper);
                var hash = exists ? TryHash(wallpaper) : string.Empty;
                monitors.Add(new WallpaperMonitorSnapshot(
                    MonitorIdentitySanitizer.RedactPath(path),
                    MonitorIdentitySanitizer.RedactPath(wallpaper),
                    hash,
                    exists,
                    activePaths.Contains(path)));
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ExternalException)
        {
            error = ex.Message;
        }
        finally
        {
            if (desktop is not null && Marshal.IsComObject(desktop)) Marshal.ReleaseComObject(desktop);
        }

        return new WallpaperStateSnapshot(capturedAt, monitors, error, false);
    }

    private static string TryHash(string path)
    {
        try { return FileUtilities.Sha256(path); }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

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

public sealed record WallpaperMonitorSnapshot(
    string MonitorDevicePath,
    string WallpaperPath,
    string WallpaperSha256,
    bool FileExists,
    bool IsActive);

public sealed record WallpaperStateSnapshot(
    DateTime CapturedAtUtc,
    IReadOnlyList<WallpaperMonitorSnapshot> Monitors,
    string? Error,
    bool SystemMutation)
{
    public int ActiveMonitorCount => Monitors.Count(x => x.IsActive);
}
