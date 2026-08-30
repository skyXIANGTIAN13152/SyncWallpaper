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
            return Result(UpdateCheckStatus.InvalidResponse, null, null, "The GitHub repository URL is not configured.", "ProjectLinks.GitHubOwner/GitHubRepository is empty.", started);
        if (!SemanticVersion.TryParse(_currentVersionText, out var current))
            return Result(UpdateCheckStatus.InvalidResponse, null, null, "The current version is invalid.", "The assembly version could not be parsed.", started);

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);
            var endpoint = channel == UpdateChannel.Beta ? _repository.ApiReleases : _repository.ApiLatestRelease;
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.UserAgent.ParseAdd($"SyncWallpaper/{_currentVersionText}");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result(UpdateCheckStatus.NotFound, current, null, "Unable to check for updates right now. Try again later.", "GitHub returned 404.", started);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var rateLimited = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
                    && values.FirstOrDefault() == "0";
                return Result(rateLimited ? UpdateCheckStatus.RateLimited : UpdateCheckStatus.Failed, current, null,
                    "Unable to check for updates right now. Try again later.", rateLimited ? "GitHub API rate limit reached." : "GitHub returned 403.", started);
            }
            if (!response.IsSuccessStatusCode)
                return Result(UpdateCheckStatus.Failed, current, null, "Unable to check for updates right now. Try again later.", $"GitHub HTTP {(int)response.StatusCode}.", started);

            var json = await ReadLimitedAsync(response, 2 * 1024 * 1024, timeoutSource.Token).ConfigureAwait(false);
            var parsedResponse = ParseReleases(json, channel);
            if (parsedResponse.InvalidUrl)
                return Result(UpdateCheckStatus.InvalidResponse, current, null, "Unable to check for updates right now. Try again later.", "Release html_url failed GitHub URL validation.", started);
            var candidate = ReleaseVersionComparer.SelectHighest(parsedResponse.Releases, channel);
            if (candidate is null)
                return Result(UpdateCheckStatus.NoEligibleRelease, current, null, "No release is available for the selected channel.", "No non-draft Release is available.", started);
            if (!SemanticVersion.TryParse(candidate.TagName, out var latest))
                return Result(UpdateCheckStatus.NoEligibleRelease, current, null, "No release is available for the selected channel.", "The Release tag is not valid SemVer.", started);
            if (!ReleaseUrlValidator.TryValidate(candidate.HtmlUrl.ToString(), _repository, out var releaseUrl))
                return Result(UpdateCheckStatus.InvalidResponse, current, latest, "Unable to check for updates right now. Try again later.", "Release html_url failed GitHub URL validation.", started);

            var status = latest! > current! ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate;
            var userMessage = status == UpdateCheckStatus.UpdateAvailable
                ? $"New version available: {latest}"
                : "You are up to date.";
            return new UpdateCheckResult(status, current!.ToNumericVersion(), latest!.ToNumericVersion(), candidate.Name,
                ReleaseNotesSanitizer.ToPlainText(candidate.Body), candidate.PublishedAt ?? candidate.CreatedAt, releaseUrl,
                userMessage, null)
            {
                CurrentSemanticVersion = current,
                LatestSemanticVersion = latest
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result(UpdateCheckStatus.Cancelled, current, null, "Update check cancelled.", "The caller cancelled the request.", started);
        }
        catch (OperationCanceledException)
        {
            return Result(UpdateCheckStatus.NetworkUnavailable, current, null, "Unable to check for updates right now. Try again later.", "The GitHub request timed out.", started);
        }
        catch (HttpRequestException ex)
        {
            return Result(UpdateCheckStatus.NetworkUnavailable, current, null, "Unable to check for updates right now. Try again later.", ex.GetType().Name + ": " + ex.Message, started);
        }
        catch (JsonException ex)
        {
            return Result(UpdateCheckStatus.InvalidResponse, current, null, "Unable to check for updates right now. Try again later.", "GitHub response was not valid JSON: " + ex.Message, started);
        }
        catch (InvalidDataException ex)
        {
            return Result(UpdateCheckStatus.InvalidResponse, current, null, "Unable to check for updates right now. Try again later.", ex.Message, started);
        }
        catch (Exception ex)
        {
            return Result(UpdateCheckStatus.Failed, current, null, "Unable to check for updates right now. Try again later.", ex.GetType().Name + ": " + ex.Message, started);
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

    private (IReadOnlyList<GitHubRelease> Releases, bool InvalidUrl) ParseReleases(string json, UpdateChannel channel)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var raw = channel == UpdateChannel.Beta
            ? JsonSerializer.Deserialize<List<GitHubReleaseResponse>>(json, options) ?? new List<GitHubReleaseResponse>()
            : new List<GitHubReleaseResponse> { JsonSerializer.Deserialize<GitHubReleaseResponse>(json, options) ?? new() };
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
            result.Add(new GitHubRelease(item.TagName.Trim(), item.Name, item.Body, url, item.Draft, item.Prerelease, item.PublishedAt, item.CreatedAt));
        }
        return (result, invalidUrl);
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, long maxBytes, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > maxBytes)
            throw new InvalidDataException("GitHub response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > maxBytes) throw new InvalidDataException("GitHub response is too large.");
            memory.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }
}
