namespace SyncWallpaper.Core;

public enum WallpaperTransactionState
{
    Preparing,
    WaitingForStableTopology,
    Applying,
    Verifying,
    Retrying,
    RollingBack,
    Completed,
    Failed,
    RollbackFailed,
    Cancelled,
    Superseded
}

public sealed record WallpaperTransactionStatus(
    long Generation,
    WallpaperTransactionState State,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    int AppliedCount,
    int ExpectedCount,
    int RetryCount,
    bool RollbackSucceeded,
    string Message)
{
    public double DurationMilliseconds => ((CompletedAtUtc ?? DateTime.UtcNow) - StartedAtUtc).TotalMilliseconds;
}

/// <summary>Small, deterministic state machine used by the Windows service and tests.</summary>
public sealed class WallpaperTransactionStateMachine
{
    private static readonly IReadOnlyDictionary<WallpaperTransactionState, WallpaperTransactionState[]> Allowed =
        new Dictionary<WallpaperTransactionState, WallpaperTransactionState[]>
        {
            [WallpaperTransactionState.Preparing] = new[] { WallpaperTransactionState.WaitingForStableTopology, WallpaperTransactionState.Applying, WallpaperTransactionState.Cancelled, WallpaperTransactionState.Superseded, WallpaperTransactionState.Failed },
            [WallpaperTransactionState.WaitingForStableTopology] = new[] { WallpaperTransactionState.Applying, WallpaperTransactionState.Cancelled, WallpaperTransactionState.Superseded, WallpaperTransactionState.Failed },
            // A no-op transaction can complete directly when every active
            // monitor already points at its rendered target path.
            [WallpaperTransactionState.Applying] = new[] { WallpaperTransactionState.Verifying, WallpaperTransactionState.Retrying, WallpaperTransactionState.RollingBack, WallpaperTransactionState.Completed, WallpaperTransactionState.Cancelled, WallpaperTransactionState.Superseded, WallpaperTransactionState.Failed },
            [WallpaperTransactionState.Verifying] = new[] { WallpaperTransactionState.Completed, WallpaperTransactionState.Retrying, WallpaperTransactionState.RollingBack, WallpaperTransactionState.Failed, WallpaperTransactionState.Cancelled, WallpaperTransactionState.Superseded },
            [WallpaperTransactionState.Retrying] = new[] { WallpaperTransactionState.Applying, WallpaperTransactionState.Verifying, WallpaperTransactionState.RollingBack, WallpaperTransactionState.Failed, WallpaperTransactionState.Cancelled, WallpaperTransactionState.Superseded },
            [WallpaperTransactionState.RollingBack] = new[] { WallpaperTransactionState.Failed, WallpaperTransactionState.RollbackFailed, WallpaperTransactionState.Cancelled },
            [WallpaperTransactionState.Completed] = Array.Empty<WallpaperTransactionState>(),
            [WallpaperTransactionState.Failed] = Array.Empty<WallpaperTransactionState>(),
            [WallpaperTransactionState.RollbackFailed] = Array.Empty<WallpaperTransactionState>(),
            [WallpaperTransactionState.Cancelled] = Array.Empty<WallpaperTransactionState>(),
            [WallpaperTransactionState.Superseded] = Array.Empty<WallpaperTransactionState>()
        };

    public WallpaperTransactionState Current { get; private set; } = WallpaperTransactionState.Preparing;

    public bool TryTransition(WallpaperTransactionState next)
    {
        if (Current == next) return true;
        if (!Allowed.TryGetValue(Current, out var nextStates) || !nextStates.Contains(next)) return false;
        Current = next;
        return true;
    }

    public void Transition(WallpaperTransactionState next)
    {
        if (!TryTransition(next)) throw new InvalidOperationException($"Invalid wallpaper transaction transition: {Current} -> {next}");
    }
}
