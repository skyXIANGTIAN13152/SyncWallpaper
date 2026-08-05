using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class WindowManagementService
{
    public IReadOnlyList<WindowPlacement> Capture(IReadOnlyList<MonitorIdentity> monitors)
    {
        var result = new List<WindowPlacement>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return true;
            GetWindowRect(hWnd, out var rect); if (rect.Right - rect.Left < 80 || rect.Bottom - rect.Top < 40) return true;
            var path = ProcessPath(hWnd); var title = WindowText(hWnd); var klass = WindowClass(hWnd);
            var monitor = monitors.FirstOrDefault(m => Intersects(m, rect));
            if (monitor is not null) result.Add(new WindowPlacement { ProcessPath = path, WindowClass = klass, TitlePattern = title, MonitorDevicePath = monitor.MonitorDevicePath, Left = rect.Left, Top = rect.Top, Width = rect.Right - rect.Left, Height = rect.Bottom - rect.Top, Maximize = IsZoomed(hWnd) });
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public int Apply(WindowPositionProfile profile, IReadOnlyList<MonitorIdentity> monitors)
    {
        var applied = 0;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var title = WindowText(hWnd); var klass = WindowClass(hWnd); var path = ProcessPath(hWnd);
            var placement = profile.Windows.FirstOrDefault(p => Match(p, path, klass, title));
            if (placement is null) return true;
            var monitor = monitors.FirstOrDefault(m => m.MonitorDevicePath.Equals(placement.MonitorDevicePath, StringComparison.OrdinalIgnoreCase));
            if (monitor is null) return true;
            SetWindowPos(hWnd, IntPtr.Zero, placement.Left, placement.Top, placement.Width, placement.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            if (placement.Maximize) ShowWindow(hWnd, SW_MAXIMIZE); applied++; return true;
        }, IntPtr.Zero);
        return applied;
    }

    public bool MoveToMonitor(IntPtr hWnd, MonitorIdentity monitor, bool maximize = false)
    {
        if (!GetWindowRect(hWnd, out var rect)) return false;
        var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        var left = monitor.DesktopX + Math.Max(0, (monitor.Width - width) / 2);
        var top = monitor.DesktopY + Math.Max(0, (monitor.Height - height) / 2);
        if (!SetWindowPos(hWnd, IntPtr.Zero, left, top, width, height, SWP_NOZORDER | SWP_NOACTIVATE)) return false;
        if (maximize) ShowWindow(hWnd, SW_MAXIMIZE); return true;
    }

    private static bool Match(WindowPlacement p, string path, string klass, string title)
        => (string.IsNullOrWhiteSpace(p.ProcessPath) || path.Equals(p.ProcessPath, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(p.WindowClass) || klass.Equals(p.WindowClass, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(p.TitlePattern) || title.Contains(p.TitlePattern, StringComparison.OrdinalIgnoreCase));
    private static bool Intersects(MonitorIdentity m, RECT r) => r.Left < m.DesktopX + m.Width && r.Right > m.DesktopX && r.Top < m.DesktopY + m.Height && r.Bottom > m.DesktopY;
    private static string WindowText(IntPtr h) { var b = new StringBuilder(512); GetWindowText(h, b, b.Capacity); return b.ToString(); }
    private static string WindowClass(IntPtr h) { var b = new StringBuilder(256); GetClassName(h, b, b.Capacity); return b.ToString(); }
    private static string ProcessPath(IntPtr h)
    {
        GetWindowThreadProcessId(h, out var id); try { return Process.GetProcessById((int)id).MainModule?.FileName ?? string.Empty; } catch { return string.Empty; }
    }
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010; private const int SW_MAXIMIZE = 3;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
}
