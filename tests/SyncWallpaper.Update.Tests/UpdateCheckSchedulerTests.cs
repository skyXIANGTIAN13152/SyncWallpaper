using SyncWallpaper.Update.Core;

namespace SyncWallpaper.Update.Tests;

[TestClass]
public sealed class UpdateCheckSchedulerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T12:00:00Z");

    [TestMethod]
    public void NewEnabledSettingsAreDueButDisabledSettingsNeverAre()
    {
        var settings = new UpdateCheckSettings { AutomaticCheckEnabled = true };
        Assert.IsTrue(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now));
        settings.AutomaticCheckEnabled = false;
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now));
        Assert.IsFalse(UpdateCheckScheduler.TryRecordAttempt(settings, Now));
        Assert.IsNull(settings.LastAttemptUtc);
    }

    [TestMethod]
    public void SuccessfulChecksBecomeDueAtExactlyTwentyFourHours()
    {
        var settings = Enabled(Now.AddDays(-1), Now.AddDays(-1));
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now.AddTicks(-1)));
        Assert.IsTrue(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now));
        Assert.IsTrue(settings.ShouldRunAutomaticCheck(Now));
    }

    [TestMethod]
    public void FirstFailedCheckCanRetryAfterOneHour()
    {
        var settings = Enabled(null, Now.AddHours(-1));
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now.AddTicks(-1)));
        Assert.IsTrue(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now));
    }

    [TestMethod]
    public void RecentFailedAttemptThrottlesAnOverdueSuccessfulCheck()
    {
        var settings = Enabled(Now.AddDays(-3), Now.AddMinutes(-59));
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now));
        Assert.IsTrue(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now.AddMinutes(1)));
    }

    [TestMethod]
    public void FailedManualCheckDoesNotShortenTheSuccessfulCheckInterval()
    {
        var settings = Enabled(Now.AddHours(-12), Now.AddHours(-2));
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now));
        Assert.IsTrue(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now.AddHours(12)));
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void FutureTimestampsBlockChecksUntilTheClockCatchesUp(bool futureSuccess)
    {
        var settings = futureSuccess
            ? Enabled(Now.AddHours(1), Now.AddDays(-2))
            : Enabled(Now.AddDays(-2), Now.AddHours(1));
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now));
        Assert.IsFalse(UpdateCheckScheduler.TryRecordAttempt(settings, Now));
    }

    [TestMethod]
    public void RecordingAttemptPreventsTheNextPollFromStartingAnotherCheck()
    {
        var settings = Enabled(Now.AddDays(-2), null);
        Assert.IsTrue(UpdateCheckScheduler.TryRecordAttempt(settings, Now));
        Assert.AreEqual(Now, settings.LastAttemptUtc);
        Assert.IsFalse(UpdateCheckScheduler.TryRecordAttempt(settings, Now.AddMinutes(59)));
        Assert.AreEqual(Now, settings.LastAttemptUtc);
        Assert.IsTrue(UpdateCheckScheduler.TryRecordAttempt(settings, Now.AddHours(1)));
    }

    [DataTestMethod]
    [DataRow(UpdateCheckStatus.UpdateAvailable)]
    [DataRow(UpdateCheckStatus.UpToDate)]
    [DataRow(UpdateCheckStatus.NoEligibleRelease)]
    public void SuccessfulResultsSetTheDailySchedule(UpdateCheckStatus status)
    {
        var settings = Enabled(null, null);
        UpdateCheckScheduler.RecordResult(settings, Result(status), Now);
        Assert.AreEqual(Now, settings.LastAttemptUtc);
        Assert.AreEqual(Now, settings.LastSuccessfulCheckUtc);
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now.AddHours(23)));
        Assert.IsTrue(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now.AddHours(24)));
    }

    [DataTestMethod]
    [DataRow(UpdateCheckStatus.NetworkUnavailable)]
    [DataRow(UpdateCheckStatus.RateLimited)]
    [DataRow(UpdateCheckStatus.InvalidResponse)]
    [DataRow(UpdateCheckStatus.NotFound)]
    [DataRow(UpdateCheckStatus.Cancelled)]
    [DataRow(UpdateCheckStatus.Failed)]
    public void FailedResultsRetainTheLastSuccessAndUseHourlyRetry(UpdateCheckStatus status)
    {
        var lastSuccess = Now.AddDays(-2);
        var settings = Enabled(lastSuccess, lastSuccess);
        UpdateCheckScheduler.RecordResult(settings, Result(status), Now);
        Assert.AreEqual(Now, settings.LastAttemptUtc);
        Assert.AreEqual(lastSuccess, settings.LastSuccessfulCheckUtc);
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now.AddMinutes(59)));
        Assert.IsTrue(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, Now.AddHours(1)));
    }

    private static UpdateCheckSettings Enabled(DateTimeOffset? success, DateTimeOffset? attempt) => new()
    {
        AutomaticCheckEnabled = true,
        LastSuccessfulCheckUtc = success,
        LastAttemptUtc = attempt
    };

    private static UpdateCheckResult Result(UpdateCheckStatus status)
        => new(status, null, null, null, null, null, null, null, null);
}
