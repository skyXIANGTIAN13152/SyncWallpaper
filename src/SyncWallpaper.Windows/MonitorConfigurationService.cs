using System.Runtime.InteropServices;
using System.Windows.Forms;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class MonitorConfigurationService
{
    public MonitorProfile CaptureProfile(string name, IReadOnlyList<MonitorIdentity> monitors)
    {
        var profile = new MonitorProfile { Name = name };
        foreach (var monitor in monitors)
            profile.Monitors.Add(new MonitorProfileEntry
            {
                Fingerprint = monitor.Clone(), Width = monitor.Width, Height = monitor.Height,
                Rotation = monitor.Rotation, DesktopX = monitor.DesktopX, DesktopY = monitor.DesktopY, IsPrimary = monitor.IsPrimary
            });
        return profile;
    }

    public IReadOnlyList<DisplayMode> EnumerateModes(string deviceName)
    {
        var result = new List<DisplayMode>();
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        for (var i = 0; EnumDisplaySettings(deviceName, i, ref mode); i++)
        {
            result.Add(new DisplayMode(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency, mode.dmDisplayOrientation));
            mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        }
        return result.Distinct().ToList();
    }

    /// <summary>Applies only entries that match a saved stable device path; an unmatched entry is never guessed.</summary>
    public bool TryApply(MonitorProfile profile, IReadOnlyList<MonitorIdentity> current)
    {
        var screens = Screen.AllScreens;
        var changed = false;
        foreach (var entry in profile.Monitors)
        {
            var monitor = current.FirstOrDefault(m => string.Equals(m.MonitorDevicePath, entry.Fingerprint.MonitorDevicePath, StringComparison.OrdinalIgnoreCase));
            if (monitor is null) continue;
            var screen = screens.FirstOrDefault(s => s.Bounds.Left == monitor.DesktopX && s.Bounds.Top == monitor.DesktopY);
            if (screen is null) continue;
            var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(screen.DeviceName, -1, ref mode)) continue;
            mode.dmPelsWidth = entry.Width; mode.dmPelsHeight = entry.Height; mode.dmDisplayOrientation = (short)Math.Clamp(entry.Rotation - 1, 0, 3);
            mode.dmPositionX = entry.DesktopX; mode.dmPositionY = entry.DesktopY; mode.dmFields = (int)(DM.PelsWidth | DM.PelsHeight | DM.Position | DM.DisplayOrientation);
            var result = ChangeDisplaySettingsEx(screen.DeviceName, ref mode, IntPtr.Zero, CDS_NORESET | CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (result == DISP_CHANGE_SUCCESSFUL) changed = true;
        }
        if (changed) ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        return changed;
    }

    public IReadOnlyList<SplitRegion> ResolveSplits(MonitorProfile profile, IReadOnlyList<MonitorIdentity> current)
    {
        var regions = new List<SplitRegion>();
        foreach (var split in profile.Splits.Where(s => s.Enabled()))
        {
            var monitor = current.FirstOrDefault(m => m.MonitorDevicePath.Equals(split.MonitorDevicePath, StringComparison.OrdinalIgnoreCase));
            if (monitor is null) continue;
            regions.Add(new SplitRegion(split.Id, split.MonitorDevicePath,
                new Int32Rect(monitor.DesktopX + split.Left + split.PaddingLeft, monitor.DesktopY + split.Top + split.PaddingTop,
                    Math.Max(0, split.Width - split.PaddingLeft - split.PaddingRight), Math.Max(0, split.Height - split.PaddingTop - split.PaddingBottom))));
        }
        return regions;
    }

    private const int ENUM_CURRENT_SETTINGS = -1, CDS_NORESET = 0x10000000, CDS_UPDATEREGISTRY = 0x00000001, DISP_CHANGE_SUCCESSFUL = 0;
    private const int DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_POSITION = 0x20, DM_DISPLAYORIENTATION = 0x80, DM_DISPLAYFREQUENCY = 0x400000;
    [Flags] private enum DM { Position = DM_POSITION, PelsWidth = DM_PELSWIDTH, PelsHeight = DM_PELSHEIGHT, DisplayOrientation = DM_DISPLAYORIENTATION, DisplayFrequency = DM_DISPLAYFREQUENCY }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string? deviceName, IntPtr devMode, IntPtr hwnd, int flags, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
        public int dmPositionX, dmPositionY; public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
}

public sealed record DisplayMode(int Width, int Height, int RefreshRate, int Orientation);
public sealed record SplitRegion(string Id, string MonitorDevicePath, Int32Rect Bounds);
public readonly record struct Int32Rect(int Left, int Top, int Width, int Height);
file static class MonitorSplitExtensions
{
    public static bool Enabled(this MonitorSplit split) => split.Width > 0 && split.Height > 0;
}
