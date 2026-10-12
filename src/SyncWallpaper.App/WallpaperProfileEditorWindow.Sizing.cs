using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FormsScreen = System.Windows.Forms.Screen;

namespace SyncWallpaper.App;

public partial class WallpaperProfileEditorWindow
{
    private HwndSource? _windowSource;
    private bool _initialBoundsApplied;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowBoundsHook);

        var ownerHandle = Owner is null ? handle : new WindowInteropHelper(Owner).Handle;
        var workArea = FormsScreen.FromHandle(ownerHandle).WorkingArea;
        var scale = Math.Max(96u, GetDpiForWindow(ownerHandle)) / 96d;
        // Leave room around the normal window, including on high-DPI laptops.
        // The minimum must also fit when the available work area is very small.
        MinWidth = Math.Min(MinWidth, workArea.Width / scale);
        MinHeight = Math.Min(MinHeight, workArea.Height / scale);
        Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width / scale * 0.9));
        Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height / scale * 0.9));
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_initialBoundsApplied) return;
        _initialBoundsApplied = true;
        var handle = new WindowInteropHelper(this).Handle;
        var workArea = FormsScreen.FromHandle(handle).WorkingArea;
        if (!GetWindowRect(handle, out var bounds)) return;
        // CenterOwner can place an editor under the taskbar when its owner is
        // already oversized. Clamp the actual native rectangle, not DIP offsets.
        var left = Math.Clamp(bounds.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - (bounds.Right - bounds.Left)));
        var top = Math.Clamp(bounds.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - (bounds.Bottom - bounds.Top)));
        SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    protected override void OnClosed(EventArgs e)
    {
        _windowSource?.RemoveHook(WindowBoundsHook);
        _windowSource = null;
        base.OnClosed(e);
    }

    private IntPtr WindowBoundsHook(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo) return IntPtr.Zero;
        var screen = FormsScreen.FromHandle(handle);
        var workArea = screen.WorkingArea;
        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        // Borderless transparent windows otherwise maximize over the taskbar.
        // Query the current monitor each time so moving to another screen works.
        info.MaxPosition = new NativePoint { X = workArea.Left - screen.Bounds.Left, Y = workArea.Top - screen.Bounds.Top };
        info.MaxSize = new NativePoint { X = workArea.Width, Y = workArea.Height };
        Marshal.StructureToPtr(info, lParam, false);
        // Let WPF also update its layout cache and enforce MinWidth/MinHeight.
        // Its handler preserves the maximized position and size set above.
        return IntPtr.Zero;
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
