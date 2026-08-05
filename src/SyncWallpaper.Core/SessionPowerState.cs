namespace SyncWallpaper.Core;

public enum SessionPowerState
{
    Active,
    Suspending,
    Suspended,
    Resuming,
    WaitingForSession,
    WaitingForExplorer,
    WaitingForStableTopology
}

/// <summary>Pure state machine for suspend/resume recovery; it never initiates sleep.</summary>
public sealed class SessionPowerStateMachine
{
    public SessionPowerState Current { get; private set; } = SessionPowerState.Active;

    public bool BeginSuspend() => Move(SessionPowerState.Suspending, SessionPowerState.Active);
    public bool MarkSuspended() => Move(SessionPowerState.Suspended, SessionPowerState.Suspending);
    public bool BeginResume() => Move(SessionPowerState.Resuming, SessionPowerState.Suspended, SessionPowerState.Suspending, SessionPowerState.Active);
    public bool SessionUnavailable() => Move(SessionPowerState.WaitingForSession, SessionPowerState.Resuming, SessionPowerState.Active);
    public bool ExplorerUnavailable() => Move(SessionPowerState.WaitingForExplorer, SessionPowerState.Resuming, SessionPowerState.WaitingForSession);
    public bool TopologySampling() => Move(SessionPowerState.WaitingForStableTopology, SessionPowerState.Resuming, SessionPowerState.WaitingForExplorer, SessionPowerState.WaitingForSession);
    public bool TopologyStable() => Move(SessionPowerState.Active, SessionPowerState.WaitingForStableTopology, SessionPowerState.Resuming);

    private bool Move(SessionPowerState next, params SessionPowerState[] allowed)
    {
        if (!allowed.Contains(Current)) return false;
        Current = next;
        return true;
    }
}

public sealed class ExplorerRecoveryBackoff
{
    private readonly TimeSpan _initial;
    private readonly TimeSpan _maximum;
    private int _failures;
    public ExplorerRecoveryBackoff(TimeSpan? initial = null, TimeSpan? maximum = null)
    {
        _initial = initial ?? TimeSpan.FromMilliseconds(250);
        _maximum = maximum ?? TimeSpan.FromSeconds(30);
    }

    public int ConsecutiveFailures => Volatile.Read(ref _failures);
    public TimeSpan NextDelay => TimeSpan.FromMilliseconds(Math.Min(_maximum.TotalMilliseconds, _initial.TotalMilliseconds * Math.Pow(2, Math.Min(ConsecutiveFailures, 8))));
    public void RecordFailure() => Interlocked.Increment(ref _failures);
    public void RecordSuccess() => Interlocked.Exchange(ref _failures, 0);
}
