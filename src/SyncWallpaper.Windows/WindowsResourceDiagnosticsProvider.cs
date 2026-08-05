using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SyncWallpaper.Windows;

public sealed record WindowsResourceSnapshot(
    DateTime CapturedAt,
    long WorkingSetBytes,
    long PrivateBytes,
    long GcHeapBytes,
    int HandleCount,
    int GdiObjects,
    int UserObjects,
    double CpuSeconds,
    int ThreadCount = 0);

/// <summary>
/// Read-only process/resource diagnostics. The two GUI object counters are kept
/// behind this Windows adapter so the application layer does not contain P/Invoke.
/// </summary>
public sealed class WindowsResourceDiagnosticsProvider
{
    private const uint ObjectTypeGdi = 0;
    private const uint ObjectTypeUser = 1;

    public WindowsResourceSnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        var gdi = 0;
        var user = 0;
        try
        {
            gdi = unchecked((int)GetGuiResources(process.Handle, ObjectTypeGdi));
            user = unchecked((int)GetGuiResources(process.Handle, ObjectTypeUser));
        }
        catch
        {
            // Diagnostics must not affect the wallpaper/monitoring process.
        }

        return new WindowsResourceSnapshot(
            DateTime.Now,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false),
            process.HandleCount,
            gdi,
            user,
            process.TotalProcessorTime.TotalSeconds,
            process.Threads.Count);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
