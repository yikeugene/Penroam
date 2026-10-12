using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Moye.Services;

public interface IUpdateInstaller
{
    void Launch(string installerPath);
}

/// <summary>Hands a verified installer to Windows after the caller has saved the library.</summary>
public sealed class UpdateInstaller : IUpdateInstaller
{
    private readonly Action<ProcessStartInfo> _start;

    public UpdateInstaller(Action<ProcessStartInfo>? start = null) => _start = start ?? StartProcess;

    public void Launch(string installerPath) => _start(CreateStartInfo(installerPath, AppContext.BaseDirectory, Environment.ProcessId));

    public static ProcessStartInfo CreateStartInfo(string installerPath, string installDirectory, int parentProcessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        if (!Path.IsPathFullyQualified(installerPath) || !Path.GetExtension(installerPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The update installer must be an absolute executable path.", nameof(installerPath));
        if (!Path.IsPathFullyQualified(installDirectory))
            throw new ArgumentException("The installation directory must be an absolute path.", nameof(installDirectory));
        if (parentProcessId <= 0) throw new ArgumentOutOfRangeException(nameof(parentProcessId));
        installerPath = Path.GetFullPath(installerPath);
        installDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
        if (!File.Exists(installerPath)) throw new FileNotFoundException("The downloaded update installer is missing. Download it again.", installerPath);
        if (!Directory.Exists(installDirectory)) throw new DirectoryNotFoundException("The current Penroam installation directory is missing.");

        var start = new ProcessStartInfo(installerPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installerPath)!
        };
        // ArgumentList protects spaces and Unicode paths without building a shell command.
        // The installer waits for this exact process to exit and never forces notes to close.
        start.ArgumentList.Add("/SILENT");
        start.ArgumentList.Add("/SP-");
        start.ArgumentList.Add("/NORESTART");
        start.ArgumentList.Add("/NOCLOSEAPPLICATIONS");
        start.ArgumentList.Add("/NOFORCECLOSEAPPLICATIONS");
        start.ArgumentList.Add("/NORESTARTAPPLICATIONS");
        start.ArgumentList.Add("/PENROAMUPDATE=" + parentProcessId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("/DIR=" + installDirectory);
        return start;
    }

    private static void StartProcess(ProcessStartInfo start)
    {
        using var process = Process.Start(start);
        if (process is null) throw new IOException("Windows could not start the update installer. Your notes remain open; try updating again.");
    }
}