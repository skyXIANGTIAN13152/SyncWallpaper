namespace SyncWallpaper.App;

/// <summary>
/// The notification-area icon uses a separate, deliberately geometric language.
/// The richer observing-eye artwork stays inside the WPF application window.
/// </summary>
public enum TrayIconState
{
    Normal,
    Paused,
    Recognizing,
    Error
}
