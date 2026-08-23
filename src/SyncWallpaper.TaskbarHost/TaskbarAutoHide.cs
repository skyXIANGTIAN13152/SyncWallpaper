using SyncWallpaper.Core;

namespace SyncWallpaper.TaskbarHost;

public enum TaskbarAutoHideAction
{
    None,
    Show,
    Hide
}

/// <summary>
/// Small deterministic state machine kept separate from cursor/PInvoke code so
/// edge reveal and delayed hiding can be verified without moving the real mouse.
/// </summary>
public sealed class TaskbarAutoHideStateMachine
{
    private DateTime? _pointerAwaySinceUtc;
    public bool IsHidden { get; private set; }

    public TaskbarAutoHideAction Update(
        bool revealRequested,
        bool pointerInside,
        bool keepOpen,
        DateTime nowUtc,
        TimeSpan hideDelay)
    {
        if (IsHidden)
        {
            _pointerAwaySinceUtc = null;
            if (!revealRequested) return TaskbarAutoHideAction.None;
            IsHidden = false;
            return TaskbarAutoHideAction.Show;
        }

        if (pointerInside || keepOpen)
        {
            _pointerAwaySinceUtc = null;
            return TaskbarAutoHideAction.None;
        }

        _pointerAwaySinceUtc ??= nowUtc;
        if (nowUtc - _pointerAwaySinceUtc < hideDelay) return TaskbarAutoHideAction.None;
        _pointerAwaySinceUtc = null;
        IsHidden = true;
        return TaskbarAutoHideAction.Hide;
    }

    public void Reset(bool hidden = false)
    {
        IsHidden = hidden;
        _pointerAwaySinceUtc = null;
    }
}

public sealed record TaskbarEdgePositions(Int32Rect Visible, Int32Rect Hidden);

public static class TaskbarEdgePositionCalculator
{
    public static TaskbarEdgePositions Calculate(Int32Rect visible, int revealThickness)
    {
        var reveal = Math.Clamp(revealThickness, 1, Math.Max(1, visible.Height));
        return new TaskbarEdgePositions(
            visible,
            new Int32Rect(visible.Left, visible.Top + visible.Height - reveal, visible.Width, visible.Height));
    }

    public static bool Contains(Int32Rect rect, int x, int y)
        => x >= rect.Left && x < (long)rect.Left + rect.Width
            && y >= rect.Top && y < (long)rect.Top + rect.Height;

    public static bool IsInRevealZone(Int32Rect visible, int revealThickness, int x, int y)
    {
        var reveal = Math.Clamp(revealThickness, 1, Math.Max(1, visible.Height));
        return x >= visible.Left && x < (long)visible.Left + visible.Width
            && y >= visible.Top + visible.Height - reveal
            && y < (long)visible.Top + visible.Height + reveal;
    }
}

public sealed record TaskbarWorkAreaReservationDecision(bool Reserve, string? FallbackReason = null);

/// <summary>
/// Keeps work-area changes conservative. A single secondary monitor can use
/// the documented AppBar negotiation safely. On current Windows builds,
/// several bottom AppBars can be assigned to the same monitor even when each
/// request uses a different monitor rectangle, so multi-secondary layouts use
/// overlay mode until that behaviour can be verified reliably.
/// </summary>
public static class TaskbarWorkAreaReservationPolicy
{
    public const string MultiMonitorFallbackReason =
        "检测到多个副屏；Windows Shell 无法保证逐屏 AppBar 预留，已安全回退为覆盖模式。";

    public static TaskbarWorkAreaReservationDecision Evaluate(
        TaskbarHostPreferences preferences,
        int secondaryMonitorCount)
    {
        var normalized = TaskbarHostPreferences.Normalize(preferences);
        if (normalized.AutoHide || !normalized.ReserveWorkArea || secondaryMonitorCount <= 0)
            return new(false);
        return secondaryMonitorCount == 1
            ? new(true)
            : new(false, MultiMonitorFallbackReason);
    }
}
