namespace SyncWallpaper.Core;

/// <summary>
/// Named failure points used by the reliability tests and diagnostics harness.
/// The production default is a no-op injector; fault injection is never enabled
/// by normal application startup.
/// </summary>
public enum FaultPoint
{
    ProcessStart,
    ProcessImmediateExit,
    HostNoResponse,
    IpcFailure,
    IpcTimeout,
    IpcCorruptMessage,
    StopTimeout,
    DisplayApply,
    DisplayVerify,
    DisplayRollback,
    AudioDeviceDisappearance,
    AudioApply,
    WindowClosed,
    ExplorerHandleInvalid,
    ConfigurationCorrupt,
    ConfigurationWrite,
    LogUnwritable,
    Cancellation
}

public interface IFaultInjector
{
    bool Enabled { get; }
    void ThrowIfRequested(FaultPoint point);
    bool IsRequested(FaultPoint point);
}

public sealed class NoFaultInjector : IFaultInjector
{
    public static NoFaultInjector Instance { get; } = new();
    public bool Enabled => false;
    public void ThrowIfRequested(FaultPoint point) { }
    public bool IsRequested(FaultPoint point) => false;
}

/// <summary>Deterministic, repeatable injector. Each configured point can be
/// consumed a fixed number of times, or indefinitely with <see cref="-1"/>.</summary>
public sealed class ConfigurableFaultInjector : IFaultInjector
{
    private readonly Dictionary<FaultPoint, int> _remaining;
    private readonly object _gate = new();

    public ConfigurableFaultInjector(IEnumerable<FaultPoint>? points = null, int occurrences = 1)
    {
        _remaining = (points ?? Array.Empty<FaultPoint>()).Distinct().ToDictionary(x => x, _ => occurrences);
    }

    public bool Enabled => _remaining.Count > 0;

    public bool IsRequested(FaultPoint point)
    {
        lock (_gate) return _remaining.TryGetValue(point, out var count) && count != 0;
    }

    public void ThrowIfRequested(FaultPoint point)
    {
        lock (_gate)
        {
            if (!_remaining.TryGetValue(point, out var count) || count == 0) return;
            if (count > 0) _remaining[point] = count - 1;
            throw new InjectedFaultException(point);
        }
    }
}

public sealed class InjectedFaultException : Exception
{
    public InjectedFaultException(FaultPoint point)
        : base($"Injected fault at {point}.") => Point = point;

    public FaultPoint Point { get; }
}
