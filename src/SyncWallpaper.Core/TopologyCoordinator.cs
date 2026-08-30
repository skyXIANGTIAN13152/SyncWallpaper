namespace SyncWallpaper.Core;

public enum TopologySignalKind { Display, Device, Power, Session, Explorer, Dpi, Settings, Manual, Startup }

public sealed record TopologySignal(TopologySignalKind Kind, string Reason, bool Manual, long Generation, DateTime TimestampUtc);

public sealed record TopologyCoordinatorResult(long Generation, bool Applied, bool Superseded, string Reason, string Signature);

/// <summary>
/// Latest-state-only coordinator. It serializes sampling/apply work, cancels
/// stale generations, coalesces duplicate signatures and gives manual requests
/// precedence over automatic signals without ever guessing a monitor identity.
/// </summary>
public sealed class TopologyCoordinator : IAsyncDisposable
{
    private readonly Func<TopologySignal, CancellationToken, Task<(DisplaySnapshot Snapshot, bool Stable)>> _sample;
    private readonly Func<TopologySignal, DisplaySnapshot, CancellationToken, Task> _apply;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _active;
    private TopologySignal? _pending;
    private Task? _loop;
    private string _lastAppliedSignature = string.Empty;
    private bool _disposed;
    private long _generation;

    public TopologyCoordinator(
        Func<TopologySignal, CancellationToken, Task<(DisplaySnapshot Snapshot, bool Stable)>> sample,
        Func<TopologySignal, DisplaySnapshot, CancellationToken, Task> apply)
    {
        _sample = sample ?? throw new ArgumentNullException(nameof(sample));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    public long CurrentGeneration => Interlocked.Read(ref _generation);
    public event EventHandler<TopologyCoordinatorResult>? Completed;

    public long Signal(TopologySignalKind kind, string reason, bool manual = false)
    {
        lock (_gate)
        {
            if (_disposed) return CurrentGeneration;
            var generation = Interlocked.Increment(ref _generation);
            _active?.Cancel();
            _active?.Dispose();
            _active = new CancellationTokenSource();
            _pending = new(kind, reason, manual, generation, DateTime.UtcNow);
            _loop ??= Task.Run(ProcessAsync);
            _wake.Release();
            return generation;
        }
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (_disposed) return _loop ?? Task.CompletedTask;
            _disposed = true;
            _stop.Cancel();
            _active?.Cancel();
            _wake.Release();
        }
        return _loop ?? Task.CompletedTask;
    }

    private async Task ProcessAsync()
    {
        try
        {
            while (true)
            {
                await _wake.WaitAsync(_stop.Token).ConfigureAwait(false);
                TopologySignal? signal;
                CancellationToken token;
                lock (_gate)
                {
                    if (_disposed) break;
                    signal = _pending;
                    _pending = null;
                    token = _active?.Token ?? _stop.Token;
                }
                if (signal is null) continue;
                try
                {
                    var sampled = await _sample(signal, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (!sampled.Stable)
                    {
                        Publish(new(signal.Generation, false, false, "Topology is not stable yet", sampled.Snapshot.Signature));
                        continue;
                    }
                    lock (_gate)
                    {
                        if (signal.Generation != CurrentGeneration || (_pending is not null && !_pending.Manual && !signal.Manual))
                        {
                            Publish(new(signal.Generation, false, true, "Superseded by a newer topology event", sampled.Snapshot.Signature));
                            continue;
                        }
                        if (!signal.Manual && string.Equals(_lastAppliedSignature, sampled.Snapshot.Signature, StringComparison.Ordinal))
                        {
                            Publish(new(signal.Generation, false, false, "Duplicate topology signature was ignored", sampled.Snapshot.Signature));
                            continue;
                        }
                    }
                    await _apply(signal, sampled.Snapshot, token).ConfigureAwait(false);
                    lock (_gate) _lastAppliedSignature = sampled.Snapshot.Signature;
                    Publish(new(signal.Generation, true, false, signal.Manual ? "Manual topology application completed" : "Topology application completed", sampled.Snapshot.Signature));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    Publish(new(signal.Generation, false, signal.Generation != CurrentGeneration, "Topology application cancelled", string.Empty));
                }
                catch (Exception ex)
                {
                    Publish(new(signal.Generation, false, false, "Topology application failed: " + ex.Message, string.Empty));
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private void Publish(TopologyCoordinatorResult result) => Completed?.Invoke(this, result);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _wake.Dispose();
        _stop.Dispose();
        _active?.Dispose();
    }
}
