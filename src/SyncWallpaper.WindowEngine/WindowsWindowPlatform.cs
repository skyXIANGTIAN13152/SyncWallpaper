using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SyncWallpaper.Core;

namespace SyncWallpaper.WindowEngine;

public sealed class WindowsWindowPlatform : IWindowPlatform, IWindowZonePlatform, IDisposable
{
    private readonly Func<IReadOnlyList<MonitorIdentity>> _monitors;
    private readonly string _ownProcessPath = Environment.ProcessPath ?? string.Empty;

    public WindowsWindowPlatform(Func<IReadOnlyList<MonitorIdentity>> monitors) => _monitors = monitors;

    public IReadOnlyList<WindowPositionSnapshot> Enumerate()
    {
        var result = new List<WindowPositionSnapshot>();
        EnumWindows((hWnd, _) =>
        {
            var snapshot = TryCreateSnapshot(hWnd, excludeOwnProcess: true);
            if (snapshot is not null) result.Add(snapshot);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public WindowPositionSnapshot? TryGetWindow(IntPtr handle) => TryCreateSnapshot(handle, excludeOwnProcess: false);

    public Int32Point? GetCursorPosition() => GetCursorPos(out var point) ? new Int32Point(point.X, point.Y) : null;

    public bool IsShiftPressed() => (GetAsyncKeyState(VkShift) & 0x8000) != 0;

    public bool TrySetPosition(WindowPositionSnapshot window, Int32Rect physicalBounds, bool maximize)
    {
        if (!IsWindow(window.Handle)) return false;
        if (IsZoomed(window.Handle) || IsIconic(window.Handle)) ShowWindow(window.Handle, SwRestore);
        var outerBounds = CompensateForInvisibleFrame(window.Handle, physicalBounds);
        var ok = SetWindowPos(window.Handle, IntPtr.Zero, outerBounds.Left, outerBounds.Top, outerBounds.Width, outerBounds.Height, SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
        if (!ok) return false;
        if (maximize) ShowWindow(window.Handle, SwMaximize);
        return true;
    }

    public bool TryStartApplication(string executablePath, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = executablePath, Arguments = arguments ?? string.Empty, UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private MonitorIdentity? FindMonitor(RECT rect)
    {
        var current = _monitors();
        return current.FirstOrDefault(m => rect.Left < m.DesktopX + m.Width && rect.Right > m.DesktopX &&
            rect.Top < m.DesktopY + m.Height && rect.Bottom > m.DesktopY);
    }

    private WindowPositionSnapshot? TryCreateSnapshot(IntPtr hWnd, bool excludeOwnProcess)
    {
        if (!IsWindow(hWnd) || !IsWindowVisible(hWnd) || IsWindowCloaked(hWnd)) return null;
        if (!GetWindowRect(hWnd, out var rect) || rect.Right - rect.Left < 80 || rect.Bottom - rect.Top < 40) return null;
        var title = WindowText(hWnd);
        var className = WindowClass(hWnd);
        var path = ProcessPath(hWnd);
        if (string.IsNullOrWhiteSpace(title) || (excludeOwnProcess && string.Equals(path, _ownProcessPath, StringComparison.OrdinalIgnoreCase))) return null;
        var bounds = DwmBounds(hWnd, rect);
        var monitor = FindMonitor(bounds);
        if (monitor is null) return null;
        var placement = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        GetWindowPlacement(hWnd, ref placement);
        return new WindowPositionSnapshot
        {
            Handle = hWnd,
            Identity = new WindowIdentity
            {
                ExecutablePath = path,
                ProcessName = Path.GetFileNameWithoutExtension(path),
                WindowClass = className,
                WindowTitle = title,
                AppUserModelId = ReadAppUserModelId(hWnd),
                IsUwp = string.IsNullOrWhiteSpace(path),
                IsElevated = IsElevated(hWnd)
            },
            MonitorDevicePath = monitor.MonitorDevicePath,
            PhysicalBounds = new Int32Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top),
            Dpi = (int)Math.Max(96, GetDpiForWindow(hWnd)),
            ShowState = (int)placement.showCmd,
            IsMaximized = IsZoomed(hWnd),
            IsMinimized = IsIconic(hWnd)
        };
    }

    private static Int32Rect CompensateForInvisibleFrame(IntPtr hWnd, Int32Rect desiredVisibleBounds)
    {
        if (!GetWindowRect(hWnd, out var outer)) return desiredVisibleBounds;
        var visible = DwmBounds(hWnd, outer);
        var leftInset = Math.Max(0, visible.Left - outer.Left);
        var topInset = Math.Max(0, visible.Top - outer.Top);
        var rightInset = Math.Max(0, outer.Right - visible.Right);
        var bottomInset = Math.Max(0, outer.Bottom - visible.Bottom);
        return new Int32Rect(
            desiredVisibleBounds.Left - leftInset,
            desiredVisibleBounds.Top - topInset,
            desiredVisibleBounds.Width + leftInset + rightInset,
            desiredVisibleBounds.Height + topInset + bottomInset);
    }

    private static RECT DwmBounds(IntPtr hWnd, RECT fallback)
    {
        if (DwmGetWindowAttribute(hWnd, 9, out RECT rect, Marshal.SizeOf<RECT>()) == 0) return rect;
        return fallback;
    }

    private static string WindowText(IntPtr hWnd) { var b = new StringBuilder(1024); GetWindowText(hWnd, b, b.Capacity); return b.ToString(); }
    private static string WindowClass(IntPtr hWnd) { var b = new StringBuilder(256); GetClassName(hWnd, b, b.Capacity); return b.ToString(); }
    private static string ProcessPath(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out var pid);
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return string.Empty;
        try
        {
            var buffer = new StringBuilder(4096); var length = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref length) ? buffer.ToString() : string.Empty;
        }
        finally { CloseHandle(handle); }
    }

    private static bool IsElevated(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out var pid);
        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token)) return false;
            try
            {
                var elevation = new TOKEN_ELEVATION();
                var size = Marshal.SizeOf<TOKEN_ELEVATION>();
                return GetTokenInformation(token, TokenInformationClass.TokenElevation, ref elevation, size, out _) && elevation.TokenIsElevated != 0;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    private static string ReadAppUserModelId(IntPtr hWnd) => string.Empty;
    public void Dispose() { }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010, SwpNoOwnerZOrder = 0x0200;
    private const int SwMaximize = 3, SwRestore = 9, VkShift = 0x10;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, TokenInformationClass classId, ref TOKEN_ELEVATION elevation, int size, out int returnLength);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowAttributeInt(IntPtr hWnd, int attribute, out int value, int size);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct WINDOWPLACEMENT { public uint length, flags, showCmd; public POINT minPosition, maxPosition; public RECT normalPosition; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct TOKEN_ELEVATION { public int TokenIsElevated; }
    private enum TokenInformationClass { TokenElevation = 20 }
    private enum DwmWindowAttribute { Cloaked = 14 }
    private static bool IsWindowCloaked(IntPtr hWnd) => DwmGetWindowAttributeInt(hWnd, (int)DwmWindowAttribute.Cloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
}
