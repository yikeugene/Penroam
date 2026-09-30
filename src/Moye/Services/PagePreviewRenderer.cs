using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Models;

namespace Moye.Services;

/// <summary>Draws a bounded, read-only page image including annotations; does not create an editable canvas.</summary>
public sealed class PagePreviewRenderer(IPdfService pdf, Func<string, Task<AssetData>> loadAsset)
{
    public async Task<BitmapSource> RenderAsync(NotePage original, double width, CancellationToken cancellationToken = default)
    {
        var page = original.Snapshot();
        var scale = Math.Min(Math.Clamp(width, 48, 1600) / page.Width,
            Math.Min(2200 / page.Height, Math.Sqrt(3_000_000d / (page.Width * page.Height))));
        var background = await pdf.RenderAsync(page, scale, cancellationToken);
        var images = new List<(NoteImage Placement, BitmapSource Image)>();
        foreach (var image in page.Images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asset = await loadAsset(image.AssetId);
            using var stream = new MemoryStream(asset.Bytes, false);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = Math.Clamp((int)Math.Ceiling(image.Width * scale), 1, 1600);
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            images.Add((image, bitmap));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, page.Width, page.Height)));
            dc.DrawImage(background, new Rect(0, 0, page.Width, page.Height));
            foreach (var image in images) dc.DrawImage(image.Image, new Rect(image.Placement.X, image.Placement.Y, image.Placement.Width, image.Placement.Height));
            foreach (var text in page.Texts)
            {
                if (string.IsNullOrEmpty(text.Text)) continue;
                var formatted = new FormattedText(text.Text, CultureInfo.GetCultureInfo("zh-HK"), FlowDirection.LeftToRight,
                    new Typeface(new FontFamily(text.FontFamily), text.Italic ? FontStyles.Italic : FontStyles.Normal,
                        text.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), text.FontSize,
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString(text.Color)), 1)
                {
                    MaxTextWidth = NoteTextLayout.ContentWidth(text.Width), MaxTextHeight = Math.Max(1, text.Height + text.FontSize * 1.4),
                    LineHeight = text.FontSize * 1.4,
                    TextAlignment = text.Alignment switch { NoteTextAlignment.Center => TextAlignment.Center, NoteTextAlignment.Right => TextAlignment.Right, _ => TextAlignment.Left }
                };
                dc.PushClip(new RectangleGeometry(new Rect(text.X, text.Y, text.Width, text.Height)));
                dc.DrawText(formatted, new Point(text.X + NoteTextLayout.HorizontalInset, text.Y)); dc.Pop();
            }
            if (page.InkData.Length > 0)
            {
                using var stream = new MemoryStream(page.InkData, false);
                new StrokeCollection(stream).Draw(dc);
            }
            dc.Pop(); dc.Pop();
        }
        var result = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(page.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(page.Height * scale)), 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }
}
