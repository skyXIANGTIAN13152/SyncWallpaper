using System.Text.Json.Serialization;

namespace SyncWallpaper.Update.Core;

/// <summary>Which releases a user has explicitly opted into.</summary>
public enum UpdateChannel
{
    Stable,
    Beta
}

public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    NoEligibleRelease,
    NetworkUnavailable,
    RateLimited,
    InvalidResponse,
    NotFound,
    Cancelled,
    Failed
}

public sealed class UpdateCheckSettings
{
    public bool AutomaticCheckEnabled { get; set; }
    public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;
    public DateTimeOffset? LastSuccessfulCheckUtc { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public string? ETag { get; set; }

    public bool ShouldRunAutomaticCheck(DateTimeOffset nowUtc, TimeSpan? interval = null)
        => UpdateCheckScheduler.ShouldRunAutomaticCheck(this, nowUtc, interval);
}

public sealed class GitHubRepositorySettings
{
    public GitHubRepositorySettings(string owner, string repository)
    {
        Owner = owner?.Trim() ?? string.Empty;
        Repository = repository?.Trim() ?? string.Empty;
    }

    public string Owner { get; }
    public string Repository { get; }
    public bool IsConfigured => IsSafeSegment(Owner) && IsSafeSegment(Repository);

    public Uri ApiLatestRelease => new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");
    public Uri ApiReleases => new($"https://api.github.com/repos/{Owner}/{Repository}/releases?per_page=100");
    public Uri ReleasesPage => new($"https://github.com/{Owner}/{Repository}/releases");
    public Uri LatestReleasePage => new($"https://github.com/{Owner}/{Repository}/releases/latest");

    private static bool IsSafeSegment(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 100
        && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
}

/// <summary>Central links for the public SyncWallpaper repository.</summary>
public static class ProjectLinks
{
    public const string GitHubOwner = "skyXIANGTIAN13152";
    public const string GitHubRepository = "SyncWallpaper";
    public static bool IsConfigured => new GitHubRepositorySettings(GitHubOwner, GitHubRepository).IsConfigured;
    public static GitHubRepositorySettings RepositorySettings => new(GitHubOwner, GitHubRepository);
    public static Uri? Repository => IsConfigured ? new Uri($"https://github.com/{GitHubOwner}/{GitHubRepository}") : null;
    public static Uri? Releases => IsConfigured ? RepositorySettings.ReleasesPage : null;
    public static Uri? LatestRelease => IsConfigured ? RepositorySettings.LatestReleasePage : null;
}

public interface IReleaseUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(UpdateChannel channel, CancellationToken cancellationToken);
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version? CurrentVersion,
    Version? LatestVersion,
    string? ReleaseName,
    string? ReleaseNotes,
    DateTimeOffset? PublishedAt,
    Uri? ReleasePageUrl,
    string? UserMessage,
    string? TechnicalMessage)
{
    public SemanticVersion? CurrentSemanticVersion { get; init; }
    public SemanticVersion? LatestSemanticVersion { get; init; }
    public UpdatePackage? Package { get; init; }
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable;
    public bool IsSuccess => Status is UpdateCheckStatus.UpdateAvailable or UpdateCheckStatus.UpToDate or UpdateCheckStatus.NoEligibleRelease;
}

public sealed record GitHubRelease(
    string TagName,
    string? Name,
    string? Body,
    Uri HtmlUrl,
    bool Draft,
    bool Prerelease,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? CreatedAt)
{
    public IReadOnlyList<GitHubReleaseAsset> Assets { get; init; } = Array.Empty<GitHubReleaseAsset>();
}

public sealed record GitHubReleaseAsset(string Name, Uri DownloadUrl, long Size, string? Digest);

public sealed record UpdatePackage(string Version, string AssetName, Uri DownloadUrl, long Size, string? Sha256, Uri? ChecksumUrl);

public sealed record PreparedUpdate(string WorkDirectory, string PackageDirectory, string Version);

public sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes, string Stage)
{
    public double? Percent => TotalBytes is > 0 ? Math.Clamp(BytesReceived * 100d / TotalBytes.Value, 0, 100) : null;
}

public sealed class GitHubReleaseResponse
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("assets")] public List<GitHubReleaseAssetResponse>? Assets { get; set; }
}

public sealed class GitHubReleaseAssetResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("digest")] public string? Digest { get; set; }
}

public sealed class UpdateCheckCache
{
    public int SchemaVersion { get; set; } = 1;
    public string? ETag { get; set; }
    public DateTimeOffset? CheckedAtUtc { get; set; }
    public UpdateCheckResult? Result { get; set; }
}
