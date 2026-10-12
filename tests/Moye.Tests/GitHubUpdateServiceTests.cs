using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moye.Services;

namespace Moye.Tests;

public sealed class GitHubUpdateServiceTests : IDisposable
{
    private const string InstallerName = "Penroam-1.2.0-Setup-win-x64.exe";
    private const string InstallerUrl = "https://github.com/yikeugene/Penroam/releases/download/v1.2.0/" + InstallerName;
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("MZ synthetic update fixture; never an executable");
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PenroamUpdateTests-" + Guid.NewGuid().ToString("N"));
    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (var client in _clients) client.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task ChecksCanonicalPublicRepositoryAndSelectsExactInstaller()
    {
        Uri? requested = null;
        string? userAgent = null;
        var service = Service((request, _) =>
        {
            requested = request.RequestUri;
            userAgent = request.Headers.UserAgent.ToString();
            Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString());
            return Task.FromResult(Json(Release(assets: [
                Asset("Penroam-1.2.0-win-x64.zip"), Asset("Penroam-1.2.0-Setup-win-arm64.exe"), Asset()])));
        });
        var update = Assert.IsType<AppUpdate>(await service.CheckAsync());
        Assert.Equal("https://api.github.com/repos/yikeugene/Penroam/releases/latest", requested!.AbsoluteUri);
        Assert.Equal("Penroam/1.1.0", userAgent);
        Assert.Equal(new Version(1, 2, 0), update.Version);
        Assert.Equal(InstallerName, update.InstallerName);
        Assert.Equal(InstallerUrl, update.InstallerUrl.AbsoluteUri);
        Assert.Equal(Hash, update.Sha256);
        Assert.Equal("Release notes", update.Notes);
        Assert.Equal("https://github.com/yikeugene/Penroam/releases/tag/v1.2.0", update.ReleaseUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("1.1.0", false, false)]
    [InlineData("v1.0.9", false, false)]
    [InlineData("v1.2.0", true, false)]
    [InlineData("v1.2.0", false, true)]
    [InlineData("v1.2.0-beta.1", false, false)]
    [InlineData("v1.2", false, false)]
    [InlineData("1.2.0.0", false, false)]
    [InlineData("v01.2.0", false, false)]
    [InlineData("v1.2.-1", false, false)]
    [InlineData("v1.2.0/installer", false, false)]
    public async Task DoesNotOfferSameOlderDraftPrereleaseOrUnrecognizedVersions(string tag, bool draft, bool prerelease)
    {
        var service = Service((_, _) => Task.FromResult(Json(Release(tag, draft: draft, prerelease: prerelease))));
        Assert.Null(await service.CheckAsync());
    }

    [Fact]
    public async Task ComparesVersionNumbersInsteadOfStrings()
    {
        const string name = "Penroam-1.10.0-Setup-win-x64.exe";
        var service = Service((_, _) => Task.FromResult(Json(Release("V1.10.0", assets: [Asset(name,
            "https://github.com/yikeugene/Penroam/releases/download/V1.10.0/" + name)]))), new Version(1, 9, 0, 0));
        Assert.Equal(new Version(1, 10, 0), (await service.CheckAsync())!.Version);
    }

    [Theory]
    [InlineData("https://github.com/yikeugene/moye/releases/download/v1.2.0/")]
    [InlineData("https://github.com/YIKEUGENE/PENROAM/releases/download/v1.2.0/")]
    public async Task AcceptsOnlyKnownRepositoryAliases(string prefix)
    {
        var service = Service((_, _) => Task.FromResult(Json(Release(assets: [Asset(url: prefix + InstallerName)]))));
        Assert.Equal(prefix + InstallerName, (await service.CheckAsync())!.InstallerUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://github.com/yikeugene/Penroam/releases/download/v1.2.0/")]
    [InlineData("https://example.com/yikeugene/Penroam/releases/download/v1.2.0/")]
    [InlineData("https://github.com/another/Penroam/releases/download/v1.2.0/")]
    [InlineData("https://github.com/yikeugene/other/releases/download/v1.2.0/")]
    [InlineData("https://github.com/yikeugene/Penroam/releases/download/v1.3.0/")]
    [InlineData("https://github.com:444/yikeugene/Penroam/releases/download/v1.2.0/")]
    [InlineData("https://someone@github.com/yikeugene/Penroam/releases/download/v1.2.0/")]
    public async Task RejectsAssetsOutsideExactPublishedRelease(string prefix)
    {
        var service = Service((_, _) => Task.FromResult(Json(Release(assets: [Asset(url: prefix + InstallerName)]))));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync());
    }

    [Fact]
    public async Task NewerReleaseWithoutCompatibleInstallerDoesNotClaimUpToDate()
    {
        var service = Service((_, _) => Task.FromResult(Json(Release(assets: [Asset("source.zip")]))));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync());
        Assert.Contains("installer", error.Message);
    }

    [Fact]
    public async Task NewerReleaseRequiresIntegrityMetadata()
    {
        var service = Service((_, _) => Task.FromResult(Json(Release(assets: [Asset(digest: null)]))));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync());
        Assert.Contains("checksum", error.Message);
    }

    [Fact]
    public async Task ResolvesChecksumSidecarBeforeOfferingTheUpdate()
    {
        var requested = new List<string>();
        var service = Service((request, _) =>
        {
            requested.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(request.RequestUri.Host == "api.github.com"
                ? Json(Release(assets: [Asset(digest: null), Asset(InstallerName + ".sha256", InstallerUrl + ".sha256", digest: null)]))
                : Text(Hash.ToUpperInvariant() + "  " + InstallerName + "\r\n"));
        });
        var update = Assert.IsType<AppUpdate>(await service.CheckAsync());
        Assert.Equal(Hash, update.Sha256);
        Assert.Equal(InstallerUrl + ".sha256", update.ChecksumUrl!.AbsoluteUri);
        Assert.Equal(2, requested.Count);
    }

    [Theory]
    [InlineData("different.exe")]
    [InlineData("../Penroam-1.2.0-Setup-win-x64.exe")]
    [InlineData("Penroam-1.2.0-Setup-win-x64.exe\nanother-line")]
    public async Task RejectsChecksumForAnotherFileOrAmbiguousContent(string name)
    {
        var service = Service((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com"
            ? Json(Release(assets: [Asset(digest: null), Asset(InstallerName + ".sha256", InstallerUrl + ".sha256", digest: null)]))
            : Text(Hash + "  " + name)));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync());
    }

    [Theory]
    [InlineData("sha256:xyz")]
    [InlineData("sha256:000000000000000000000000000000000000000000000000000000000000000g")]
    public async Task RejectsMalformedPublishedDigest(string digest)
    {
        var service = Service((_, _) => Task.FromResult(Json(Release(assets: [Asset(digest: digest)]))));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(1073741825L)]
    public async Task RejectsUnboundedOrInvalidInstallerSizes(long size)
    {
        var service = Service((_, _) => Task.FromResult(Json(Release(assets: [Asset(size: size)]))));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync());
    }

    [Fact]
    public async Task NoPublishedReleaseReturnsNoUpdate()
    {
        var service = Service((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await service.CheckAsync());
    }

    [Fact]
    public async Task RateLimitIsAnErrorNotUpToDate()
    {
        var service = Service((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckAsync());
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }

    [Fact]
    public async Task RejectsOversizedMetadataBeforeParsing()
    {
        var service = Service((_, _) => Task.FromResult(Text(new string(' ', 1024 * 1024 + 1))));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync());
    }

    [Fact]
    public async Task DownloadsVerifiesAndReportsCompletionOnlyAfterVerification()
    {
        var service = Service((_, _) => Task.FromResult(Bytes(Payload)));
        var reports = new List<double>();
        var path = await service.DownloadAsync(Update(), new DirectProgress(reports.Add));
        Assert.Equal(InstallerName, Path.GetFileName(path));
        Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        Assert.StartsWith(_directory, path);
        Assert.Equal(0, reports[0]);
        Assert.Equal(1, reports[^1]);
        Assert.All(reports.Take(reports.Count - 1), value => Assert.InRange(value, 0, 0.99));
        Assert.Single(Directory.GetFiles(_directory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ChecksumFailureRemovesPartialInstallerAndNeverReportsComplete()
    {
        var service = Service((_, _) => Task.FromResult(Bytes(Payload)));
        var reports = new List<double>();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(
            Update() with { Sha256 = new string('0', 64) }, new DirectProgress(reports.Add)));
        Assert.Contains("checksum", error.Message);
        Assert.DoesNotContain(1, reports);
        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task DetectsTruncatedOrOversizedStreamWithoutContentLength(int difference)
    {
        var service = Service((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(Payload))
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Update() with { Size = Payload.Length + difference }));
        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Fact]
    public async Task CancellingDuringDownloadRemovesAllStagedFiles()
    {
        var bytes = new byte[160000];
        var service = Service((_, _) => Task.FromResult(Bytes(bytes)));
        using var cancellation = new CancellationTokenSource();
        var update = Update() with { Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(update,
            new DirectProgress(value => { if (value > 0) cancellation.Cancel(); }), cancellation.Token));
        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Fact]
    public async Task CancellationIsPassedToMetadataRequest()
    {
        using var cancellation = new CancellationTokenSource();
        var service = Service(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return Json(Release());
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckAsync(cancellation.Token));
    }

    [Fact]
    public async Task FollowsGitHubSignedReleaseStorageRedirect()
    {
        var requests = new List<string>();
        var service = Service((request, _) =>
        {
            requests.Add(request.RequestUri!.Host);
            return Task.FromResult(request.RequestUri.Host == "github.com"
                ? Redirect("https://release-assets.githubusercontent.com/github-production-release-asset/123?signature=example")
                : Bytes(Payload));
        });
        Assert.True(File.Exists(await service.DownloadAsync(Update())));
        Assert.Equal(new[] { "github.com", "release-assets.githubusercontent.com" }, requests);
    }

    [Fact]
    public async Task FollowsLegacyMoyeToCanonicalPenroamRedirect()
    {
        var service = Service((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/moye/")
            ? Redirect(InstallerUrl) : Bytes(Payload)));
        var update = Update() with { InstallerUrl = new Uri(InstallerUrl.Replace("/Penroam/", "/moye/")) };
        Assert.True(File.Exists(await service.DownloadAsync(update)));
    }

    [Theory]
    [InlineData("https://example.com/setup.exe")]
    [InlineData("http://release-assets.githubusercontent.com/setup.exe")]
    [InlineData("https://github.com/another/repository/releases/download/setup.exe")]
    [InlineData("https://release-assets.githubusercontent.com.attacker.example/setup.exe")]
    public async Task RejectsUnexpectedDownloadRedirectBeforeRequestingIt(string target)
    {
        var requests = 0;
        var service = Service((_, _) => { requests++; return Task.FromResult(Redirect(target)); });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Update()));
        Assert.Equal(1, requests);
        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Fact]
    public async Task RejectsForgedDownloadInformationBeforeMakingRequestOrWritingFile()
    {
        var service = Service((_, _) => throw new InvalidOperationException("No request should be made."));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(Update() with { InstallerName = "../setup.exe" }));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task ReleaseCheckCleansOnlyOldKnownInstallerFilesInGuidFolders()
    {
        var oldDirectory = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        var recentDirectory = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        var unrelatedDirectory = Path.Combine(_directory, "unrelated");
        Directory.CreateDirectory(oldDirectory);
        Directory.CreateDirectory(recentDirectory);
        Directory.CreateDirectory(unrelatedDirectory);
        var oldInstaller = Path.Combine(oldDirectory, InstallerName);
        var oldPartial = oldInstaller + ".download";
        var recentInstaller = Path.Combine(recentDirectory, InstallerName);
        var unrelatedInstaller = Path.Combine(unrelatedDirectory, InstallerName);
        foreach (var file in new[] { oldInstaller, oldPartial, recentInstaller, unrelatedInstaller })
            await File.WriteAllBytesAsync(file, Payload);
        foreach (var file in new[] { oldInstaller, oldPartial, unrelatedInstaller })
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-8));
        var service = Service((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Null(await service.CheckAsync());

        Assert.False(Directory.Exists(oldDirectory));
        Assert.True(File.Exists(recentInstaller));
        Assert.True(File.Exists(unrelatedInstaller));
        Assert.True(Directory.Exists(_directory));
    }

    [Fact]
    public async Task CleanupPreservesUnknownFilesAndNestedFolders()
    {
        var directory = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(directory, "nested");
        Directory.CreateDirectory(nested);
        var unknown = Path.Combine(directory, "personal.txt");
        var nestedInstaller = Path.Combine(nested, InstallerName);
        var invalidInstaller = Path.Combine(directory, "Penroam-v1.2.0-Setup-win-x64.exe");
        foreach (var file in new[] { unknown, nestedInstaller, invalidInstaller })
        {
            await File.WriteAllBytesAsync(file, Payload);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-8));
        }
        var service = Service((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await service.CheckAsync());
        Assert.True(File.Exists(unknown));
        Assert.True(File.Exists(nestedInstaller));
        Assert.True(File.Exists(invalidInstaller));
    }

    [Fact]
    public async Task CleanupKeepsRecentEmptyFolderAndOverlappingUnknownFilename()
    {
        var directory = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        var empty = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(empty);
        var unknown = Path.Combine(directory, "Penroam-Setup-win-x64.exe");
        await File.WriteAllBytesAsync(unknown, Payload);
        File.SetLastWriteTimeUtc(unknown, DateTime.UtcNow.AddDays(-8));
        var service = Service((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await service.CheckAsync());
        Assert.True(File.Exists(unknown));
        Assert.True(Directory.Exists(empty));
    }

    private GitHubUpdateService Service(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        Version? currentVersion = null)
    {
        var client = new HttpClient(new FakeHandler(responder));
        _clients.Add(client);
        return new GitHubUpdateService(currentVersion ?? new Version(1, 1, 0), client, _directory);
    }

    private static AppUpdate Update() => new(new Version(1, 2, 0), "v1.2.0", "Release notes",
        new Uri("https://github.com/yikeugene/Penroam/releases/tag/v1.2.0"),
        new Uri(InstallerUrl), InstallerName, Payload.Length, Hash, null);

    private static object Release(string tag = "v1.2.0", object[]? assets = null, bool draft = false, bool prerelease = false) =>
        new { tag_name = tag, draft, prerelease, body = "Release notes", assets = assets ?? [Asset()] };

    private static object Asset(string name = InstallerName, string? url = null, string? digest = "default", long? size = null) =>
        new { name, browser_download_url = url ?? InstallerUrl, size = size ?? Payload.Length,
            state = "uploaded", digest = digest == "default" ? "sha256:" + Hash : digest };

    private static HttpResponseMessage Json(object value) => Text(JsonSerializer.Serialize(value));
    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static HttpResponseMessage Bytes(byte[] value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
    private static HttpResponseMessage Redirect(string url) => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(url) } };
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request, cancellationToken);
    }
    private sealed class DirectProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => _inner.ReadAsync(buffer, token);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}