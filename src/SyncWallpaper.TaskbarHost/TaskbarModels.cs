using SyncWallpaper.Core;

namespace SyncWallpaper.TaskbarHost;

public sealed record TaskWindowCandidate
{
    public nint Handle { get; init; }
    public int ProcessId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string ProcessPath { get; init; } = string.Empty;
    public string WindowClass { get; init; } = string.Empty;
    public string AppUserModelId { get; init; } = string.Empty;
    public Int32Rect Bounds { get; init; }
    public bool IsVisible { get; init; }
    public bool IsCloaked { get; init; }
    public bool IsToolWindow { get; init; }
    public bool IsAppWindow { get; init; }
    public bool IsNoActivate { get; init; }
    public bool HasOwner { get; init; }
    public bool IsRootOwner { get; init; } = true;
    public bool IsOwnProcess { get; init; }
    public bool IsMinimized { get; init; }
    public bool IsForeground { get; init; }
    public bool IsUwp { get; init; }
    public bool IsElevated { get; init; }
}

public sealed record TaskbarMonitor(
    string RuntimeKey,
    string DisplayLabel,
    string WindowsDisplayName,
    string DevicePath,
    Int32Rect Bounds,
    int Dpi,
    bool IsPrimary);

public sealed record TaskbarTaskItem(
    nint Handle,
    int ProcessId,
    string Title,
    string ProcessName,
    string ProcessPath,
    string WindowClass,
    string AppUserModelId,
    string MonitorKey,
    bool IsMinimized,
    bool IsForeground,
    bool IsUwp,
    bool IsElevated);

public sealed record TaskbarBarStatus(
    string MonitorKey,
    string DisplayLabel,
    Int32Rect Bounds,
    int TaskCount,
    int GroupCount = 0,
    int PinnedCount = 0,
    bool AutoHide = false,
    bool IsHidden = false,
    bool WorkAreaReserved = false,
    string? PlacementError = null);

public sealed record TaskbarSnapshot(
    DateTime CapturedAtUtc,
    IReadOnlyList<TaskbarMonitor> Monitors,
    IReadOnlyList<TaskbarTaskItem> Tasks,
    int ExplorerProcessId)
{
    public static TaskbarSnapshot Empty { get; } = new(DateTime.UtcNow, Array.Empty<TaskbarMonitor>(), Array.Empty<TaskbarTaskItem>(), 0);
    public int SecondaryMonitorCount => Monitors.Count(x => !x.IsPrimary);
    public int SecondaryTaskCount => Tasks.Count(task => Monitors.Any(m => !m.IsPrimary && m.RuntimeKey == task.MonitorKey));
}

public enum TaskWindowActionResult
{
    Missing,
    Activated,
    Restored,
    Minimized,
    AccessDenied,
    Failed
}

public enum TaskWindowCloseResult
{
    Missing,
    Requested,
    AccessDenied,
    Failed
}

public sealed record TaskbarWindowActions(
    Func<nint, TaskWindowActionResult> ActivateOrMinimize,
    Func<nint, TaskWindowCloseResult> Close);

public sealed record TaskbarHostStatus(
    string State,
    int MonitorCount,
    int BarCount,
    int TaskCount,
    bool HookActive,
    int ExplorerProcessId,
    DateTime? LastRefreshUtc,
    string? LastError,
    IReadOnlyList<TaskbarBarStatus>? Bars = null)
{
    public static TaskbarHostStatus Stopped { get; } = new("Stopped", 0, 0, 0, false, 0, null, null);
}

public interface ITaskWindowPlatform : IDisposable
{
    IReadOnlyList<TaskWindowCandidate> Enumerate();
    TaskWindowActionResult ActivateOrMinimize(nint handle);
    TaskWindowCloseResult Close(nint handle);
    int ExplorerProcessId { get; }
}

public interface ITaskbarChangeSource : IDisposable
{
    event EventHandler? Changed;
    bool IsActive { get; }
}

public interface ITaskbarViewHost : IDisposable
{
    event EventHandler? StatusChanged;
    int BarCount { get; }
    IReadOnlyList<TaskbarBarStatus> Bars { get; }
    void Render(TaskbarSnapshot snapshot, TaskbarWindowActions actions);
}

public static class TaskbarWindowFilter
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "XamlExplorerHostIslandWindow"
    };

    public static bool ShouldInclude(TaskWindowCandidate candidate)
    {
        if (candidate.Handle == 0 || !candidate.IsVisible || candidate.IsCloaked || candidate.IsOwnProcess) return false;
        if (string.IsNullOrWhiteSpace(candidate.Title)) return false;
        if (candidate.Bounds.Width <= 0 || candidate.Bounds.Height <= 0) return false;
        if (ShellClasses.Contains(candidate.WindowClass)) return false;
        if ((candidate.IsToolWindow || candidate.IsNoActivate) && !candidate.IsAppWindow) return false;
        if ((candidate.HasOwner || !candidate.IsRootOwner) && !candidate.IsAppWindow) return false;
        return true;
    }
}

public static class TaskbarSnapshotBuilder
{
    public static TaskbarSnapshot Build(
        IReadOnlyList<MonitorIdentity> identities,
        IEnumerable<TaskWindowCandidate> candidates,
        int explorerProcessId = 0)
    {
        var monitors = identities
            .Where(m => m.Width > 0 && m.Height > 0)
            .Select((m, index) => new TaskbarMonitor(
                RuntimeMonitorKey(m, index),
                string.IsNullOrWhiteSpace(m.DisplayLabel) ? $"显示器 {index + 1}" : m.DisplayLabel,
                m.WindowsDisplayName,
                m.MonitorDevicePath,
                new Int32Rect(m.DesktopX, m.DesktopY, m.Width, m.Height),
                Math.Max(96, m.Dpi),
                m.IsPrimary))
            .ToArray();

        var tasks = new List<TaskbarTaskItem>();
        foreach (var candidate in candidates.Where(TaskbarWindowFilter.ShouldInclude))
        {
            var monitor = FindMonitor(candidate.Bounds, monitors);
            if (monitor is null) continue;
            tasks.Add(new TaskbarTaskItem(
                candidate.Handle,
                candidate.ProcessId,
                candidate.Title.Trim(),
                candidate.ProcessName,
                candidate.ProcessPath,
                candidate.WindowClass,
                candidate.AppUserModelId,
                monitor.RuntimeKey,
                candidate.IsMinimized,
                candidate.IsForeground,
                candidate.IsUwp,
                candidate.IsElevated));
        }

        return new TaskbarSnapshot(
            DateTime.UtcNow,
            monitors,
            tasks.OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToArray(),
            explorerProcessId);
    }

    public static string RuntimeMonitorKey(MonitorIdentity monitor, int index)
    {
        var identity = !string.IsNullOrWhiteSpace(monitor.StableId)
            ? monitor.StableId
            : !string.IsNullOrWhiteSpace(monitor.MonitorDevicePath)
                ? monitor.MonitorDevicePath
                : $"monitor-{index}";
        // Runtime geometry prevents two physically identical, serial-less displays
        // from sharing one bar. It is deliberately not persisted as an identity.
        return $"{identity}|{monitor.DesktopX},{monitor.DesktopY},{monitor.Width}x{monitor.Height}";
    }

    private static TaskbarMonitor? FindMonitor(Int32Rect window, IReadOnlyList<TaskbarMonitor> monitors)
    {
        TaskbarMonitor? best = null;
        long bestArea = 0;
        foreach (var monitor in monitors)
        {
            var area = IntersectionArea(window, monitor.Bounds);
            if (area <= bestArea) continue;
            best = monitor;
            bestArea = area;
        }
        if (best is not null) return best;

        var centerX = window.Left + window.Width / 2d;
        var centerY = window.Top + window.Height / 2d;
        return monitors.OrderBy(m => DistanceSquared(centerX, centerY, m.Bounds)).FirstOrDefault();
    }

    private static long IntersectionArea(Int32Rect left, Int32Rect right)
    {
        var width = Math.Max(0, Math.Min((long)left.Left + left.Width, (long)right.Left + right.Width) - Math.Max(left.Left, right.Left));
        var height = Math.Max(0, Math.Min((long)left.Top + left.Height, (long)right.Top + right.Height) - Math.Max(left.Top, right.Top));
        return width * height;
    }

    private static double DistanceSquared(double x, double y, Int32Rect bounds)
    {
        var nearestX = Math.Clamp(x, bounds.Left, (double)bounds.Left + bounds.Width);
        var nearestY = Math.Clamp(y, bounds.Top, (double)bounds.Top + bounds.Height);
        return Math.Pow(x - nearestX, 2) + Math.Pow(y - nearestY, 2);
    }
}
