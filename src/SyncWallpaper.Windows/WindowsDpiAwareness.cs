using System.Runtime.InteropServices;

namespace SyncWallpaper.Windows;

/// <summary>Keeps diagnostic Screen/QueryDisplayConfig coordinates in physical pixels.</summary>
public static class WindowsDpiAwareness
{
    private static readonly nint PerMonitorAwareV2 = new(-4);

    public static bool TryEnablePerMonitorV2()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(PerMonitorAwareV2)) return true;
            return SetProcessDpiAwareness(2) == 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(nint dpiAwarenessContext);

    [DllImport("shcore.dll")]
    private static extern int SetProcessDpiAwareness(int value);
}
