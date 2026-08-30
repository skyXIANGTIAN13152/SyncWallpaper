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
            {"tag_name":"v1.0.1","name":"Release 1.0.1","body":"Fix\n<script>alert(1)</script>","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v1.0.1","draft":false,"prerelease":false,"published_at":"2026-08-01T00:00:00Z"}
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
            {"tag_name":"1.0.0","name":"same","body":"notes","html_url":"https://github.com/owner/syncwallpaper/releases/tag/1.0.0","draft":false,"prerelease":false}
            """);
        var equal = await new GitHubReleaseChecker(client, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.UpToDate, equal.Status);

        using var lowerClient = CreateClient("""
            {"tag_name":"0.9.9","name":"old","body":"notes","html_url":"https://github.com/owner/syncwallpaper/releases/tag/0.9.9","draft":false,"prerelease":false}
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
            {"tag_name":"v2.0.0-rc.1","name":"rc","html_url":"https://github.com/owner/syncwallpaper/releases/tag/v2.0.0-rc.1","draft":false,"prerelease":true}
            """);
        var result = await new GitHubReleaseChecker(client, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        Assert.AreEqual(UpdateCheckStatus.NoEligibleRelease, result.Status);
    }

    [TestMethod]
    public async Task InvalidReleaseUrlIsRejected()
    {
        using var client = CreateClient("""
            {"tag_name":"1.0.1","name":"bad","html_url":"http://evil.example/releases/1.0.1","draft":false,"prerelease":false}
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
            return JsonResponse("""{"tag_name":"1.0.1","html_url":"https://github.com/owner/syncwallpaper/releases/tag/1.0.1","draft":false,"prerelease":false}""");
        }));
        var checker = new GitHubReleaseChecker(client, Repository, "1.0.0");
        var first = checker.CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        var second = checker.CheckAsync(UpdateChannel.Stable, CancellationToken.None);
        var results = await Task.WhenAll(first, second);
        Assert.AreEqual(1, calls);
        Assert.IsTrue(results.All(x => x.Status == UpdateCheckStatus.UpdateAvailable));

        using var source = new CancellationTokenSource();
        source.Cancel();
        using var cancelledClient = CreateClient("""{"tag_name":"1.0.1","html_url":"https://github.com/owner/syncwallpaper/releases/tag/1.0.1","draft":false,"prerelease":false}""");
        var cancelled = await new GitHubReleaseChecker(cancelledClient, Repository, "1.0.0").CheckAsync(UpdateChannel.Stable, source.Token);
        Assert.AreEqual(UpdateCheckStatus.Cancelled, cancelled.Status);
    }

    [TestMethod]
    public void SemanticVersionAndWeeklyScheduleFollowRules()
    {
        Assert.IsTrue(SemanticVersion.TryParse("v1.2.3", out _));
        Assert.IsTrue(SemanticVersion.Parse("1.0.0") > SemanticVersion.Parse("1.0.0-rc.2"));
        Assert.IsTrue(SemanticVersion.Parse("1.0.0-rc.2") > SemanticVersion.Parse("1.0.0-beta.9"));
        Assert.IsTrue(SemanticVersion.Parse("2.0.0") > SemanticVersion.Parse("1.99.99"));
        var settings = new UpdateCheckSettings { AutomaticCheckEnabled = true, LastSuccessfulCheckUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z") };
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, DateTimeOffset.Parse("2026-01-07T23:59:00Z")));
        Assert.IsTrue(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, DateTimeOffset.Parse("2026-01-08T00:00:00Z")));
        Assert.IsFalse(UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, DateTimeOffset.Parse("2025-12-01T00:00:00Z")));
    }

    private static HttpClient CreateClient(string body, HttpStatusCode status = HttpStatusCode.OK, IReadOnlyDictionary<string, string>? headers = null)
        => new(new DelegateHandler((_, _) => Task.FromResult(JsonResponse(body, status, headers))));

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
