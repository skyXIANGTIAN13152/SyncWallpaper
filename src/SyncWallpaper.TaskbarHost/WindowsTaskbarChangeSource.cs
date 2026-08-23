using Microsoft.Win32;
using SyncWallpaper.WindowEngine;

namespace SyncWallpaper.TaskbarHost;

public sealed class WindowsTaskbarChangeSource : ITaskbarChangeSource
{
    private readonly IWindowEventSource _windowEvents;
    private bool _disposed;

    public WindowsTaskbarChangeSource(IWindowEventSource? windowEvents = null)
    {
        _windowEvents = windowEvents ?? new WindowsWindowEventSource();
        _windowEvents.EventReceived += WindowEvents_EventReceived;
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    public event EventHandler? Changed;
    public bool IsActive => !_disposed && _windowEvents.IsActive;

    private void WindowEvents_EventReceived(object? sender, WindowEvent e) => Changed?.Invoke(this, EventArgs.Empty);
    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General or UserPreferenceCategory.Window)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _windowEvents.EventReceived -= WindowEvents_EventReceived;
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _windowEvents.Dispose();
        Changed = null;
    }
}
