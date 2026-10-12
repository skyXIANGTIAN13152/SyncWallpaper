namespace SyncWallpaper.Update.Core;

public static class UpdatePromptPolicy
{
    public static bool ShouldPrompt(string version, bool manual, string? lastVersion,
        DateTimeOffset? lastPromptUtc, DateTimeOffset nowUtc)
        => manual || !string.Equals(version, lastVersion, StringComparison.Ordinal)
            || lastPromptUtc is null || nowUtc - lastPromptUtc.Value >= TimeSpan.FromDays(1);
}
