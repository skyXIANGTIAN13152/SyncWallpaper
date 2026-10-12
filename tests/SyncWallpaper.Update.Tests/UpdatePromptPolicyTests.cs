using SyncWallpaper.Update.Core;

namespace SyncWallpaper.Update.Tests;

[TestClass]
public class UpdatePromptPolicyTests
{
    [TestMethod]
    public void SameReleaseWaitsUntilTomorrowButManualCheckCanRetry()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.IsFalse(UpdatePromptPolicy.ShouldPrompt("1.3.0", false, "1.3.0", now.AddHours(-2), now));
        Assert.IsTrue(UpdatePromptPolicy.ShouldPrompt("1.3.0", true, "1.3.0", now.AddHours(-2), now));
        Assert.IsTrue(UpdatePromptPolicy.ShouldPrompt("1.3.0", false, "1.3.0", now.AddDays(-1), now));
    }

    [TestMethod]
    public void NewReleaseCanPromptAndClockRollbackDoesNotRepeatOldPrompt()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.IsTrue(UpdatePromptPolicy.ShouldPrompt("1.3.1", false, "1.3.0", now, now));
        Assert.IsFalse(UpdatePromptPolicy.ShouldPrompt("1.3.0", false, "1.3.0", now.AddHours(1), now));
        Assert.IsTrue(UpdatePromptPolicy.ShouldPrompt("1.3.0", false, null, null, now));
    }
}
