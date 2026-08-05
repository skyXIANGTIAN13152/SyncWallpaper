namespace SyncWallpaper.Core;

public enum SafeModeTrigger { StartupCrash, ConfigurationFailure, HostCrashLoop, WallpaperRollbackFailure, ProfileOscillation }

public sealed class SafeModePolicy
{
    private readonly object _gate = new();
    private readonly int _failureThreshold;
    private int _startupFailures;
    public bool Enabled { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTime? EnteredAtUtc { get; private set; }
    public SafeModePolicy(int failureThreshold = 3) => _failureThreshold = Math.Max(1, failureThreshold);
    public int StartupFailures => Volatile.Read(ref _startupFailures);

    public bool Record(SafeModeTrigger trigger, string message)
    {
        lock (_gate)
        {
            if (trigger == SafeModeTrigger.StartupCrash) Interlocked.Increment(ref _startupFailures);
            if (trigger == SafeModeTrigger.StartupCrash && _startupFailures < _failureThreshold && !Enabled) return false;
            Enabled = true;
            Reason = message;
            EnteredAtUtc = DateTime.UtcNow;
            return true;
        }
    }

    public void RecordCleanStart() => Interlocked.Exchange(ref _startupFailures, 0);

    public bool TryLeave(string confirmation)
    {
        if (!string.Equals(confirmation, "YES", StringComparison.Ordinal)) return false;
        lock (_gate)
        {
            Enabled = false;
            Reason = string.Empty;
            EnteredAtUtc = null;
            Interlocked.Exchange(ref _startupFailures, 0);
            return true;
        }
    }
}
