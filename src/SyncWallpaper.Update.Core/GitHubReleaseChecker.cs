using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SyncWallpaper.Update.Core;

public sealed class GitHubReleaseChecker : IReleaseUpdateChecker
{
    private readonly HttpClient _httpClient;
    private readonly GitHubRepositorySettings _repository;
    private readonly string _currentVersionText;
    private readonly TimeSpan _timeout;
    private readonly object _gate = new();
    private Task<UpdateCheckResult>? _inFlight;

    public GitHubReleaseChecker(
        HttpClient httpClient,
        GitHubRepositorySettings repository,
        string currentVersion,
        TimeSpan? timeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _currentVersionText = currentVersion ?? string.Empty;
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
    }

    public Task<UpdateCheckResult> CheckAsync(UpdateChannel channel, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_inFlight is { IsCompleted: false }) return _inFlight;
            _inFlight = CheckCoreAsync(channel, cancellationToken);
            _ = _inFlight.ContinueWith(_ => { lock (_gate) _inFlight = null; }, TaskScheduler.Default);
            return _inFlight;
        }
    }

    private async Task<UpdateCheckResult> CheckCoreAsync(UpdateChannel channel, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        if (!_repository.IsConfigured)
            return Result(UpdateCheckStatus.InvalidResponse, null, null, "尚未配置 GitHub 仓库地址。", "ProjectLinks.GitHubOwner/GitHubRepository 为空。", started);
        if (!SemanticVersion.TryParse(_currentVersionText, out var current))
            return Result(UpdateCheckStatus.InvalidResponse, null, null, "当前版本号无效。", "无法解析程序集版本。", started);

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);
            // /latest is 404 when a repository only has prereleases, and it may
            // point at a different language edition. Filter the release list locally.
            using var request = new HttpRequestMessage(HttpMethod.Get, _repository.ApiReleases);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.UserAgent.ParseAdd($"SyncWallpaper/{_currentVersionText}");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result(UpdateCheckStatus.NotFound, current, null, "暂时无法检查更新，请稍后重试。", "GitHub 返回 404。", started);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var rateLimited = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
                    && values.FirstOrDefault() == "0";
                return Result(rateLimited ? UpdateCheckStatus.RateLimited : UpdateCheckStatus.Failed, current, null,
                    "暂时无法检查更新，请稍后重试。", rateLimited ? "GitHub API 已限流。" : "GitHub 返回 403。", started);
            }
            if (!response.IsSuccessStatusCode)
                return Result(UpdateCheckStatus.Failed, current, null, "暂时无法检查更新，请稍后重试。", $"GitHub HTTP {(int)response.StatusCode}。", started);

            var json = await ReadLimitedAsync(response, 2 * 1024 * 1024, timeoutSource.Token).ConfigureAwait(false);
            var parsedResponse = ParseReleases(json);
            if (parsedResponse.InvalidUrl)
                return Result(UpdateCheckStatus.InvalidResponse, current, null, "暂时无法检查更新，请稍后重试。", "Release html_url 未通过 GitHub URL 校验。", started);
            // Language suffixes are separate distributions, not newer feature builds.
            // Never send the Chinese edition to an English-only package or vice versa.
            var englishEdition = IsEnglishEdition(_currentVersionText);
            var candidate = ReleaseVersionComparer.SelectHighest(
                parsedResponse.Releases.Where(release => IsEnglishEdition(release.TagName) == englishEdition), channel);
            var noReleaseMessage = channel == UpdateChannel.Stable ? "当前暂无稳定版发布。" : "当前暂无测试版发布。";
            if (candidate is null)
                return Result(UpdateCheckStatus.NoEligibleRelease, current, null, noReleaseMessage, "当前语言和更新渠道没有可用的非草稿 Release。", started);
            if (!SemanticVersion.TryParse(candidate.TagName, out var latest))
                return Result(UpdateCheckStatus.NoEligibleRelease, current, null, noReleaseMessage, "Release tag 不是有效的 SemVer。", started);
            if (!ReleaseUrlValidator.TryValidate(candidate.HtmlUrl.ToString(), _repository, out var releaseUrl))
                return Result(UpdateCheckStatus.InvalidResponse, current, latest, "暂时无法检查更新，请稍后重试。", "Release html_url 未通过 GitHub URL 校验。", started);

            var status = latest! > current! ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate;
            var userMessage = status == UpdateCheckStatus.UpdateAvailable
                ? $"发现新版本：{latest}"
                : "当前已是最新版本。";
            return new UpdateCheckResult(status, current!.ToNumericVersion(), latest!.ToNumericVersion(), candidate.Name,
                ReleaseNotesSanitizer.ToPlainText(candidate.Body), candidate.PublishedAt ?? candidate.CreatedAt, releaseUrl,
                userMessage, null)
            {
                CurrentSemanticVersion = current,
                LatestSemanticVersion = latest,
                Package = UpdatePackageSecurity.SelectPackage(candidate, _repository)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result(UpdateCheckStatus.Cancelled, current, null, "已取消更新检查。", "调用方取消了请求。", started);
        }
        catch (OperationCanceledException)
        {
            return Result(UpdateCheckStatus.NetworkUnavailable, current, null, "暂时无法检查更新，请稍后重试。", "GitHub 请求超时。", started);
        }
        catch (HttpRequestException ex)
        {
            return Result(UpdateCheckStatus.NetworkUnavailable, current, null, "暂时无法检查更新，请稍后重试。", ex.GetType().Name + "：" + ex.Message, started);
        }
        catch (JsonException ex)
        {
            return Result(UpdateCheckStatus.InvalidResponse, current, null, "暂时无法检查更新，请稍后重试。", "GitHub 响应不是有效 JSON：" + ex.Message, started);
        }
        catch (InvalidDataException ex)
        {
            return Result(UpdateCheckStatus.InvalidResponse, current, null, "暂时无法检查更新，请稍后重试。", ex.Message, started);
        }
        catch (Exception ex)
        {
            return Result(UpdateCheckStatus.Failed, current, null, "暂时无法检查更新，请稍后重试。", ex.GetType().Name + "：" + ex.Message, started);
        }
    }

    private UpdateCheckResult Result(UpdateCheckStatus status, SemanticVersion? current, SemanticVersion? latest,
        string userMessage, string technicalMessage, DateTimeOffset started)
        => new(status, current?.ToNumericVersion(), latest?.ToNumericVersion(), null, null, null, null,
            userMessage, technicalMessage)
        {
            CurrentSemanticVersion = current,
            LatestSemanticVersion = latest
        };

    private static bool IsEnglishEdition(string version)
        => version.Split('+', 2)[0].Trim().EndsWith("-en", StringComparison.OrdinalIgnoreCase);

    private (IReadOnlyList<GitHubRelease> Releases, bool InvalidUrl) ParseReleases(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var raw = JsonSerializer.Deserialize<List<GitHubReleaseResponse>>(json, options) ?? new List<GitHubReleaseResponse>();
        var result = new List<GitHubRelease>();
        var invalidUrl = false;
        foreach (var item in raw)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.TagName) || string.IsNullOrWhiteSpace(item.HtmlUrl)) continue;
            if (!Uri.TryCreate(item.HtmlUrl, UriKind.Absolute, out var url)
                || !ReleaseUrlValidator.IsAllowed(url, _repository))
            {
                invalidUrl = true;
                continue;
            }
            var assets = (item.Assets ?? new()).Where(asset => asset is not null && !string.IsNullOrWhiteSpace(asset.Name))
                .Select(asset => Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUrl)
                    ? new GitHubReleaseAsset(asset.Name!, downloadUrl, asset.Size, asset.Digest) : null)
                .OfType<GitHubReleaseAsset>().ToArray();
            result.Add(new GitHubRelease(item.TagName.Trim(), item.Name, item.Body, url, item.Draft, item.Prerelease, item.PublishedAt, item.CreatedAt)
            {
                Assets = assets
            });
        }
        return (result, invalidUrl);
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, long maxBytes, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > maxBytes)
            throw new InvalidDataException("GitHub 响应过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > maxBytes) throw new InvalidDataException("GitHub 响应过大。");
            memory.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }
}
