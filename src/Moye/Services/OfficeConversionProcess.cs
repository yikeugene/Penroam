using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using L = Moye.Localization.LocalizationService;

namespace Moye.Services;

/// <summary>Bounds Office automation in a separate process, keeping the editor responsive.</summary>
public sealed class OfficeConversionProcess : IOfficePdfConverter
{
    private readonly TimeSpan _timeout;
    public OfficeConversionProcess(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public static bool IsAvailableFor(string extension)
    {
        if (!OfficeDocumentValidation.SupportsExtension(extension)) return false;
        try { return Type.GetTypeFromProgID(extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) ? "Word.Application" : "PowerPoint.Application", false) is not null; }
        catch (Exception exception) when (exception is System.Security.SecurityException or COMException) { return false; }
    }

    public async Task ConvertAsync(string sourcePath, string outputPdfPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sourcePath = Path.GetFullPath(sourcePath); outputPdfPath = Path.GetFullPath(outputPdfPath);
        await Task.Run(() => OfficeDocumentValidation.Validate(sourcePath, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (!Path.GetExtension(outputPdfPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(L.Text("Messages.Office.PdfOutputRequired"));
        if (File.Exists(outputPdfPath)) throw new IOException(L.Text("Messages.Office.OutputExists"));
        if (!IsAvailableFor(Path.GetExtension(sourcePath)))
            throw new InvalidOperationException(L.Text("Messages.Office.DesktopAppMissing"));
        using var process = new Process { StartInfo = CreateStartInfo(ResolveWorkerExecutable(), sourcePath, outputPdfPath) };
        if (!process.Start()) throw new IOException(L.Text("Messages.Office.WorkerStartFailed"));
        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try { await process.WaitForExitAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            await StopWorkerAsync(process).ConfigureAwait(false);
            try { await Task.WhenAll(errorTask, outputTask).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or TimeoutException) { }
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new TimeoutException(L.Text("Messages.Office.Timeout"));
        }
        var error = (await errorTask.ConfigureAwait(false)).Trim();
        await outputTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidDataException(string.IsNullOrWhiteSpace(error)
                ? L.Text("Messages.Office.ConversionFailed")
                : error.Length <= 4096 ? error : error[..4096]);
        if (!File.Exists(outputPdfPath) || new FileInfo(outputPdfPath).Length < 5)
            throw new InvalidDataException(L.Text("Messages.Office.NoReadablePdf"));
        var signature = new byte[5];
        using var pdf = File.OpenRead(outputPdfPath);
        await pdf.ReadExactlyAsync(signature, cancellationToken).ConfigureAwait(false);
        if (!signature.AsSpan().SequenceEqual("%PDF-"u8)) throw new InvalidDataException(L.Text("Messages.Office.InvalidPdf"));
    }

    private static ProcessStartInfo CreateStartInfo(string workerExecutable, string sourcePath, string outputPdfPath)
    {
        var start = new ProcessStartInfo(workerExecutable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true, RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPdfPath))!
        };
        // Paths are separate arguments, never interpolated into a shell command.
        start.ArgumentList.Add("--convert-office");
        start.ArgumentList.Add(sourcePath); start.ArgumentList.Add(outputPdfPath);
        return start;
    }

    private static string ResolveWorkerExecutable()
    {
        // Environment.ProcessPath can point at testhost or dotnet. The worker
        // is always the apphost beside Penroam.dll (also used in the portable ZIP).
        var assembly = typeof(OfficeConversionProcess).Assembly.Location;
        var executable = string.IsNullOrEmpty(assembly) ? null : Path.ChangeExtension(assembly, ".exe");
        if (executable is not null && File.Exists(executable)) return executable;
        throw new FileNotFoundException(L.Text("Messages.Office.WorkerMissing"));
    }

    private static async Task StopWorkerAsync(Process process)
    {
        try
        {
            // Kill only the exact worker we started. Never terminate the Office
            // application, its process tree, or a process found by its name.
            if (!process.HasExited) process.Kill(entireProcessTree: false);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException) { }
    }
}
