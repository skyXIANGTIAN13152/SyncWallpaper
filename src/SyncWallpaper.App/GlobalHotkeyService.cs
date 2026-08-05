using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using SyncWallpaper.Core;

namespace SyncWallpaper.App;

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly Dictionary<int, Action> _actions = new();
    private HwndSource? _source;
    private int _nextId = 1000;
    public void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            _source = (HwndSource)PresentationSource.FromVisual(window)!;
            _source.AddHook(WndProc);
        };
    }
    public bool Register(GlobalHotkeyDefinition definition)
    {
        if (_source is null || definition.VirtualKey == 0) return false;
        var id = _nextId++; var ok = RegisterHotKey(_source.Handle, id, definition.Modifiers, definition.VirtualKey);
        if (ok) _actions[id] = () => { }; return ok;
    }
    public bool Register(uint modifiers, Key key, Action action)
    {
        if (_source is null) return false;
        var id = _nextId++; var vk = KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(_source.Handle, id, modifiers, (uint)vk)) return false;
        _actions[id] = action; return true;
    }
    public void Dispose()
    {
        if (_source is not null) foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action)) { action(); handled = true; }
        return IntPtr.Zero;
    }
    private const int WM_HOTKEY = 0x0312;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
