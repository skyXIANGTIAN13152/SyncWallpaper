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
    private readonly TopologyCoordinator _topology;
    private readonly SessionPowerStateMachine _powerState = new();
    private readonly NativeSystemMessageSource? _messageSource;
    private readonly Func<DisplaySnapshot, Task> _onStable;
    private readonly Action<string>? _onSystemEvent;
    private string _lastSignature = string.Empty;
    private DisplaySnapshot? _stableSnapshot;
    public string LastSystemEvent { get; private set; } = "No system event received yet";
    public long Generation => _topology.CurrentGeneration;
    public SessionPowerState PowerState => _powerState.Current;

    public DisplayChangeCoordinator(MonitorDiscoveryService discovery, Func<DisplaySnapshot, Task> onStable, Action<string>? onSystemEvent = null)
    {
        _discovery = discovery; _onStable = onStable; _onSystemEvent = onSystemEvent;
        _topology = new TopologyCoordinator(
            (_, _) =>
            {
                var snapshot = Interlocked.Exchange(ref _stableSnapshot, null) ?? new DisplaySnapshot { Monitors = _discovery.Discover().ToList() };
                return Task.FromResult((snapshot, true));
            },
            async (_, snapshot, token) =>
            {
                token.ThrowIfCancellationRequested();
                await _onStable(snapshot).ConfigureAwait(false);
                _powerState.TopologyStable();
            });
        _stabilizer = new DisplayTopologyStabilizer(
            () => new DisplaySnapshot { Monitors = _discovery.Discover().ToList() },
            (snapshot, token) =>
            {
                if (!token.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _stableSnapshot, snapshot);
                    _topology.Signal(TopologySignalKind.Display, "stable-display-topology");
                }
                return Task.CompletedTask;
            });
        _debouncer = new EventDebouncer(TimeSpan.FromMilliseconds(250), _ => { _stabilizer.Signal(); return Task.CompletedTask; });
        SystemEvents.DisplaySettingsChanged += OnSystemEvent;
        SystemEvents.PowerModeChanged += OnPowerEvent;
        SystemEvents.SessionSwitch += OnSessionEvent;
        try { _messageSource = new NativeSystemMessageSource(RecordNativeEvent, () => Signal()); }
        catch (Exception ex) { _onSystemEvent?.Invoke($"NativeMessageSourceUnavailable:{ex.Message}"); }
    }
    public void Signal(bool manual = false)
    {
        if (manual)
        {
            Interlocked.Exchange(ref _stableSnapshot, new DisplaySnapshot { Monitors = _discovery.Discover().ToList() });
            _topology.Signal(TopologySignalKind.Manual, "manual-display-refresh", manual: true);
            return;
        }
        _debouncer.Signal();
    }
    public void Start() => Signal();
    private void OnSystemEvent(object? s, EventArgs e) { RecordEvent("DisplaySettingsChanged"); Signal(); }
    private void OnPowerEvent(object? s, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) { _powerState.BeginSuspend(); _powerState.MarkSuspended(); }
        else if (e.Mode == PowerModes.Resume) _powerState.BeginResume();
        RecordEvent($"Power:{e.Mode}");
        Signal();
    }
    private void OnSessionEvent(object? s, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) _powerState.SessionUnavailable();
        else if (e.Reason == SessionSwitchReason.SessionUnlock) _powerState.BeginResume();
        RecordEvent($"Session:{e.Reason}");
        Signal();
    }
    private void RecordEvent(string value)
    {
        LastSystemEvent = $"{DateTime.Now:HH:mm:ss} {value}";
        _onSystemEvent?.Invoke(value);
    }
    private void RecordNativeEvent(string value) => RecordEvent(value);
    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnSystemEvent;
        SystemEvents.PowerModeChanged -= OnPowerEvent;
        SystemEvents.SessionSwitch -= OnSessionEvent;
        _messageSource?.Dispose();
        _debouncer.Dispose();
        _stabilizer.Dispose();
        try { _topology.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
    }
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
        // HwndSource creates a normal top-level window by default. Without
        // explicitly hiding it, Windows groups this 1x1 event receiver into
        // the taskbar and shows a misleading second SyncWallpaper thumbnail.
        HideFromTaskbar(_source.Handle);
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

    private static void HideFromTaskbar(IntPtr hwnd)
    {
        var extendedStyle = GetWindowLongPtr(hwnd, ExtendedStyleIndex).ToInt64();
        extendedStyle = (extendedStyle & ~AppWindowStyle) | ToolWindowStyle | NoActivateStyle;
        SetWindowLongPtr(hwnd, ExtendedStyleIndex, new IntPtr(extendedStyle));
        ShowWindow(hwnd, HideWindow);
    }

    private const int ExtendedStyleIndex = -20;
    private const long ToolWindowStyle = 0x00000080L;
    private const long AppWindowStyle = 0x00040000L;
    private const long NoActivateStyle = 0x08000000L;
    private const int HideWindow = 0;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);
}
