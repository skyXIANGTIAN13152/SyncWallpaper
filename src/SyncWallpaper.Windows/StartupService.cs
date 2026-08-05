using Microsoft.Win32;

namespace SyncWallpaper.Windows;

public sealed class StartupService
{
    private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "SyncWallpaper";
    public bool IsEnabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey, false); return key?.GetValue(ValueName) is not null; }
    }
    public void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{executablePath}\" --background"); else key.DeleteValue(ValueName, false);
    }
}
