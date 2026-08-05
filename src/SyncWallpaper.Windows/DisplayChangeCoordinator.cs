using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class DisplayChangeCoordinator : IDisposable
{
    private readonly MonitorDiscoveryService _discovery;
    private readonly EventDebouncer _debouncer;
    private readonly DisplayTopologyStabilizer _stabilizer;
    private readonly NativeSystemMessageSource? _messageSource;
    private readonly Func<DisplaySnapshot, Task> _onStable;
    private readonly Action<string>? _onSystemEvent;
    private string _lastSignature = string.Empty;
    public string LastSystemEvent { get; private set; } = "尚未收到系统事件";

    public DisplayChangeCoordinator(MonitorDiscoveryService discovery, Func<DisplaySnapshot, Task> onStable, Action<string>? onSystemEvent = null)
    {
        _discovery = discovery; _onStable = onStable; _onSystemEvent = onSystemEvent;
        _stabilizer = new DisplayTopologyStabilizer(
            () => new DisplaySnapshot { Monitors = _discovery.Discover().ToList() },
            async (snapshot, token) =>
            {
                if (!token.IsCancellationRequested) await _onStable(snapshot);
            });
        _debouncer = new EventDebouncer(TimeSpan.FromMilliseconds(250), _ => { _stabilizer.Signal(); return Task.CompletedTask; });
        SystemEvents.DisplaySettingsChanged += OnSystemEvent;
        SystemEvents.PowerModeChanged += OnPowerEvent;
        SystemEvents.SessionSwitch += OnSessionEvent;
        try { _messageSource = new NativeSystemMessageSource(RecordNativeEvent, Signal); }
        catch (Exception ex) { _onSystemEvent?.Invoke($"NativeMessageSourceUnavailable:{ex.Message}"); }
    }
    public void Signal() => _debouncer.Signal();
    public void Start() => Signal();
    private void OnSystemEvent(object? s, EventArgs e) { RecordEvent("DisplaySettingsChanged"); Signal(); }
    private void OnPowerEvent(object? s, PowerModeChangedEventArgs e) { RecordEvent($"Power:{e.Mode}"); Signal(); }
    private void OnSessionEvent(object? s, SessionSwitchEventArgs e) { RecordEvent($"Session:{e.Reason}"); Signal(); }
    private void RecordEvent(string value)
    {
        LastSystemEvent = $"{DateTime.Now:HH:mm:ss} {value}";
        _onSystemEvent?.Invoke(value);
    }
    private void RecordNativeEvent(string value) => RecordEvent(value);
    public void Dispose() { SystemEvents.DisplaySettingsChanged -= OnSystemEvent; SystemEvents.PowerModeChanged -= OnPowerEvent; SystemEvents.SessionSwitch -= OnSessionEvent; _messageSource?.Dispose(); _debouncer.Dispose(); _stabilizer.Dispose(); }
}

/// <summary>Small message-only WPF window for event-driven topology signals.</summary>
internal sealed class NativeSystemMessageSource : IDisposable
{
    private readonly HwndSource _source;
    private readonly Action<string> _record;
    private readonly Action _signal;
    private readonly int _taskbarCreated;

    public NativeSystemMessageSource(Action<string> record, Action signal)
    {
        _record = record; _signal = signal;
        var parameters = new HwndSourceParameters("SyncWallpaper.DisplayWatcher") { Width = 1, Height = 1, PositionX = -32000, PositionY = -32000, UsesPerPixelOpacity = false };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var value = message switch
        {
            0x007E => "WM_DISPLAYCHANGE",
            0x0219 => "WM_DEVICECHANGE",
            0x0218 => "WM_POWERBROADCAST",
            0x02E0 => "WM_DPICHANGED",
            0x001A => "WM_SETTINGCHANGE",
            _ when message == _taskbarCreated => "TaskbarCreated",
            _ => string.Empty
        };
        if (value.Length > 0) { _record(value); _signal(); }
        return IntPtr.Zero;
    }

    public void Dispose() { _source.RemoveHook(WndProc); _source.Dispose(); }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);
}
