namespace SyncWallpaper.Update.Core;

public static class UpdateCheckScheduler
{
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromDays(1);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    public static bool ShouldRunAutomaticCheck(UpdateCheckSettings settings, DateTimeOffset nowUtc, TimeSpan? interval = null)
    {
        if (settings is null || !settings.AutomaticCheckEnabled) return false;

        // Check both timestamps: a more recent failed attempt must still throttle an
        // overdue successful check, and clock rollback must not cause request loops.
        if (settings.LastSuccessfulCheckUtc is { } success
            && (nowUtc < success || nowUtc - success < (interval ?? AutomaticInterval))) return false;
        if (settings.LastAttemptUtc is { } attempt
            && (nowUtc < attempt || nowUtc - attempt < RetryInterval)) return false;
        return true;
    }

    public static bool TryRecordAttempt(UpdateCheckSettings settings, DateTimeOffset nowUtc)
    {
        if (settings is null || !settings.AutomaticCheckEnabled) return false;
        if (!ShouldRunAutomaticCheck(settings, nowUtc)) return false;
        settings.LastAttemptUtc = nowUtc;
        return true;
    }

    public static void RecordResult(UpdateCheckSettings settings, UpdateCheckResult result, DateTimeOffset nowUtc)
    {
        settings.LastAttemptUtc = nowUtc;
        if (result.Status is UpdateCheckStatus.UpdateAvailable or UpdateCheckStatus.UpToDate or UpdateCheckStatus.NoEligibleRelease)
            settings.LastSuccessfulCheckUtc = nowUtc;
    }
}
