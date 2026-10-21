using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using L = Moye.Localization.LocalizationService;

namespace Moye.Services;

public sealed record AppUpdate(Version Version, string Tag, string Notes, Uri ReleaseUrl,
    Uri InstallerUrl, string InstallerName, long Size, string? Sha256, Uri? ChecksumUrl);

public interface IAppUpdateService
{
    Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken = default);
    Task<string> DownloadAsync(AppUpdate update, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Checks the public Penroam release and stages a verified Windows installer.</summary>
public sealed class GitHubUpdateService : IAppUpdateService
{
    private const string RepositoryUrl = "https://github.com/yikeugene/Penroam";
    private const long MaximumInstallerBytes = 1024L * 1024 * 1024;
    private const int MaximumMetadataBytes = 1024 * 1024;
    private static readonly Uri LatestReleaseUrl = new("https://api.github.com/repos/yikeugene/Penroam/releases/latest");
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private readonly Version _currentVersion;
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public static Version CurrentVersion
    {
        get
        {
            var version = typeof(GitHubUpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        }
    }

    public GitHubUpdateService(Version currentVersion, HttpClient? httpClient = null, string? cacheDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        _currentVersion = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
        _httpClient = httpClient ?? SharedClient;
        _cacheDirectory = Path.GetFullPath(cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Moye", "Updates"));
    }

    public async Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(CleanupStagedDownloads, cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await GetAsync(LatestReleaseUrl, false, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var metadata = await ReadLimitedAsync(response.Content, MaximumMetadataBytes, timeout.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(metadata);
        var release = document.RootElement;
        if (release.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(L.Text("Messages.Update.InvalidRelease"));
        if (ReadBoolean(release, "draft") || ReadBoolean(release, "prerelease")) return null;
        var tag = ReadString(release, "tag_name");
        if (!TryParseStableVersion(tag, out var version) || version <= _currentVersion) return null;

        var installerName = $"Penroam-{version.ToString(3)}-Setup-win-x64.exe";
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(L.Text("Messages.Update.NoInstaller"));
        var installer = FindAsset(assets, installerName);
        if (installer is null)
            throw new InvalidDataException(L.Text("Messages.Update.NoCompatibleInstaller"));
        if (!installer.Value.TryGetProperty("size", out var sizeElement) || sizeElement.ValueKind != JsonValueKind.Number || !sizeElement.TryGetInt64(out var size)
            || size <= 0 || size > MaximumInstallerBytes)
            throw new InvalidDataException(L.Text("Messages.Update.InvalidSize"));
        var installerUrl = ReadAssetUrl(installer.Value, tag!, installerName);
        var hash = ReadDigest(installer.Value);
        Uri? checksumUrl = null;
        if (hash is null)
        {
            var checksumName = installerName + ".sha256";
            var checksum = FindAsset(assets, checksumName)
                ?? throw new InvalidDataException(L.Text("Messages.Update.ChecksumMissing"));
            checksumUrl = ReadAssetUrl(checksum, tag!, checksumName);
            hash = await ReadChecksumAsync(checksumUrl, installerName, timeout.Token).ConfigureAwait(false);
        }
        var notes = ReadString(release, "body") ?? "";
        if (notes.Length > 12000) notes = notes[..12000] + "\n…";
        return new AppUpdate(version, tag!, notes,
            new Uri($"{RepositoryUrl}/releases/tag/{Uri.EscapeDataString(tag!)}"),
            installerUrl, installerName, size, hash, checksumUrl);
    }

    public async Task<string> DownloadAsync(AppUpdate update, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateUpdate(update);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        var token = timeout.Token;
        token.ThrowIfCancellationRequested();
        var expectedHash = update.Sha256;
        if (expectedHash is null)
            expectedHash = await ReadChecksumAsync(update.ChecksumUrl!, update.InstallerName, token).ConfigureAwait(false);
        var downloadDirectory = Path.Combine(_cacheDirectory, Guid.NewGuid().ToString("N"));
        var partialPath = Path.Combine(downloadDirectory, update.InstallerName + ".download");
        var installerPath = Path.Combine(downloadDirectory, update.InstallerName);
        Directory.CreateDirectory(downloadDirectory);
        var completed = false;
        try
        {
            progress?.Report(0);
            using var response = await GetAsync(update.InstallerUrl, true, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != update.Size)
                throw new InvalidDataException(L.Text("Messages.Update.DownloadSizeMismatch"));
            await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var destination = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > update.Size || total > MaximumInstallerBytes)
                        throw new InvalidDataException(L.Text("Messages.Update.DownloadTooLarge"));
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    // Verification still needs to finish before reporting completion.
                    progress?.Report(Math.Min(0.99, (double)total / update.Size));
                }
                if (total != update.Size)
                    throw new InvalidDataException(L.Text("Messages.Update.DownloadIncomplete"));
                var actualHash = hash.GetHashAndReset();
                if (!CryptographicOperations.FixedTimeEquals(actualHash, Convert.FromHexString(expectedHash)))
                    throw new InvalidDataException(L.Text("Messages.Update.ChecksumMismatch"));
                await destination.FlushAsync(token).ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(partialPath, installerPath);
            completed = true;
            progress?.Report(1);
            return installerPath;
        }
        finally
        {
            if (!completed)
            {
                TryDelete(partialPath);
                TryDelete(installerPath);
                try { Directory.Delete(downloadDirectory); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private void CleanupStagedDownloads()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory)
                || (File.GetAttributes(_cacheDirectory) & FileAttributes.ReparsePoint) != 0) return;
            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var directory in Directory.EnumerateDirectories(_cacheDirectory))
            {
                try
                {
                    if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)
                        || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    var removedOldCandidate = false;
                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        var name = Path.GetFileName(file);
                        if (name.EndsWith(".download", StringComparison.Ordinal)) name = name[..^9];
                        const string prefix = "Penroam-";
                        const string suffix = "-Setup-win-x64.exe";
                        if (name.Length <= prefix.Length + suffix.Length
                            || !name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)
                            || !TryParseStableVersion(name[prefix.Length..^suffix.Length], out var version)
                            || name != $"{prefix}{version.ToString(3)}{suffix}"
                            || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0
                            || File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                        TryDelete(file);
                        removedOldCandidate = true;
                    }
                    // Nonrecursive removal preserves unknown files, nested folders and links.
                    if ((removedOldCandidate || Directory.GetLastWriteTimeUtc(directory) < cutoff)
                        && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool TryParseStableVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrEmpty(tag)) return false;
        var numeric = tag[0] is 'v' or 'V' ? tag[1..] : tag;
        var parts = numeric.Split('.');
        if (parts.Length != 3) return false;
        var numbers = new int[3];
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length == 0 || (parts[index].Length > 1 && parts[index][0] == '0')
                || !int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
                return false;
        }
        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    private static JsonElement? FindAsset(JsonElement assets, string name)
    {
        JsonElement? match = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object || ReadString(asset, "name") != name) continue;
            if (match is not null) throw new InvalidDataException(L.Text("Messages.Update.DuplicateInstaller"));
            if (ReadString(asset, "state") is { } state && state != "uploaded") continue;
            match = asset;
        }
        return match;
    }

    private static bool ReadBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException(L.Text("Messages.Update.IncompleteRelease"));
        return value.GetBoolean();
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Uri ExpectedAssetUrl(string tag, string name) =>
        new($"{RepositoryUrl}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}");

    private static Uri ReadAssetUrl(JsonElement asset, string tag, string name)
    {
        var expected = ExpectedAssetUrl(tag, name);
        if (!Uri.TryCreate(ReadString(asset, "browser_download_url"), UriKind.Absolute, out var actual) || !SameUrl(actual, expected))
            throw new InvalidDataException(L.Text("Messages.Update.WrongRelease"));
        return actual;
    }

    private static bool SameUrl(Uri actual, Uri expected) => actual.IsAbsoluteUri && actual.Scheme == Uri.UriSchemeHttps
        && actual.IsDefaultPort && actual.UserInfo.Length == 0 && actual.Fragment.Length == 0 && actual.Query.Length == 0
        && string.Equals(actual.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
        && HasSameRepositoryPath(actual.AbsolutePath, expected.AbsolutePath);

    private static bool HasSameRepositoryPath(string actualPath, string expectedPath)
    {
        const string canonicalPrefix = "/yikeugene/Penroam";
        const string legacyPrefix = "/yikeugene/moye";
        var prefix = actualPath.StartsWith(canonicalPrefix + "/", StringComparison.OrdinalIgnoreCase) ? canonicalPrefix
            : actualPath.StartsWith(legacyPrefix + "/", StringComparison.OrdinalIgnoreCase) ? legacyPrefix : null;
        return prefix is not null && string.Equals(actualPath[prefix.Length..], expectedPath[canonicalPrefix.Length..], StringComparison.Ordinal);
    }

    private static string? ReadDigest(JsonElement asset)
    {
        var digest = ReadString(asset, "digest");
        if (string.IsNullOrEmpty(digest)) return null;
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
        var hash = digest[7..];
        if (!ValidHash(hash)) throw new InvalidDataException(L.Text("Messages.Update.InvalidChecksum"));
        return hash.ToLowerInvariant();
    }

    private static bool ValidHash(string? hash) => hash is { Length: 64 }
        && hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static void ValidateUpdate(AppUpdate update)
    {
        if (!TryParseStableVersion(update.Tag, out var version) || version != update.Version
            || update.InstallerName != $"Penroam-{version.ToString(3)}-Setup-win-x64.exe"
            || update.Size <= 0 || update.Size > MaximumInstallerBytes
            || !SameUrl(update.InstallerUrl, ExpectedAssetUrl(update.Tag, update.InstallerName))
            || (update.Sha256 is not null && !ValidHash(update.Sha256))
            || (update.Sha256 is null && update.ChecksumUrl is null)
            || (update.ChecksumUrl is not null && !SameUrl(update.ChecksumUrl, ExpectedAssetUrl(update.Tag, update.InstallerName + ".sha256"))))
            throw new InvalidDataException(L.Text("Messages.Update.InvalidInformation"));
    }

    private async Task<string> ReadChecksumAsync(Uri url, string installerName, CancellationToken token)
    {
        using var response = await GetAsync(url, true, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await ReadLimitedAsync(response.Content, 4096, token).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(bytes).Trim();
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !ValidHash(parts[0]) || parts[1].TrimStart('*') != installerName)
            throw new InvalidDataException(L.Text("Messages.Update.WrongChecksumFile"));
        return parts[0].ToLowerInvariant();
    }

    private async Task<HttpResponseMessage> GetAsync(Uri url, bool allowAssetRedirects, CancellationToken token)
    {
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd($"Penroam/{_currentVersion.ToString(3)}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (redirect == 5 || location is null || !allowAssetRedirects)
                throw new InvalidDataException(L.Text("Messages.Update.UnexpectedRedirect"));
            var next = location.IsAbsoluteUri ? location : new Uri(url, location);
            if (next.Scheme != Uri.UriSchemeHttps || !next.IsDefaultPort || next.UserInfo.Length > 0 || next.Fragment.Length > 0
                || !IsReleaseDownloadHost(next))
                throw new InvalidDataException(L.Text("Messages.Update.RedirectOutsideGitHub"));
            url = next;
        }
        throw new InvalidDataException(L.Text("Messages.Update.TooManyRedirects"));
    }

    private static bool IsReleaseDownloadHost(Uri uri) => uri.Host.ToLowerInvariant() switch
    {
        "release-assets.githubusercontent.com" => true,
        "objects.githubusercontent.com" => true,
        "github-releases.githubusercontent.com" => true,
        "github.com" => uri.AbsolutePath.StartsWith("/yikeugene/Penroam/releases/download/", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.StartsWith("/yikeugene/moye/releases/download/", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maximumBytes, CancellationToken token)
    {
        if (content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException(L.Text("Messages.Update.ResponseTooLarge"));
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (result.Length + read > maximumBytes)
                throw new InvalidDataException(L.Text("Messages.Update.ResponseTooLarge"));
            result.Write(buffer, 0, read);
        }
        return result.ToArray();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}