using System.IO;
using System.Security.Cryptography;

namespace Moye.Services;

/// <summary>Keep the verified payload locked while saving and handing it to Setup.</summary>
public sealed class UpdateInstallCoordinator(IUpdateInstaller installer)
{
    public async Task InstallAsync(AppUpdate update, string path, Func<Task> saveAsync, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saveAsync);
        if (update.Sha256 is not { Length: 64 } expected || !expected.All(Uri.IsHexDigit))
            throw new InvalidDataException("The update has no valid verification checksum.");
        await using var payload = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (payload.Length != update.Size)
            throw new InvalidDataException("The downloaded update is incomplete. Please download it again.");
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(payload, cancellationToken));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded update could not be verified. Please download it again.");
        await saveAsync();
        cancellationToken.ThrowIfCancellationRequested();
        installer.Launch(path);
    }
}
