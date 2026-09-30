using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Ink;
using Moye.Models;

namespace Moye.Controls;

/// <summary>A bounded, versioned clipboard document. Assets make it independent of the source library.</summary>
public sealed class NoteSelection
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    public int Version { get; set; } = 1;
    public byte[] InkData { get; set; } = [];
    public List<NoteText> Texts { get; set; } = [];
    public List<NoteImage> Images { get; set; } = [];
    public List<AssetData> Assets { get; set; } = [];
    public bool IsEmpty => InkData.Length == 0 && Texts.Count == 0 && Images.Count == 0;

    public byte[] Encode()
    {
        Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, DocumentJson.Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("This selection is too large for the clipboard. Copy fewer objects.");
        return bytes;
    }

    public static NoteSelection Decode(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumBytes) throw new InvalidDataException("Unsupported clipboard selection size.");
        var result = JsonSerializer.Deserialize<NoteSelection>(bytes, DocumentJson.Options) ?? throw new InvalidDataException("Empty clipboard selection.");
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if (Version != 1 || InkData is null || Texts is null || Images is null || Assets is null || Texts.Count + Images.Count > 10000)
            throw new InvalidDataException("Unsupported clipboard selection.");
        if (InkData.LongLength + Assets.Sum(a => (long)(a?.Bytes?.Length ?? 0)) > MaximumBytes)
            throw new InvalidDataException("This selection is too large for the clipboard.");
        if (InkData.Length > 0)
        {
            var strokes = new StrokeCollection(new MemoryStream(InkData, false));
            var bounds = strokes.GetBounds();
            if (strokes.Count == 0 || bounds.IsEmpty || !Finite(bounds.X, bounds.Y, bounds.Width, bounds.Height))
                throw new InvalidDataException("Invalid clipboard ink.");
        }
        foreach (var text in Texts)
        {
            if (text is null || !Geometry(text.X, text.Y, text.Width, text.Height) || !double.IsFinite(text.FontSize) || text.FontSize is < 6 or > 128 || text.Text is null || text.Text.Length > 2_000_000 || string.IsNullOrWhiteSpace(text.FontFamily) || text.FontFamily.Length > 256 || !Enum.IsDefined(text.Alignment))
                throw new InvalidDataException("Invalid clipboard text.");
            _ = new System.Windows.Media.FontFamily(text.FontFamily);
            _ = System.Windows.Media.ColorConverter.ConvertFromString(text.Color);
        }
        var assetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in Assets)
        {
            if (asset is null || string.IsNullOrWhiteSpace(asset.Id) || !assetIds.Add(asset.Id) || asset.Bytes is null || asset.Bytes.Length == 0 || asset.ContentType is not ("image/png" or "image/jpeg"))
                throw new InvalidDataException("Invalid clipboard image asset.");
            using var stream = new MemoryStream(asset.Bytes, false);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.OnDemand);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth > 20000 || frame.PixelHeight > 20000 || (long)frame.PixelWidth * frame.PixelHeight > 80_000_000)
                throw new InvalidDataException("Clipboard image exceeds the supported dimensions.");
        }
        foreach (var image in Images)
            if (image is null || !Geometry(image.X, image.Y, image.Width, image.Height) || !assetIds.Contains(image.AssetId))
                throw new InvalidDataException("Invalid clipboard image.");
    }

    public Rect Bounds()
    {
        var bounds = InkData.Length > 0 ? new StrokeCollection(new MemoryStream(InkData, false)).GetBounds() : Rect.Empty;
        foreach (var text in Texts) bounds.Union(new Rect(text.X, text.Y, text.Width, text.Height));
        foreach (var image in Images) bounds.Union(new Rect(image.X, image.Y, image.Width, image.Height));
        return bounds;
    }

    private static bool Geometry(double x, double y, double width, double height) => Finite(x, y, width, height) && width > 0 && height > 0;
    private static bool Finite(params double[] values) => values.All(value => double.IsFinite(value) && Math.Abs(value) <= 1_000_000);
}
