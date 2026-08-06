namespace SyncWallpaper.Update.Core;

public static class UpdateCheckScheduler
{
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromDays(7);

    public static bool ShouldRunAutomaticCheck(UpdateCheckSettings settings, DateTimeOffset nowUtc)
        => settings is not null && settings.ShouldRunAutomaticCheck(nowUtc, AutomaticInterval);

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
