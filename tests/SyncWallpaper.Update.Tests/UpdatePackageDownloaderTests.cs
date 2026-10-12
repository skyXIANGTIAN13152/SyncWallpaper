using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SyncWallpaper.Update.Core;

namespace SyncWallpaper.Update.Tests;

[TestClass]
public sealed class UpdatePackageDownloaderTests
{
    private static GitHubRepositorySettings Repository => new("owner", "syncwallpaper");
    private const string Version = "1.2.0-beta.9";

    [TestMethod]
    public async Task ValidDigestStagesOnlyProgramFilesAndLeavesExistingSiblingUntouched()
    {
        using var fixture = new DownloadFixture(CreateArchive());
        var other = Path.Combine(fixture.Root, "existing-job");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "keep.txt"), "preserve");
        var progress = new InlineProgress();
        var prepared = await fixture.DownloadAsync(progress);
        Assert.AreEqual(Version, prepared.Version);
        Assert.AreEqual(fixture.Root, Path.GetDirectoryName(prepared.WorkDirectory));
        Assert.IsTrue(Guid.TryParseExact(Path.GetFileName(prepared.WorkDirectory), "N", out _));
        Assert.AreEqual(prepared.WorkDirectory, Path.GetDirectoryName(prepared.PackageDirectory));
        Assert.IsTrue(File.Exists(Path.Combine(prepared.PackageDirectory, "App", "SyncWallpaper.App.exe")));
        Assert.AreEqual("preserve", File.ReadAllText(Path.Combine(other, "keep.txt")));
        Assert.AreEqual(100d, progress.Events.Last().Percent);
        Assert.AreEqual("更新已准备好", progress.Events.Last().Stage);
    }

    [TestMethod]
    public async Task ReleaseChecksumAssetCanSupplySha256WhenApiDigestIsAbsent()
    {
        var archive = CreateArchive();
        using var fixture = new DownloadFixture(archive);
        var package = fixture.Package with { Sha256 = null, ChecksumUrl = AssetUrl(Path.ChangeExtension(fixture.Package.AssetName, ".sha256")) };
        using var client = new HttpClient(new Handler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fixture.Package.Sha256 + "  " + package.AssetName + "\n") }
                : Bytes(archive))));
        var prepared = await new UpdatePackageDownloader(client, Repository).DownloadAndStageAsync(package, fixture.Root, null, CancellationToken.None);
        Assert.IsTrue(Directory.Exists(prepared.PackageDirectory));
    }

    [DataTestMethod]
    [DataRow("different.zip")]
    [DataRow("../SyncWallpaper-1.2.0-beta.9-win-x64-selfcontained.zip")]
    public async Task ChecksumMustNameTheExactPackage(string checksumFileName)
    {
        using var fixture = new DownloadFixture(CreateArchive());
        var package = fixture.Package with { Sha256 = null, ChecksumUrl = AssetUrl(Path.ChangeExtension(fixture.Package.AssetName, ".sha256")) };
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixture.Package.Sha256 + "  " + checksumFileName)
        })));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new UpdatePackageDownloader(client, Repository)
            .DownloadAndStageAsync(package, fixture.Root, null, CancellationToken.None));
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task HashMismatchRemovesOwnedStagingAndDoesNotRemoveOtherWork()
    {
        using var fixture = new DownloadFixture(CreateArchive());
        File.WriteAllText(Path.Combine(fixture.Root, "keep.txt"), "keep");
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.DownloadAsync(package: fixture.Package with { Sha256 = new string('0', 64) }));
        Assert.AreEqual(0, Directory.GetDirectories(fixture.Root).Length);
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(fixture.Root, "keep.txt")));
    }

    [TestMethod]
    public async Task DeclaredSizeMismatchFailsWithoutLeavingStaging()
    {
        using var fixture = new DownloadFixture(CreateArchive());
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.DownloadAsync(package: fixture.Package with { Size = fixture.Package.Size + 1 }));
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task IncompleteStreamingDownloadIsRejectedEvenWithoutContentLength()
    {
        var archive = CreateArchive();
        using var fixture = new DownloadFixture(archive);
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(archive[..(archive.Length / 2)])
        })));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new UpdatePackageDownloader(client, Repository)
            .DownloadAndStageAsync(fixture.Package, fixture.Root, null, CancellationToken.None));
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task CancellationAfterBytesArriveRemovesPartialDownload()
    {
        using var fixture = new DownloadFixture(CreateArchive());
        using var source = new CancellationTokenSource();
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Bytes(CreateArchive()))));
        var progress = new InlineProgress(value => { if (value.BytesReceived > 0) source.Cancel(); });
        try
        {
            await new UpdatePackageDownloader(client, Repository).DownloadAndStageAsync(fixture.Package, fixture.Root, progress, source.Token);
            Assert.Fail("A cancelled download must not become an installable package.");
        }
        catch (OperationCanceledException) { }
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task InvalidZipWithCorrectDigestIsRejectedAndCleaned()
    {
        using var fixture = new DownloadFixture(Encoding.UTF8.GetBytes("not an archive"));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.DownloadAsync());
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task CancellationDuringNetworkRequestCleansOwnedStaging()
    {
        using var fixture = new DownloadFixture(CreateArchive());
        using var source = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            source.Cancel();
            await Task.Delay(1000, token);
            return Bytes(Array.Empty<byte>());
        }));
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => new UpdatePackageDownloader(client, Repository)
            .DownloadAndStageAsync(fixture.Package, fixture.Root, null, source.Token));
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task NetworkFailureCleansOwnedStaging()
    {
        using var fixture = new DownloadFixture(CreateArchive());
        using var client = new HttpClient(new Handler((_, _) => throw new HttpRequestException("offline")));
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => new UpdatePackageDownloader(client, Repository)
            .DownloadAndStageAsync(fixture.Package, fixture.Root, null, CancellationToken.None));
        fixture.AssertNoStagingRemains();
    }

    [DataTestMethod]
    [DataRow("../outside.txt")]
    [DataRow("App/../../outside.txt")]
    [DataRow("/App/file.dll")]
    [DataRow("C:/App/file.dll")]
    [DataRow("App\\file.dll")]
    [DataRow("App/file.dll:stream")]
    [DataRow("App/CON.dll")]
    [DataRow("App/CONIN$.dll")]
    [DataRow("App/COM¹.dll")]
    [DataRow("App/space .")]
    [DataRow("App/file.dll ")]
    [DataRow("App//file.dll")]
    [DataRow("Config/settings.json")]
    [DataRow("Wallpapers/private.jpg")]
    [DataRow("App/Config/settings.json")]
    [DataRow("unexpected.ps1")]
    public async Task UnsafeArchivePathsAreRejectedAndRemoved(string path)
    {
        using var fixture = new DownloadFixture(CreateArchive(extra: (path, "unsafe")));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.DownloadAsync());
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task SymlinkArchiveEntriesAreRejected()
    {
        using var fixture = new DownloadFixture(CreateArchive(modify: archive =>
        {
            var link = archive.CreateEntry("App/link.dll");
            link.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(link.Open());
            writer.Write("../../Config/settings.json");
        }));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.DownloadAsync());
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task CaseInsensitiveDuplicateEntriesAreRejected()
    {
        using var fixture = new DownloadFixture(CreateArchive(extra: ("app/SYNCWALLPAPER.APP.EXE", "duplicate")));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.DownloadAsync());
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task SuspiciousCompressionRatioIsRejected()
    {
        using var fixture = new DownloadFixture(CreateArchive(extra: ("App/bomb.dat", new string('0', 4 * 1024 * 1024))));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.DownloadAsync());
        fixture.AssertNoStagingRemains();
    }

    [DataTestMethod]
    [DataRow("1.2.0-beta.8", "win-x64", true)]
    [DataRow(Version, "win-arm64", true)]
    [DataRow(Version, "win-x64", false)]
    public async Task ManifestVersionRuntimeAndDistributionMustMatch(string version, string rid, bool selfContained)
    {
        using var fixture = new DownloadFixture(CreateArchive(manifest: $$"""{"version":"{{version}}","rid":"{{rid}}","selfContained":{{selfContained.ToString().ToLowerInvariant()}}}"""));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.DownloadAsync());
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task MissingApplicationIsRejected()
    {
        using var fixture = new DownloadFixture(CreateArchive(includeApplication: false));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.DownloadAsync());
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task WrongLanguageManifestIsRejected()
    {
        using var fixture = new DownloadFixture(CreateArchive(manifest: $$"""{"version":"{{Version}}","rid":"win-x64","selfContained":true,"language":"en"}"""));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.DownloadAsync());
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task OfficialCdnRedirectIsFollowedAndVerified()
    {
        var bytes = CreateArchive();
        using var fixture = new DownloadFixture(bytes);
        var calls = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            return Task.FromResult(request.RequestUri!.Host == "github.com"
                ? Redirect("https://release-assets.githubusercontent.com/github-production-release-asset/file?sig=example")
                : Bytes(bytes));
        }));
        var prepared = await new UpdatePackageDownloader(client, Repository).DownloadAndStageAsync(fixture.Package, fixture.Root, null, CancellationToken.None);
        Assert.AreEqual(2, calls);
        Assert.IsTrue(Directory.Exists(prepared.PackageDirectory));
    }

    [DataTestMethod]
    [DataRow("https://evil.example/update.zip")]
    [DataRow("http://release-assets.githubusercontent.com/update.zip")]
    [DataRow("https://github.com/other/repository/releases/download/v1.2.0-beta.9/update.zip")]
    [DataRow("https://release-assets.githubusercontent.com.evil.example/update.zip")]
    public async Task UnsafeRedirectIsRejectedBeforeSecondRequest(string url)
    {
        using var fixture = new DownloadFixture(CreateArchive());
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Redirect(url)); }));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new UpdatePackageDownloader(client, Repository)
            .DownloadAndStageAsync(fixture.Package, fixture.Root, null, CancellationToken.None));
        Assert.AreEqual(1, calls);
        fixture.AssertNoStagingRemains();
    }

    [TestMethod]
    public async Task InvalidPackageMetadataNeverMakesNetworkRequestOrStaging()
    {
        using var fixture = new DownloadFixture(CreateArchive());
        var requests = 0;
        using var client = new HttpClient(new Handler((_, _) => { requests++; return Task.FromResult(Bytes(Array.Empty<byte>())); }));
        var downloader = new UpdatePackageDownloader(client, Repository);
        var variants = new[]
        {
            fixture.Package with { DownloadUrl = new Uri("https://evil.example/update.zip") },
            fixture.Package with { AssetName = "unexpected.zip" },
            fixture.Package with { Size = UpdatePackageSecurity.MaximumPackageBytes + 1 },
            fixture.Package with { Sha256 = null, ChecksumUrl = null },
            fixture.Package with { Sha256 = "not-a-hash" }
        };
        foreach (var package in variants)
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => downloader.DownloadAndStageAsync(package, fixture.Root, null, CancellationToken.None));
        Assert.AreEqual(0, requests);
        fixture.AssertNoStagingRemains();
    }

    private static byte[] CreateArchive((string Path, string Contents)? extra = null, string? manifest = null,
        Action<ZipArchive>? modify = null, bool includeApplication = true)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add("package-manifest.json", manifest ?? $$"""{"version":"{{Version}}","rid":"win-x64","selfContained":true,"language":"zh-CN"}""");
            if (includeApplication) Add("App/SyncWallpaper.App.exe", "fake program fixture; never executed");
            Add("docs/UPDATES.md", "update instructions");
            if (extra.HasValue) Add(extra.Value.Path, extra.Value.Contents);
            modify?.Invoke(archive);
            void Add(string path, string contents)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                writer.Write(contents);
            }
        }
        return stream.ToArray();
    }

    private static Uri AssetUrl(string assetName) => new($"https://github.com/owner/syncwallpaper/releases/download/v{Version}/{assetName}");
    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Redirect(string url)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(url);
        return response;
    }

    private sealed class DownloadFixture : IDisposable
    {
        private readonly HttpClient _client;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "SyncWallpaper-update-download-tests-" + Guid.NewGuid().ToString("N"));
        public UpdatePackage Package { get; }

        public DownloadFixture(byte[] bytes)
        {
            Directory.CreateDirectory(Root);
            var assetName = UpdatePackageSecurity.ExpectedAssetName(Version);
            Package = new UpdatePackage(Version, assetName, AssetUrl(assetName), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), null);
            _client = new HttpClient(new Handler((_, _) => Task.FromResult(Bytes(bytes))));
        }

        public Task<PreparedUpdate> DownloadAsync(IProgress<UpdateDownloadProgress>? progress = null, UpdatePackage? package = null)
            => new UpdatePackageDownloader(_client, Repository).DownloadAndStageAsync(package ?? Package, Root, progress, CancellationToken.None);

        public void AssertNoStagingRemains() => Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(Root).Count());
        public void Dispose()
        {
            _client.Dispose();
            var fullPath = Path.GetFullPath(Root);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith("SyncWallpaper-update-download-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test fixture cleanup target.");
            if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
        }
    }

    private sealed class InlineProgress(Action<UpdateDownloadProgress>? onReport = null) : IProgress<UpdateDownloadProgress>
    {
        public List<UpdateDownloadProgress> Events { get; } = new();
        public void Report(UpdateDownloadProgress value)
        {
            Events.Add(value);
            onReport?.Invoke(value);
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
