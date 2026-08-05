using System.Runtime.InteropServices;

namespace SyncWallpaper.WindowEngine;

public sealed class WindowsWindowEventSource : IDisposable
{
    private readonly WinEventDelegate _callback;
    private IntPtr _hook;
    public event EventHandler<WindowEvent>? EventReceived;
    public bool IsActive => _hook != IntPtr.Zero;

    public WindowsWindowEventSource()
    {
        _callback = OnEvent;
        _hook = SetWinEventHook(EventSystemMin, EventObjectLocationChange, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd != IntPtr.Zero) EventReceived?.Invoke(this, new WindowEvent(eventType, hwnd, idObject, idChild, time));
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    private const uint EventSystemMin = 0x00000001, EventObjectLocationChange = 0x800B, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 0x0002;
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint minEvent, uint maxEvent, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}

public sealed record WindowEvent(uint EventType, IntPtr WindowHandle, int ObjectId, int ChildId, uint Timestamp);
