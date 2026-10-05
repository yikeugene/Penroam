using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using Moye.Models;

namespace Moye.Services;

public static class PageExtensionService
{
    // 200 inches at 96 DIPs/inch stays within the standard 14,400-point PDF page limit.
    public const double MaxDimension = 19_200;
    public const double DipsPerMillimeter = 96 / 25.4;

    public static Rect GetPdfBounds(NotePage page) => page.Pdf is { } pdf
        ? new Rect(pdf.OffsetX, pdf.OffsetY, pdf.DisplayWidth > 0 ? pdf.DisplayWidth : page.Width,
            pdf.DisplayHeight > 0 ? pdf.DisplayHeight : page.Height)
        : new Rect(0, 0, page.Width, page.Height);

    public static bool CanExtend(NotePage page, PageMargins margins)
    {
        static bool Addition(double value) => double.IsFinite(value) && value >= 0;
        return Addition(margins.Left) && Addition(margins.Top) && Addition(margins.Right) && Addition(margins.Bottom)
            && (margins.Left > 0 || margins.Top > 0 || margins.Right > 0 || margins.Bottom > 0)
            && double.IsFinite(page.Width) && page.Width > 0 && double.IsFinite(page.Height) && page.Height > 0
            && page.Width + margins.Left + margins.Right <= MaxDimension
            && page.Height + margins.Top + margins.Bottom <= MaxDimension
            && PdfPagePlacement.IsValid(page) && PaperPagePlacement.IsValid(page);
    }

    /// <summary>Returns a complete new snapshot, so a failed ink decode cannot leave a partially extended page.</summary>
    public static NotePage Extend(NotePage original, PageMargins margins)
    {
        if (!CanExtend(original, margins))
            throw new ArgumentException("Add a positive margin and keep each page dimension within 5,080 mm.", nameof(margins));

        var page = original.Snapshot();
        if (page.Pdf is { } pdf)
        {
            var bounds = GetPdfBounds(original);
            pdf.DisplayWidth = bounds.Width; pdf.DisplayHeight = bounds.Height;
            pdf.OffsetX = bounds.X + margins.Left; pdf.OffsetY = bounds.Y + margins.Top;
        }
        else
        {
            var layout = original.PaperLayout ?? new PaperPageLayout { Width = original.Width, Height = original.Height };
            page.PaperLayout = layout with { X = layout.X + margins.Left, Y = layout.Y + margins.Top };
        }
        page.Width += margins.Left + margins.Right;
        page.Height += margins.Top + margins.Bottom;
        if (margins.Left == 0 && margins.Top == 0) return page;

        foreach (var text in page.Texts) { text.X += margins.Left; text.Y += margins.Top; }
        foreach (var image in page.Images) { image.X += margins.Left; image.Y += margins.Top; }
        if (page.InkData.Length > 0)
        {
            using var source = new MemoryStream(page.InkData, false);
            var strokes = new StrokeCollection(source);
            strokes.Transform(new Matrix(1, 0, 0, 1, margins.Left, margins.Top), false);
            using var output = new MemoryStream();
            strokes.Save(output);
            page.InkData = output.ToArray();
        }
        return page;
    }
}
