using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SyncWallpaper.Update.Core;

/// <summary>Downloads a consented release into a fresh staging directory, without touching the installed app.</summary>
public sealed class UpdatePackageDownloader
{
    private const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const long MaximumEntryBytes = 512L * 1024 * 1024;
    private const int MaximumEntries = 20000;
    private readonly HttpClient _httpClient;
    private readonly GitHubRepositorySettings _repository;

    /// <param name="httpClient">Use a client with AllowAutoRedirect=false so each redirect is checked before following it.</param>
    public UpdatePackageDownloader(HttpClient httpClient, GitHubRepositorySettings repository)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<PreparedUpdate> DownloadAndStageAsync(UpdatePackage package, string workRoot,
        IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidatePackage(package);
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(workRoot);
        UpdatePackageLayout.EnsureNoReparseAncestors(root);
        Directory.CreateDirectory(root);
        var workDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        if (Directory.Exists(workDirectory) || File.Exists(workDirectory)) throw new IOException("更新临时目录已被占用。");
        Directory.CreateDirectory(workDirectory);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            var token = timeout.Token;
            var expectedHash = UpdatePackageSecurity.NormalizeSha256(package.Sha256);
            if (expectedHash is null)
            {
                progress?.Report(new UpdateDownloadProgress(0, package.Size, "正在获取校验信息"));
                expectedHash = await ReadChecksumAsync(package, token).ConfigureAwait(false);
            }
            var zipPath = Path.Combine(workDirectory, "package.zip");
            await DownloadPackageAsync(package, expectedHash, zipPath, progress, token).ConfigureAwait(false);
            progress?.Report(new UpdateDownloadProgress(package.Size, package.Size, "正在验证并解压更新"));
            var packageDirectory = Path.Combine(workDirectory, "package");
            Directory.CreateDirectory(packageDirectory);
            await ExtractPackageAsync(zipPath, packageDirectory, token).ConfigureAwait(false);
            UpdatePackageLayout.ValidatePackageDirectory(packageDirectory, package.Version);
            token.ThrowIfCancellationRequested();
            progress?.Report(new UpdateDownloadProgress(package.Size, package.Size, "更新已准备好"));
            return new PreparedUpdate(workDirectory, packageDirectory, package.Version);
        }
        catch (Exception failure)
        {
            try
            {
                // Only the newly-created direct GUID child is owned by this invocation.
                UpdatePackageLayout.EnsureNoReparseAncestors(workDirectory);
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (Exception cleanupFailure) when (cleanupFailure is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"更新未完成，临时目录未能清理：{workDirectory}", new AggregateException(failure, cleanupFailure));
            }
            throw;
        }
    }

    private void ValidatePackage(UpdatePackage package)
    {
        if (package.AssetName != UpdatePackageSecurity.ExpectedAssetName(package.Version)
            || package.Size <= 0 || package.Size > UpdatePackageSecurity.MaximumPackageBytes
            || !UpdatePackageSecurity.IsReleaseAssetUrl(package.DownloadUrl, _repository, package.Version, package.AssetName))
            throw new InvalidDataException("更新包地址、文件名或大小不符合官方发布格式。");
        if (package.Sha256 is not null && UpdatePackageSecurity.NormalizeSha256(package.Sha256) is null)
            throw new InvalidDataException("更新包 SHA256 无效。");
        if (package.ChecksumUrl is not null && !UpdatePackageSecurity.IsReleaseAssetUrl(package.ChecksumUrl, _repository,
                package.Version, Path.ChangeExtension(package.AssetName, ".sha256")))
            throw new InvalidDataException("更新校验文件地址无效。");
        if (package.Sha256 is null && package.ChecksumUrl is null)
            throw new InvalidDataException("更新包没有可用的 SHA256 校验信息。");
    }

    private async Task<string> ReadChecksumAsync(UpdatePackage package, CancellationToken token)
    {
        using var response = await SendWithSafeRedirectsAsync(package.ChecksumUrl!, token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is > 65536) throw new InvalidDataException("更新校验文件过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > 65536) throw new InvalidDataException("更新校验文件过大。");
            memory.Write(buffer, 0, read);
        }
        var entries = Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF').Split('\n')
            .Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1].TrimStart('*') == package.AssetName).ToArray();
        if (entries.Length != 1 || UpdatePackageSecurity.NormalizeSha256(entries[0][0]) is not { } hash)
            throw new InvalidDataException("校验文件中没有唯一对应的更新包 SHA256。");
        return hash;
    }

    private async Task DownloadPackageAsync(UpdatePackage package, string expectedHash, string zipPath,
        IProgress<UpdateDownloadProgress>? progress, CancellationToken token)
    {
        using var response = await SendWithSafeRedirectsAsync(package.DownloadUrl, token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is { } contentLength && contentLength != package.Size)
            throw new InvalidDataException("更新包下载大小与发布信息不一致。");
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var target = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long received = 0;
        var progressTimer = Stopwatch.StartNew();
        while (true)
        {
            var read = await source.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) break;
            received += read;
            if (received > package.Size) throw new InvalidDataException("更新包下载超过声明大小。");
            await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
            if (progressTimer.ElapsedMilliseconds >= 150 || received == package.Size)
            {
                progress?.Report(new UpdateDownloadProgress(received, package.Size, "正在下载更新"));
                progressTimer.Restart();
            }
        }
        if (received != package.Size) throw new InvalidDataException("更新包下载不完整。");
        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(expectedHash)))
            throw new InvalidDataException("更新包 SHA256 校验失败，已停止更新。");
        await target.FlushAsync(token).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendWithSafeRedirectsAsync(Uri original, CancellationToken token)
    {
        var current = original;
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (!UpdatePackageSecurity.IsAllowedRedirect(current, original)) throw new InvalidDataException("更新下载跳转到了非官方地址。");
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("SyncWallpaper-Updater/1.0");
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            try
            {
                if (response.RequestMessage?.RequestUri is { } final && !UpdatePackageSecurity.IsAllowedRedirect(final, original))
                    throw new InvalidDataException("更新下载响应来自非官方地址。");
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location ?? throw new InvalidDataException("更新下载跳转缺少目标地址。");
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    response.Dispose();
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        throw new InvalidDataException("更新下载跳转次数过多。");
    }

    private static async Task ExtractPackageAsync(string zipPath, string packageDirectory, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaximumEntries) throw new InvalidDataException("更新包文件数量过多。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        // Validate the whole directory before creating any extracted file.
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var isDirectory = entry.FullName.EndsWith('/');
            var mode = (entry.ExternalAttributes >> 16) & 0xf000;
            if ((mode != 0 && mode != 0x8000 && mode != 0x4000)
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || !UpdatePackageLayout.IsAllowedRelativePath(entry.FullName, isDirectory)
                || !seen.Add(entry.FullName.TrimEnd('/')))
                throw new InvalidDataException("更新包包含不安全、重复或不允许替换的文件路径。");
            if (entry.Length < 0 || entry.Length > MaximumEntryBytes || (isDirectory && entry.Length != 0))
                throw new InvalidDataException("更新包单个文件过大或目录无效。");
            expanded += entry.Length;
            if (expanded > MaximumExpandedBytes || (entry.Length > 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > 1000))
                throw new InvalidDataException("更新包解压大小超出安全上限。");
        }
        var buffer = new byte[65536];
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(Path.Combine(packageDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(packageDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新包路径超出了临时目录。");
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
            long written = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0) break;
                written += read;
                if (written > entry.Length) throw new InvalidDataException("更新包解压数据超出声明大小。");
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }
            if (written != entry.Length) throw new InvalidDataException("更新包解压数据不完整。");
        }
    }
}
