using System.Windows;
using System.Windows.Media;
using SyncWallpaper.Core;

namespace SyncWallpaper.App;

public sealed class MonitorFadingService : IDisposable
{
    private readonly List<Window> _overlays = new();
    public void Apply(IReadOnlyList<MonitorIdentity> monitors, MonitorIdentity? active, MonitorFadingSettings settings)
    {
        Clear(); if (!settings.Enabled) return;
        foreach (var monitor in monitors)
        {
            if (active is not null && monitor.MonitorDevicePath.Equals(active.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)) continue;
            var window = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, Left = monitor.DesktopX, Top = monitor.DesktopY, Width = monitor.Width, Height = monitor.Height, AllowsTransparency = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(255 * (1 - settings.InactiveOpacity)), 0, 0, 0)), ShowActivated = false };
            window.Show(); _overlays.Add(window);
        }
    }
    public void Clear() { foreach (var window in _overlays) window.Close(); _overlays.Clear(); }
    public void Dispose() => Clear();
}
