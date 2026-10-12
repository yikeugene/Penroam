using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Moye.Services;

namespace Moye.Tests;

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Penroam updater 測試 " + Guid.NewGuid().ToString("N"));
    private readonly string _installer;

    public UpdateInstallerTests()
    {
        Directory.CreateDirectory(_directory);
        _installer = Path.Combine(_directory, "Penroam-1.2.0-Setup-win-x64.exe");
        File.WriteAllText(_installer, "Test fixture; never executed.");
    }

    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public void HandoffPreservesUnicodeAndSpacesAsIndividualArguments()
    {
        var target = Path.Combine(_directory, "筆記 & notes");
        Directory.CreateDirectory(target);
        var start = UpdateInstaller.CreateStartInfo(_installer, target + Path.DirectorySeparatorChar, 12345);

        Assert.Equal(_installer, start.FileName);
        Assert.Equal(_directory, start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
        Assert.Empty(start.Arguments);
        Assert.Equal("/DIR=" + target, start.ArgumentList[^1]);
        Assert.Contains("/PENROAMUPDATE=12345", start.ArgumentList);
        Assert.Equal(8, start.ArgumentList.Count);
    }

    [Fact]
    public void HandoffShowsInstallationProgressWithoutClosingAppsOrRestartingWindows()
    {
        var start = UpdateInstaller.CreateStartInfo(_installer, _directory, 1);
        Assert.Contains("/SILENT", start.ArgumentList);
        Assert.Contains("/NORESTART", start.ArgumentList);
        Assert.Contains("/NOCLOSEAPPLICATIONS", start.ArgumentList);
        Assert.Contains("/NOFORCECLOSEAPPLICATIONS", start.ArgumentList);
        Assert.Contains("/NORESTARTAPPLICATIONS", start.ArgumentList);
        Assert.DoesNotContain("/FORCECLOSEAPPLICATIONS", start.ArgumentList);
        Assert.DoesNotContain("/SUPPRESSMSGBOXES", start.ArgumentList);
        Assert.DoesNotContain("/VERYSILENT", start.ArgumentList);
    }

    [Theory]
    [InlineData("update.exe")]
    [InlineData("C:relative.exe")]
    [InlineData("https://github.com/yikeugene/moye/releases/download/v1.2.0/update.exe")]
    [InlineData("C:\\downloads\\update.cmd")]
    public void NonExecutableOrRelativeSourcesAreRejected(string installer)
    {
        Assert.Throws<ArgumentException>(() => UpdateInstaller.CreateStartInfo(installer, _directory, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidParentProcessIdsAreRejected(int parentId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UpdateInstaller.CreateStartInfo(_installer, _directory, parentId));
    }

    [Fact]
    public void MissingInstallerNeverInvokesTheProcessLauncher()
    {
        var called = false;
        var service = new UpdateInstaller(_ => called = true);
        Assert.Throws<FileNotFoundException>(() => service.Launch(Path.Combine(_directory, "missing.exe")));
        Assert.False(called);
    }

    [Fact]
    public void MissingOrRelativeInstallLocationsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => UpdateInstaller.CreateStartInfo(_installer, "relative", 1));
        Assert.Throws<DirectoryNotFoundException>(() => UpdateInstaller.CreateStartInfo(_installer, Path.Combine(_directory, "missing"), 1));
    }

    [Fact]
    public void LaunchUsesCurrentProcessAndExistingApplicationDirectory()
    {
        ProcessStartInfo? received = null;
        new UpdateInstaller(start => received = start).Launch(_installer);
        Assert.NotNull(received);
        Assert.Contains("/PENROAMUPDATE=" + Environment.ProcessId, received.ArgumentList);
        Assert.Contains("/DIR=" + Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), received.ArgumentList);
    }

    [Fact]
    public void WindowsLaunchFailureReachesCallerSoItCanKeepNotesOpen()
    {
        var failure = new Win32Exception(5, "Access denied");
        var service = new UpdateInstaller(_ => throw failure);
        Assert.Same(failure, Assert.Throws<Win32Exception>(() => service.Launch(_installer)));
    }
}