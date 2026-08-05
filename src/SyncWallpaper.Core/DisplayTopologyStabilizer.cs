namespace SyncWallpaper.Core;

/// <summary>
/// Coalesces a burst of display/power/shell notifications and accepts a
/// topology only after two equal observations.  Every signal supersedes the
/// previous run, so an old monitor snapshot cannot be applied after a newer
/// event arrives.
/// </summary>
public sealed class DisplayTopologyStabilizer : IDisposable
{
    private readonly Func<DisplaySnapshot> _sample;
    private readonly Func<DisplaySnapshot, CancellationToken, Task> _onStable;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _sampleInterval;
    private readonly TimeSpan _maximumWait;
    private readonly object _gate = new();
    private CancellationTokenSource _cancel = new();
    private long _version;
    private string _lastSignature = string.Empty;
    private bool _disposed;

    public DisplayTopologyStabilizer(
        Func<DisplaySnapshot> sample,
        Func<DisplaySnapshot, CancellationToken, Task> onStable,
        TimeSpan? initialDelay = null,
        TimeSpan? sampleInterval = null,
        TimeSpan? maximumWait = null)
    {
        _sample = sample;
        _onStable = onStable;
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(2);
        _sampleInterval = sampleInterval ?? TimeSpan.FromMilliseconds(250);
        _maximumWait = maximumWait ?? TimeSpan.FromSeconds(10);
    }

    public long Version => Interlocked.Read(ref _version);

    public void Signal()
    {
        CancellationToken token;
        long version;
        lock (_gate)
        {
            if (_disposed) return;
            version = Interlocked.Increment(ref _version);
            _cancel.Cancel();
            _cancel.Dispose();
            _cancel = new CancellationTokenSource();
            token = _cancel.Token;
        }
        _ = RunAsync(version, token);
    }

    private async Task RunAsync(long version, CancellationToken token)
    {
        try
        {
            await Task.Delay(_initialDelay, token).ConfigureAwait(false);
            var started = DateTime.UtcNow;
            DisplaySnapshot? previous = null;
            while (DateTime.UtcNow - started <= _maximumWait)
            {
                token.ThrowIfCancellationRequested();
                var current = _sample();
                if (previous is not null && string.Equals(previous.Signature, current.Signature, StringComparison.Ordinal))
                {
                    if (version != Version) return;
                    lock (_gate)
                    {
                        if (string.Equals(_lastSignature, current.Signature, StringComparison.Ordinal)) return;
                        _lastSignature = current.Signature;
                    }
                    await _onStable(current, token).ConfigureAwait(false);
                    return;
                }
                previous = current;
                await Task.Delay(_sampleInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Increment(ref _version);
            _cancel.Cancel();
            _cancel.Dispose();
        }
    }
}
