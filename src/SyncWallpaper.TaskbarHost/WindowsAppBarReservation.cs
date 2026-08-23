using System.ComponentModel;
using System.Runtime.InteropServices;
using SyncWallpaper.Core;

namespace SyncWallpaper.TaskbarHost;

internal sealed record AppBarReservationResult(bool Success, Int32Rect Bounds, string? Error = null);

/// <summary>
/// Documented Shell AppBar registration for a single secondary bar.  It never
/// injects into Explorer and always unregisters before its owning HWND closes.
/// </summary>
internal sealed class WindowsAppBarReservation : IDisposable
{
    private readonly uint _callbackMessage = RegisterWindowMessage("SyncWallpaper.TaskbarHost.AppBar.PositionChanged");
    private nint _window;
    private bool _registered;
    private bool _disposed;

    public uint CallbackMessage => _callbackMessage;
    public bool IsRegistered => _registered;

    public bool IsPositionChangedMessage(int message, nint wParam)
        => _registered && message == _callbackMessage && wParam.ToInt64() == AbnPosChanged;

    public AppBarReservationResult Reserve(nint window, Int32Rect monitorBounds, int height)
    {
        if (_disposed) return new(false, default, "AppBar 已释放。");
        if (window == 0 || _callbackMessage == 0) return new(false, default, "无法取得 AppBar 窗口或回调消息。");
        if (_window != 0 && _window != window) Remove();
        _window = window;
        if (!_registered)
        {
            var register = NewData();
            register.CallbackMessage = _callbackMessage;
            if (SHAppBarMessage(AbmNew, ref register) == 0)
                return new(false, default, "Windows 拒绝注册副屏 AppBar。" + LastErrorSuffix());
            _registered = true;
        }

        var data = NewData();
        data.Edge = AbeBottom;
        data.Rect = NativeRect.FromCore(monitorBounds);
        SHAppBarMessage(AbmQueryPos, ref data);
        data.Rect.Top = Math.Max(data.Rect.Top, data.Rect.Bottom - Math.Max(1, height));
        if (SHAppBarMessage(AbmSetPos, ref data) == 0)
        {
            var error = "Windows 拒绝设置副屏 AppBar 位置。" + LastErrorSuffix();
            Remove();
            return new(false, default, error);
        }
        return new(true, data.Rect.ToCore());
    }

    public void Remove()
    {
        if (!_registered || _window == 0) return;
        try
        {
            var data = NewData();
            SHAppBarMessage(AbmRemove, ref data);
        }
        finally
        {
            _registered = false;
            _window = 0;
        }
    }

    private AppBarData NewData() => new()
    {
        Size = (uint)Marshal.SizeOf<AppBarData>(),
        Window = _window
    };

    private static string LastErrorSuffix()
    {
        var error = Marshal.GetLastWin32Error();
        return error == 0 ? string.Empty : $"（{new Win32Exception(error).Message}）";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Remove();
    }

    private const uint AbmNew = 0x00000000;
    private const uint AbmRemove = 0x00000001;
    private const uint AbmQueryPos = 0x00000002;
    private const uint AbmSetPos = 0x00000003;
    private const uint AbeBottom = 3;
    private const long AbnPosChanged = 1;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern nuint SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public nint Window;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rect;
        public nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public static NativeRect FromCore(Int32Rect rect) => new()
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = checked(rect.Left + rect.Width),
            Bottom = checked(rect.Top + rect.Height)
        };

        public readonly Int32Rect ToCore()
            => new(Left, Top, Math.Max(1, Right - Left), Math.Max(1, Bottom - Top));
    }
}
