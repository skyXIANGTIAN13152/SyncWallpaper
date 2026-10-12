using System.Net;
using System.Text;
using SyncWallpaper.Update.Core;

namespace SyncWallpaper.Update.Tests;

[TestClass]
public sealed class GitHubReleaseCheckerTests
{
    private static GitHubRepositorySettings Repository => new("owner", "syncwallpaper");

    [TestMethod]
    public async Task StableReleaseHigherThanCurrentIsAvailable()
    {
        using var client = CreateClient("""
            [{"tag_name":"v1.0.1","name":"Release 1.0.1","body":"修复\n<script>alert(1)</script>","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v1.0.1","draft":false,"prerelease":false,"published_at":"2026-08-01T00:00:00Z"}]
            """);
        var result = await new GitHubReleaseChecker(client, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.AreEqual("1.0.1", result.LatestSemanticVersion?.ToString());
        StringAssert.Contains(result.ReleaseNotes!, "<script>");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ReleasePageUrl?.ToString()));
    }

    [TestMethod]
    public async Task SameOrLowerReleaseIsUpToDate()
    {
        using var client = CreateClient("""
            [{"tag_name":"1.0.0","name":"same","body":"notes","html_url":"https://github.com/owner/syncwallpaper/releases/tag/1.0.0","draft":false,"prerelease":false}]
            """);
        var equal = await new GitHubReleaseChecker(client, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.UpToDate, equal.Status);

        using var lowerClient = CreateClient("""
            [{"tag_name":"0.9.9","name":"old","body":"notes","html_url":"https://github.com/owner/syncwallpaper/releases/tag/0.9.9","draft":false,"prerelease":false}]
            """);
        var lower = await new GitHubReleaseChecker(lowerClient, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.UpToDate, lower.Status);
    }

    [TestMethod]
    public async Task BetaSelectsHighestEligibleReleaseAndIgnoresDraft()
    {
        using var client = CreateClient("""
            [
              {"tag_name":"v1.1.0-beta.1","name":"beta","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v1.1.0-beta.1","draft":false,"prerelease":true},
              {"tag_name":"v1.0.5","name":"stable","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v1.0.5","draft":false,"prerelease":false},
              {"tag_name":"v9.0.0","name":"draft","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v9.0.0","draft":true,"prerelease":false},
              {"tag_name":"not-a-version","name":"bad","html_url":"https://github.com/owner/syncwallpaper/releases/tag/not-a-version","draft":false,"prerelease":true}
            ]
            """);
        var result = await new GitHubReleaseChecker(client, Repository, "1.0.0").CheckAsync(UpdateChannel.Beta, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.AreEqual("1.1.0-beta.1", result.LatestSemanticVersion?.ToString());
    }

    [TestMethod]
    public async Task DraftAndStablePrereleaseAreNoEligible()
    {
        using var client = CreateClient("""
            [{"tag_name":"v2.0.0-rc.1","name":"rc","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v2.0.0-rc.1","draft":false,"prerelease":true}]
            """);
        var result = await new GitHubReleaseChecker(client, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.NoEligibleRelease, result.Status);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("当前暂无稳定版发布。", result.UserMessage);
    }

    [TestMethod]
    public async Task InvalidReleaseUrlIsRejected()
    {
        using var client = CreateClient("""
            [{"tag_name":"1.0.1","name":"bad","html_url":"http://evil.example/releases/1.0.1","draft":false,"prerelease":false}]
            """);
        var result = await new GitHubReleaseChecker(client, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.InvalidResponse, result.Status);
        Assert.IsFalse(ReleaseUrlValidator.IsAllowed(new Uri("http://github.com/owner/syncwallpaper/releases/tag/1"), Repository));
        Assert.IsFalse(ReleaseUrlValidator.IsAllowed(new Uri("https://github.com/other/syncwallpaper/releases/tag/1"), Repository));
    }

    [TestMethod]
    public async Task HttpErrorsAndMalformedJsonAreSafe()
    {
        using var notFound = CreateClient(string.Empty, HttpStatusCode.NotFound);
        Assert.AreEqual(UpdateCheckStatus.NotFound, (await new GitHubReleaseChecker(notFound, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, CancellationToken.None)).Status);

        using var limited = CreateClient(string.Empty, HttpStatusCode.Forbidden, new Dictionary<string, string> { ["X-RateLimit-Remaining"] = "0" });
        Assert.AreEqual(UpdateCheckStatus.RateLimited, (await new GitHubReleaseChecker(limited, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, CancellationToken.None)).Status);

        using var malformed = CreateClient("{broken");
        Assert.AreEqual(UpdateCheckStatus.InvalidResponse, (await new GitHubReleaseChecker(malformed, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, CancellationToken.None)).Status);
    }

    [TestMethod]
    public async Task OversizedResponseIsRejected()
    {
        using var client = CreateClient(new string('x', 2 * 1024 * 1024 + 1));
        var result = await new GitHubReleaseChecker(client, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.InvalidResponse, result.Status);
    }

    [TestMethod]
    public async Task CancellationAndConcurrentChecksAreSafe()
    {
        var calls = 0;
        using var client = new HttpClient(new DelegateHandler(async (_, token) =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50, token);
            return JsonResponse("""[{"tag_name":"1.0.1","html_url":"https://github.com/owner/syncwallpaper/releases/tag/1.0.1","draft":false,"prerelease":false}]""");
        }));
        var checker = new GitHubReleaseChecker(client, Repository, "1.0.0");
        var first = checker.CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        var second = checker.CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        var results = await Task.WhenAll(first, second);
        Assert.AreEqual(1, calls);
        Assert.IsTrue(results.All(x => x.Status == UpdateCheckStatus.UpdateAvailable));

        using var source = new CancellationTokenSource();
        source.Cancel();
        using var cancelledClient = CreateClient("""[{"tag_name":"1.0.1","html_url":"https://github.com/owner/syncwallpaper/releases/tag/1.0.1","draft":false,"prerelease":false}]""");
        var cancelled = await new GitHubReleaseChecker(cancelledClient, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, source.Token);
        Assert.AreEqual(UpdateCheckStatus.Cancelled, cancelled.Status);
    }

    [TestMethod]
    public void SemanticVersionAndDailyScheduleFollowRules()
    {
        Assert.IsTrue(SemanticVersion.TryParse("v1.2.3", out _));
        Assert.IsTrue(SemanticVersion.Parse("1.0.0") > SemanticVersion.Parse("1.0.0-rc.2"));
        Assert.IsTrue(SemanticVersion.Parse("1.0.0-rc.2") > SemanticVersion.Parse("1.0.0-beta.9"));
        Assert.IsTrue(SemanticVersion.Parse("2.0.0") > SemanticVersion.Parse("1.99.99"));
        var settings = new UpdateCheckSettings { AutomaticCheckEnabled = true, LastSuccessfulCheckUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z") };
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, DateTimeOffset.Parse("2026-01-01T23:59:00Z")));
        Assert.IsTrue(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, DateTimeOffset.Parse("2026-01-02T00:00:00Z")));
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, DateTimeOffset.Parse("2025-12-01T00:00:00Z")));
    }

    [DataTestMethod]
    [DataRow("1.1.0-beta.1", "1.1.0-beta.2")]
    [DataRow("1.1.0-beta.1-en", "1.1.0-beta.2-en")]
    [DataRow("1.1.0-beta.1-en+commit123", "1.1.0-beta.2-en")]
    public async Task ParallelEditionsOnlyOfferTheirOwnLanguage(string current, string expected)
    {
        using var client = CreateClient("""
            [
              {"tag_name":"v1.1.0-beta.2","name":"Chinese","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v1.1.0-beta.2","draft":false,"prerelease":true},
              {"tag_name":"v1.1.0-beta.2-en","name":"English","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v1.1.0-beta.2-en","draft":false,"prerelease":true}
            ]
            """);
        var result = await new GitHubReleaseChecker(client, Repository, current).CheckAsync(UpdateChannel.Beta, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.AreEqual(expected, result.LatestSemanticVersion?.ToString());
    }

    private static HttpClient CreateClient(string body, HttpStatusCode status = HttpStatusCode.OK, IReadOnlyDictionary<string, string>? headers = null)
        => new(new DelegateHandler((_, _) => Task.FromResult(JsonResponse(body, status, headers))));

    [DataTestMethod]
    [DataRow(UpdateChannel.Stable, "当前暂无稳定版发布。")]
    [DataRow(UpdateChannel.Beta, "当前暂无测试版发布。")]
    public async Task BothChannelsReadReleaseListAndEmptyChannelIsSuccessful(UpdateChannel channel, string expectedMessage)
    {
        Uri? requested = null;
        using var client = new HttpClient(new DelegateHandler((request, _) =>
        {
            requested = request.RequestUri;
            return Task.FromResult(requested == Repository.ApiReleases
                ? JsonResponse("[]") : JsonResponse("", HttpStatusCode.NotFound));
        }));
        var result = await new GitHubReleaseChecker(client, Repository, "1.0.0").CheckAsync(channel, CancellationToken.None);
        Assert.AreEqual(Repository.ApiReleases, requested);
        Assert.AreEqual(UpdateCheckStatus.NoEligibleRelease, result.Status);
        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.AreEqual(expectedMessage, result.UserMessage);
    }

    [DataTestMethod]
    [DataRow("1.0.0", "1.1.0")]
    [DataRow("1.0.0-en", "1.2.0-en")]
    public async Task StableSelectsOwnLanguageFromMixedReleaseList(string current, string expected)
    {
        using var client = CreateClient("""
            [
              {"tag_name":"v2.0.0-beta.1","name":"New Chinese beta","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v2.0.0-beta.1","draft":false,"prerelease":true},
              {"tag_name":"v1.2.0-en","name":"Latest English stable","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v1.2.0-en","draft":false,"prerelease":false},
              {"tag_name":"v1.1.0","name":"Chinese stable","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v1.1.0","draft":false,"prerelease":false},
              {"tag_name":"v3.0.0","name":"Draft stable","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v3.0.0","draft":true,"prerelease":false}
            ]
            """);
        var result = await new GitHubReleaseChecker(client, Repository, current).CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.AreEqual(expected, result.LatestSemanticVersion!.ToString());
    }

    [DataTestMethod]
    [DataRow("1.2.0-beta.8", "1.2.0-beta.9")]
    [DataRow("1.2.0-beta.8-en", "1.2.0-beta.9-en")]
    public async Task UpdateSelectsMatchingSelfContainedAssetWithDigest(string current, string latest)
    {
        var name = UpdatePackageSecurity.ExpectedAssetName(latest);
        using var client = CreateClient($$"""
            [{"tag_name":"v{{latest}}","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v{{latest}}","prerelease":true,"assets":[
              {"name":"SyncWallpaper-{{latest}}-win-x64.zip","browser_download_url":"https://github.com/owner/syncwallpaper/releases/download/v{{latest}}/SyncWallpaper-{{latest}}-win-x64.zip","size":500,"digest":"sha256:{{new string('a', 64)}}"},
              {"name":"{{name}}","browser_download_url":"https://github.com/owner/syncwallpaper/releases/download/v{{latest}}/{{name}}","size":123456,"digest":"sha256:{{new string('b', 64)}}"}
            ]}]
            """);
        var result = await new GitHubReleaseChecker(client, Repository, current).CheckAsync(UpdateChannel.Beta, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.IsNotNull(result.Package);
        Assert.AreEqual(name, result.Package.AssetName);
        Assert.AreEqual(latest, result.Package.Version);
        Assert.AreEqual(new string('b', 64), result.Package.Sha256);
        Assert.AreEqual(123456L, result.Package.Size);
    }

    [TestMethod]
    public async Task ReleaseChecksumAssetEnablesInstallWithoutApiDigest()
    {
        const string version = "1.2.0-beta.9";
        var name = UpdatePackageSecurity.ExpectedAssetName(version);
        var checksumName = Path.ChangeExtension(name, ".sha256");
        using var client = CreateClient($$"""
            [{"tag_name":"v{{version}}","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v{{version}}","prerelease":true,"assets":[
              {"name":"{{name}}","browser_download_url":"https://github.com/owner/syncwallpaper/releases/download/v{{version}}/{{name}}","size":123456},
              {"name":"{{checksumName}}","browser_download_url":"https://github.com/owner/syncwallpaper/releases/download/v{{version}}/{{checksumName}}","size":150}
            ]}]
            """);
        var result = await new GitHubReleaseChecker(client, Repository, "1.2.0-beta.8").CheckAsync(UpdateChannel.Beta, CancellationToken.None);
        Assert.IsNotNull(result.Package);
        Assert.IsNull(result.Package.Sha256);
        Assert.AreEqual(checksumName, Path.GetFileName(result.Package.ChecksumUrl!.AbsolutePath));
    }

    [TestMethod]
    public async Task ReleaseWithoutInstallableAssetStillOffersReleaseInformation()
    {
        const string version = "1.2.0-beta.9";
        var name = UpdatePackageSecurity.ExpectedAssetName(version);
        var releasePage = "https://github.com/owner/syncwallpaper/releases/tag/v" + version;
        var variants = new[]
        {
            new GitHubReleaseAsset(name, new Uri($"https://github.com/owner/syncwallpaper/releases/download/v{version}/{name}"), 100, null),
            new GitHubReleaseAsset(name, new Uri($"https://github.com/other/repo/releases/download/v{version}/{name}"), 100, "sha256:" + new string('a', 64)),
            new GitHubReleaseAsset(name, new Uri($"https://github.com/owner/syncwallpaper/releases/download/v{version}/{name}"), 0, "sha256:" + new string('a', 64)),
            new GitHubReleaseAsset(name.Replace("win-x64", "win-arm64"), new Uri($"https://github.com/owner/syncwallpaper/releases/download/v{version}/{name}"), 100, "sha256:" + new string('a', 64))
        };
        foreach (var asset in variants)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new { tag_name = "v" + version, html_url = releasePage, prerelease = true,
                    assets = new[] { new { name = asset.Name, browser_download_url = asset.DownloadUrl, size = asset.Size, digest = asset.Digest } } }
            });
            using var client = CreateClient(json);
            var result = await new GitHubReleaseChecker(client, Repository, "1.2.0-beta.8").CheckAsync(UpdateChannel.Beta, CancellationToken.None);
            Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
            Assert.IsNull(result.Package);
            Assert.AreEqual(releasePage, result.ReleasePageUrl!.ToString());
        }
    }

    [TestMethod]
    public void DuplicateNamedAssetsAreNotAmbiguouslyInstalled()
    {
        const string version = "1.2.0-beta.9";
        var name = UpdatePackageSecurity.ExpectedAssetName(version);
        var asset = new GitHubReleaseAsset(name, new Uri($"https://github.com/owner/syncwallpaper/releases/download/v{version}/{name}"), 100, "sha256:" + new string('a', 64));
        var release = new GitHubRelease("v" + version, null, null,
            new Uri("https://github.com/owner/syncwallpaper/releases/tag/v" + version), false, true, null, null) { Assets = new[] { asset, asset } };
        Assert.IsNull(UpdatePackageSecurity.SelectPackage(release, Repository));
    }

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK, IReadOnlyDictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (headers is not null)
            foreach (var pair in headers) response.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        return response;
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _handler(request, cancellationToken);
    }
}
