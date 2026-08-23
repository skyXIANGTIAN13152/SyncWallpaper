using System.Runtime.InteropServices;
using SyncWallpaper.Core;

namespace SyncWallpaper.TaskbarHost;

internal sealed record MonitorPlacement(
    Int32Rect LogicalMonitorArea,
    Int32Rect LogicalWorkArea,
    Int32Rect NativeMonitorArea,
    Int32Rect NativeWorkArea,
    double ScaleX,
    double ScaleY)
{
    public double NativeScale => Math.Max(ScaleX, ScaleY);

    public Int32Rect ToLogical(Int32Rect native)
    {
        var left = LogicalMonitorArea.Left + (int)Math.Round((native.Left - NativeMonitorArea.Left) / ScaleX);
        var top = LogicalMonitorArea.Top + (int)Math.Round((native.Top - NativeMonitorArea.Top) / ScaleY);
        var width = Math.Max(1, (int)Math.Round(native.Width / ScaleX));
        var height = Math.Max(1, (int)Math.Round(native.Height / ScaleY));
        return new Int32Rect(left, top, width, height);
    }
}

internal static class WindowsMonitorPlacement
{
    public static MonitorPlacement Resolve(TaskbarMonitor monitor)
    {
        var entries = Enumerate();
        var native = entries.FirstOrDefault(x =>
            string.Equals(x.DeviceName, monitor.WindowsDisplayName, StringComparison.OrdinalIgnoreCase));
        if (native is null || monitor.Bounds.Width <= 0 || monitor.Bounds.Height <= 0)
            return new MonitorPlacement(monitor.Bounds, monitor.Bounds, monitor.Bounds, monitor.Bounds, 1d, 1d);

        var scaleX = native.Monitor.Width / (double)monitor.Bounds.Width;
        var scaleY = native.Monitor.Height / (double)monitor.Bounds.Height;
        if (!double.IsFinite(scaleX) || scaleX <= 0) scaleX = 1;
        if (!double.IsFinite(scaleY) || scaleY <= 0) scaleY = scaleX;

        var logicalLeftInset = (int)Math.Round((native.Work.Left - native.Monitor.Left) / scaleX);
        var logicalTopInset = (int)Math.Round((native.Work.Top - native.Monitor.Top) / scaleY);
        var logicalRightInset = (int)Math.Round((native.Monitor.Right - native.Work.Right) / scaleX);
        var logicalBottomInset = (int)Math.Round((native.Monitor.Bottom - native.Work.Bottom) / scaleY);
        var logicalWork = new Int32Rect(
            monitor.Bounds.Left + logicalLeftInset,
            monitor.Bounds.Top + logicalTopInset,
            Math.Max(1, monitor.Bounds.Width - logicalLeftInset - logicalRightInset),
            Math.Max(1, monitor.Bounds.Height - logicalTopInset - logicalBottomInset));
        return new MonitorPlacement(
            monitor.Bounds,
            logicalWork,
            native.Monitor.ToCore(),
            native.Work.ToCore(),
            scaleX,
            scaleY);
    }

    private static IReadOnlyList<NativeMonitor> Enumerate()
    {
        var result = new List<NativeMonitor>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
            if (GetMonitorInfo(monitor, ref info))
                result.Add(new NativeMonitor(info.DeviceName ?? string.Empty, info.Monitor, info.Work));
            return true;
        }, 0);
        return result;
    }

    private sealed record NativeMonitor(string DeviceName, NativeRect Monitor, NativeRect Work);
    private delegate bool MonitorEnumProc(nint monitor, nint deviceContext, nint rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint deviceContext, nint clipRect, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public Int32Rect ToCore() => new(Left, Top, Width, Height);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }
}
