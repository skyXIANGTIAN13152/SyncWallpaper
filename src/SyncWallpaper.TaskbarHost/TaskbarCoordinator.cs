using SyncWallpaper.Core;

namespace SyncWallpaper.TaskbarHost;

public sealed class TaskbarCoordinator : IDisposable
{
    private readonly Func<IReadOnlyList<MonitorIdentity>> _monitorProvider;
    private readonly ITaskWindowPlatform _windowPlatform;
    private readonly ITaskbarChangeSource _changeSource;
    private readonly ITaskbarViewHost _viewHost;
    private readonly TimeSpan _debounceDelay;
    private readonly TimeSpan _periodicInterval;
    private readonly object _refreshGate = new();
    private readonly object _timerGate = new();
    private System.Threading.Timer? _debounceTimer;
    private System.Threading.Timer? _periodicTimer;
    private bool _started;
    private bool _disposed;

    public TaskbarCoordinator(
        Func<IReadOnlyList<MonitorIdentity>> monitorProvider,
        ITaskWindowPlatform windowPlatform,
        ITaskbarChangeSource changeSource,
        ITaskbarViewHost viewHost,
        TimeSpan? debounceDelay = null,
        TimeSpan? periodicInterval = null)
    {
        _monitorProvider = monitorProvider;
        _windowPlatform = windowPlatform;
        _changeSource = changeSource;
        _viewHost = viewHost;
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(180);
        _periodicInterval = periodicInterval ?? TimeSpan.FromSeconds(5);
    }

    public event EventHandler<TaskbarHostStatus>? StatusChanged;
    public TaskbarSnapshot LastSnapshot { get; private set; } = TaskbarSnapshot.Empty;
    public TaskbarHostStatus Status { get; private set; } = TaskbarHostStatus.Stopped;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        if (!_changeSource.IsActive) throw new InvalidOperationException("WinEventHook 注册失败，副屏任务栏未启动。");

        _started = true;
        _changeSource.Changed += ChangeSource_Changed;
        _viewHost.StatusChanged += ViewHost_StatusChanged;
        _debounceTimer = new System.Threading.Timer(_ => RefreshSafely(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _periodicTimer = new System.Threading.Timer(_ => ScheduleRefresh(TimeSpan.Zero), null, _periodicInterval, _periodicInterval);
        try
        {
            RefreshCore();
        }
        catch
        {
            StopCore();
            throw;
        }
    }

    public void RefreshNow()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started) throw new InvalidOperationException("副屏任务栏尚未启动。");
        RefreshCore();
    }

    private void ChangeSource_Changed(object? sender, EventArgs e) => ScheduleRefresh(_debounceDelay);

    private void ViewHost_StatusChanged(object? sender, EventArgs e)
    {
        // Render can marshal to the WPF dispatcher while RefreshCore owns the
        // refresh gate. Never take that same gate from the dispatcher callback.
        if (_disposed || !_started) return;
        UpdateStatus(Status with
        {
            BarCount = _viewHost.BarCount,
            Bars = _viewHost.Bars,
            HookActive = _changeSource.IsActive
        });
    }

    private void ScheduleRefresh(TimeSpan delay)
    {
        if (_disposed || !_started) return;
        lock (_timerGate)
        {
            if (_disposed || !_started) return;
            try { _debounceTimer?.Change(delay, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
    }

    private void RefreshSafely()
    {
        try { RefreshCore(); }
        catch (Exception ex)
        {
            UpdateStatus(Status with { State = "Running", LastError = ex.Message, HookActive = _changeSource.IsActive });
        }
    }

    private void RefreshCore()
    {
        lock (_refreshGate)
        {
            if (_disposed || !_started) return;
            var monitors = _monitorProvider();
            var candidates = _windowPlatform.Enumerate();
            var snapshot = TaskbarSnapshotBuilder.Build(monitors, candidates, _windowPlatform.ExplorerProcessId);
            _viewHost.Render(snapshot, new TaskbarWindowActions(ActivateOrMinimize, CloseWindow));
            LastSnapshot = snapshot;
            UpdateStatus(new TaskbarHostStatus(
                "Running",
                snapshot.Monitors.Count,
                _viewHost.BarCount,
                snapshot.SecondaryTaskCount,
                _changeSource.IsActive,
                snapshot.ExplorerProcessId,
                snapshot.CapturedAtUtc,
                null,
                _viewHost.Bars));
        }
    }

    private TaskWindowActionResult ActivateOrMinimize(nint handle)
    {
        var result = _windowPlatform.ActivateOrMinimize(handle);
        ScheduleRefresh(TimeSpan.FromMilliseconds(60));
        return result;
    }

    private TaskWindowCloseResult CloseWindow(nint handle)
    {
        var result = _windowPlatform.Close(handle);
        ScheduleRefresh(TimeSpan.FromMilliseconds(250));
        return result;
    }

    private void UpdateStatus(TaskbarHostStatus status)
    {
        Status = status;
        try { StatusChanged?.Invoke(this, status); } catch { }
    }

    private void StopCore()
    {
        if (!_started) return;
        _started = false;
        _changeSource.Changed -= ChangeSource_Changed;
        _viewHost.StatusChanged -= ViewHost_StatusChanged;
        lock (_timerGate)
        {
            _debounceTimer?.Dispose();
            _periodicTimer?.Dispose();
            _debounceTimer = null;
            _periodicTimer = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCore();
        lock (_refreshGate)
        {
            _changeSource.Dispose();
            _viewHost.Dispose();
            _windowPlatform.Dispose();
            LastSnapshot = TaskbarSnapshot.Empty;
            UpdateStatus(TaskbarHostStatus.Stopped);
            StatusChanged = null;
        }
    }
}
