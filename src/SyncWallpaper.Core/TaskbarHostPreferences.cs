namespace SyncWallpaper.Core;

/// <summary>
/// Global behavior for the optional secondary taskbar process.  The host reads
/// this document only when it starts; the main application restarts that one
/// isolated module after a user applies a change.
/// </summary>
public sealed class TaskbarHostPreferences
{
    public const string FileName = "taskbar-settings.json";
    public int SchemaVersion { get; set; } = 1;
    public bool AutoHide { get; set; }
    public bool ReserveWorkArea { get; set; } = true;
    public bool ShowPinnedItems { get; set; } = true;
    public bool ShowClock { get; set; } = true;
    public int Height { get; set; } = 48;
    public int RevealThickness { get; set; } = 2;
    public int HideDelayMilliseconds { get; set; } = 650;

    public static bool Validate(TaskbarHostPreferences value)
        => value.SchemaVersion == 1
            && value.Height is >= 36 and <= 72
            && value.RevealThickness is >= 1 and <= 8
            && value.HideDelayMilliseconds is >= 150 and <= 5000;

    public static TaskbarHostPreferences Normalize(TaskbarHostPreferences? value)
    {
        value ??= new TaskbarHostPreferences();
        return new TaskbarHostPreferences
        {
            SchemaVersion = 1,
            AutoHide = value.AutoHide,
            // An automatically hidden bar must not permanently remove desktop
            // work area.  This also avoids competing with Explorer's own
            // per-monitor auto-hide AppBar registration.
            ReserveWorkArea = !value.AutoHide && value.ReserveWorkArea,
            ShowPinnedItems = value.ShowPinnedItems,
            ShowClock = value.ShowClock,
            Height = Math.Clamp(value.Height, 36, 72),
            RevealThickness = Math.Clamp(value.RevealThickness, 1, 8),
            HideDelayMilliseconds = Math.Clamp(value.HideDelayMilliseconds, 150, 5000)
        };
    }
}
