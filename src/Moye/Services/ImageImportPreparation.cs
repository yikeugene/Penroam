using System.Windows.Media.Imaging;
using L = Moye.Localization.LocalizationService;

namespace Moye.Services;

internal sealed record PreparedImageImport(int PixelWidth, int PixelHeight, BitmapSource Preview);

/// <summary>Validate source dimensions before decoding a bounded display preview; keep original bytes for storage.</summary>
internal static class ImageImportPreparation
{
    internal const int MaximumPreviewDimension = 2048;

    internal static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 20000 || height > 20000 || (long)width * height > 80_000_000)
            throw new InvalidDataException(L.Text("Core.ImageTooLarge"));
    }

    internal static Task<PreparedImageImport> PrepareAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        AttachmentLimits.ValidateLength(bytes.LongLength);
        return Task.Run(() =>
        {
            int width, height;
            using (var metadata = new MemoryStream(bytes, false))
            {
                var decoder = BitmapDecoder.Create(metadata, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
                width = decoder.Frames[0].PixelWidth;
                height = decoder.Frames[0].PixelHeight;
                ValidateDimensions(width, height);
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(bytes, false);
            var preview = new BitmapImage();
            preview.BeginInit();
            preview.CacheOption = BitmapCacheOption.OnLoad;
            var scale = Math.Min(1, (double)MaximumPreviewDimension / Math.Max(width, height));
            preview.DecodePixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
            preview.DecodePixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
            preview.StreamSource = stream;
            preview.EndInit();
            preview.Freeze();
            cancellationToken.ThrowIfCancellationRequested();
            return new PreparedImageImport(width, height, preview);
        }, cancellationToken);
    }
}
