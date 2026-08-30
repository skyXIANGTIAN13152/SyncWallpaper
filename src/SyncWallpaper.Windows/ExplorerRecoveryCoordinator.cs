using System.Diagnostics;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public enum ExplorerRecoveryState { Available, Unavailable, Waiting, Recovering, Faulted }

/// <summary>
/// Passive Explorer recovery. It never terminates Explorer; it recreates the
/// next wallpaper COM object through a fresh transaction and only re-applies
/// after Explorer is observable and the backoff has elapsed.
/// </summary>
public sealed class ExplorerRecoveryCoordinator : IDisposable
{
    private readonly Func<bool> _isAvailable;
    private readonly Func<CancellationToken, Task> _reapply;
    private readonly Action<string> _log;
    private readonly ExplorerRecoveryBackoff _backoff = new();
    private readonly object _gate = new();
    private CancellationTokenSource _stop = new();
    private System.Threading.Timer? _timer;
    private bool _running;
    private bool _disposed;
    private DateTime _lastNoticeUtc;
    public ExplorerRecoveryState State { get; private set; } = ExplorerRecoveryState.Available;
    public DateTime? LastAttemptUtc { get; private set; }
    public string LastError { get; private set; } = string.Empty;

    public ExplorerRecoveryCoordinator(Func<CancellationToken, Task> reapply, Action<string>? log = null, Func<bool>? availability = null)
    {
        _reapply = reapply ?? throw new ArgumentNullException(nameof(reapply));
        _log = log ?? (_ => { });
        _isAvailable = availability ?? (() => Process.GetProcessesByName("explorer").Length > 0);
    }

    public void NotifyUnavailable(string reason)
    {
        lock (_gate)
        {
            if (_disposed) return;
            State = ExplorerRecoveryState.Unavailable;
            LastError = reason;
            var now = DateTime.UtcNow;
            if (now - _lastNoticeUtc > TimeSpan.FromSeconds(5)) { _log("Explorer is temporarily unavailable: " + reason); _lastNoticeUtc = now; }
            ScheduleLocked();
        }
    }

    public void NotifyShellEvent(string reason)
    {
        if (string.Equals(reason, "TaskbarCreated", StringComparison.OrdinalIgnoreCase)) NotifyUnavailable("TaskbarCreated received; waiting for Explorer recovery");
    }

    private void ScheduleLocked()
    {
        State = ExplorerRecoveryState.Waiting;
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => _ = AttemptAsync(), null, _backoff.NextDelay, Timeout.InfiniteTimeSpan);
    }

    private async Task AttemptAsync()
    {
        lock (_gate)
        {
            if (_disposed || _running) return;
            _running = true;
            LastAttemptUtc = DateTime.UtcNow;
            State = ExplorerRecoveryState.Recovering;
        }
        try
        {
            if (!_isAvailable())
            {
                _backoff.RecordFailure();
                lock (_gate) { State = ExplorerRecoveryState.Unavailable; ScheduleLocked(); }
                return;
            }
            await _reapply(_stop.Token).ConfigureAwait(false);
            _backoff.RecordSuccess();
            lock (_gate) { State = ExplorerRecoveryState.Available; LastError = string.Empty; }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _backoff.RecordFailure();
            lock (_gate) { State = ExplorerRecoveryState.Faulted; LastError = ex.Message; ScheduleLocked(); }
            _log("Passive Explorer recovery failed: " + ex.Message);
        }
        finally { lock (_gate) _running = false; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            _stop.Cancel();
            _stop.Dispose();
        }
    }
}
