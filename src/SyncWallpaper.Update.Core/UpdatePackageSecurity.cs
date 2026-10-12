namespace SyncWallpaper.Update.Core;

/// <summary>Constrain automated downloads to the chosen edition's official release asset.</summary>
public static class UpdatePackageSecurity
{
    public const long MaximumPackageBytes = 512L * 1024 * 1024;

    public static UpdatePackage? SelectPackage(GitHubRelease release, GitHubRepositorySettings repository)
    {
        if (!SemanticVersion.TryParse(release.TagName, out var semanticVersion)) return null;
        var version = semanticVersion!.ToString();
        var assetName = ExpectedAssetName(version);
        var matching = release.Assets.Where(asset => string.Equals(asset.Name, assetName, StringComparison.Ordinal)).ToArray();
        if (matching.Length != 1) return null;
        var asset = matching[0];
        if (asset.Size <= 0 || asset.Size > MaximumPackageBytes
            || !IsReleaseAssetUrl(asset.DownloadUrl, repository, version, assetName)) return null;

        var sha256 = ParseSha256Digest(asset.Digest);
        var checksumName = Path.ChangeExtension(assetName, ".sha256");
        var checksumAssets = release.Assets.Where(item => string.Equals(item.Name, checksumName, StringComparison.Ordinal)
            && item.Size is > 0 and <= 65536 && IsReleaseAssetUrl(item.DownloadUrl, repository, version, checksumName)).ToArray();
        var checksumUrl = checksumAssets.Length == 1 ? checksumAssets[0].DownloadUrl : null;
        if (sha256 is null && checksumUrl is null) return null;
        return new UpdatePackage(version, assetName, asset.DownloadUrl, asset.Size, sha256, checksumUrl);
    }

    public static string ExpectedAssetName(string version) => $"SyncWallpaper-{version}-win-x64-selfcontained.zip";

    public static string? ParseSha256Digest(string? digest)
        => digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
            ? NormalizeSha256(digest[7..]) : null;

    public static string? NormalizeSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;

    public static bool IsReleaseAssetUrl(Uri? uri, GitHubRepositorySettings repository, string version, string assetName)
    {
        if (!repository.IsConfigured || !IsSecureUrl(uri) || !string.Equals(uri!.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length != 0 || !SemanticVersion.TryParse(version, out var parsed)
            || !string.Equals(parsed!.ToString(), version, StringComparison.Ordinal)) return false;
        var path = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
        var prefix = $"{repository.Owner}/{repository.Repository}/releases/download/";
        return new[] { version, "v" + version, "V" + version }.Any(tag =>
            string.Equals(path, prefix + tag + "/" + assetName, StringComparison.Ordinal));
    }

    internal static bool IsAllowedRedirect(Uri uri, Uri original)
    {
        if (!IsSecureUrl(uri)) return false;
        if (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return uri.Equals(original);
        return string.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSecureUrl(Uri? uri) => uri is { IsAbsoluteUri: true }
        && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
}
