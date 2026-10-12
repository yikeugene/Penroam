using System.IO;
using System.Security.Cryptography;
using Moye.Services;

namespace Moye.Tests;

public sealed class UpdateInstallCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Penroam-update-handoff-tests-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _bytes = [0x4d, 0x5a, 1, 2, 3, 4];

    private (AppUpdate Update, string Path) Payload()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "Penroam-1.2.0-Setup-win-x64.exe");
        File.WriteAllBytes(path, _bytes);
        return (new AppUpdate(new Version(1, 2, 0), "v1.2.0", "Notes",
            new Uri("https://github.com/yikeugene/Penroam/releases/tag/v1.2.0"),
            new Uri("https://github.com/yikeugene/Penroam/releases/download/v1.2.0/Penroam-1.2.0-Setup-win-x64.exe"),
            Path.GetFileName(path), _bytes.Length, Convert.ToHexString(SHA256.HashData(_bytes)), null), path);
    }

    [Fact]
    public async Task SavesBeforeLaunchAndLocksVerifiedPayloadThroughoutHandoff()
    {
        var (update, path) = Payload();
        var events = new List<string>();
        var coordinator = new UpdateInstallCoordinator(new FakeInstaller(value =>
        {
            Assert.Equal(path, value);
            Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose());
            events.Add("launch");
        }));
        await coordinator.InstallAsync(update, path, async () =>
        {
            Assert.Throws<IOException>(() => File.WriteAllBytes(path, [9]));
            await Task.Yield();
            events.Add("save");
        });
        Assert.Equal(new[] { "save", "launch" }, events);
        File.WriteAllBytes(path, _bytes); // Lock is released after handoff.
    }

    [Fact]
    public async Task SaveFailureNeverLaunchesInstaller()
    {
        var (update, path) = Payload();
        var launched = false;
        var coordinator = new UpdateInstallCoordinator(new FakeInstaller(_ => launched = true));
        await Assert.ThrowsAsync<IOException>(() => coordinator.InstallAsync(update, path,
            () => Task.FromException(new IOException("Disk is full"))));
        Assert.False(launched);
    }

    [Fact]
    public async Task LaunchFailurePropagatesAfterSaveAndReleasesFile()
    {
        var (update, path) = Payload();
        var saved = false;
        var coordinator = new UpdateInstallCoordinator(new FakeInstaller(_ => throw new InvalidOperationException("Launch blocked")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.InstallAsync(update, path,
            () => { saved = true; return Task.CompletedTask; }));
        Assert.True(saved);
        File.Delete(path);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TamperedPayloadNeverSavesOrLaunches(bool differentSize)
    {
        var (update, path) = Payload();
        File.WriteAllBytes(path, differentSize ? [1] : [9, 9, 9, 9, 9, 9]);
        var touched = false;
        var coordinator = new UpdateInstallCoordinator(new FakeInstaller(_ => touched = true));
        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.InstallAsync(update, path,
            () => { touched = true; return Task.CompletedTask; }));
        Assert.False(touched);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    public async Task MissingVerificationNeverLaunches(string? checksum)
    {
        var (update, path) = Payload();
        var launched = false;
        var coordinator = new UpdateInstallCoordinator(new FakeInstaller(_ => launched = true));
        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.InstallAsync(update with { Sha256 = checksum }, path,
            () => Task.CompletedTask));
        Assert.False(launched);
    }

    [Fact]
    public async Task CancellationAfterSaveNeverLaunches()
    {
        var (update, path) = Payload();
        using var cancellation = new CancellationTokenSource();
        var launched = false;
        var coordinator = new UpdateInstallCoordinator(new FakeInstaller(_ => launched = true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.InstallAsync(update, path,
            () => { cancellation.Cancel(); return Task.CompletedTask; }, cancellation.Token));
        Assert.False(launched);
    }

    private sealed class FakeInstaller(Action<string> launch) : IUpdateInstaller
    {
        public void Launch(string path) => launch(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
