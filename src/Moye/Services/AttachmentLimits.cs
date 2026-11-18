using L = Moye.Localization.LocalizationService;

namespace Moye.Services;

/// <summary>Keep imported originals within the size supported by editable notebook backups.</summary>
public static class AttachmentLimits
{
    public const int MaximumBytes = 512 * 1024 * 1024;

    public static void ValidateLength(long length)
    {
        if (length > MaximumBytes)
            throw new InvalidDataException(L.Text("Messages.Import.AttachmentTooLarge"));
    }

    public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        // Keep the same read-only handle open through the read, so the file cannot
        // be replaced or enlarged between the size check and the allocation.
        ValidateLength(stream.Length);
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }
}
