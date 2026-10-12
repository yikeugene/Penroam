using System.Windows;
using Moye.Models;

namespace Moye.Services;

public enum PdfExportLayout { OriginalSize, A4Tiles }
public enum PdfTileOrientation { Automatic, Portrait, Landscape }

/// <summary>Printing options are independent of saved notebook geometry.</summary>
public sealed record PdfExportOptions
{
    public PdfExportLayout Layout { get; init; }
    public PdfTileOrientation Orientation { get; init; }
    public double OverlapMillimeters { get; init; } = 10;
    public static PdfExportOptions Original { get; } = new();
}

public sealed record PdfExportTile(int SourcePageIndex, int Row, int Column, Rect Bounds, double SheetWidth, double SheetHeight);

public static class PdfExportPagination
{
    public const int MaximumSheets = 1000;
    public const double DipsPerMillimeter = 96d / 25.4;
    private const double Tolerance = .1;

    public static IReadOnlyList<PdfExportTile> Plan(IReadOnlyList<NotePage> pages, PdfExportOptions options)
    {
        if (!Enum.IsDefined(options.Layout) || !Enum.IsDefined(options.Orientation))
            throw new ArgumentException("Choose a supported PDF layout and orientation.");
        if (!double.IsFinite(options.OverlapMillimeters) || options.OverlapMillimeters < 0 || options.OverlapMillimeters > 30)
            throw new ArgumentException("Overlap must be between 0 and 30 mm.");
        var result = new List<PdfExportTile>();
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            if (!double.IsFinite(page.Width) || !double.IsFinite(page.Height) || page.Width <= 0 || page.Height <= 0)
                throw new ArgumentException("A page has invalid dimensions.");
            if (options.Layout == PdfExportLayout.OriginalSize)
            {
                result.Add(new(index, 0, 0, new Rect(0, 0, page.Width, page.Height), page.Width, page.Height));
                continue;
            }
            var overlap = options.OverlapMillimeters * DipsPerMillimeter;
            var portrait = Grid(page, 210 * DipsPerMillimeter, 297 * DipsPerMillimeter, overlap);
            var landscape = Grid(page, 297 * DipsPerMillimeter, 210 * DipsPerMillimeter, overlap);
            var selected = options.Orientation switch
            {
                PdfTileOrientation.Portrait => portrait,
                PdfTileOrientation.Landscape => landscape,
                _ => landscape.Columns * (long)landscape.Rows < portrait.Columns * (long)portrait.Rows ? landscape : portrait
            };
            if (selected.Columns * (long)selected.Rows > MaximumSheets - result.Count)
                throw new ArgumentException($"This export needs more than {MaximumSheets} sheets. Select fewer pages or reduce the overlap.");
            // Reading order is left to right, then top to bottom, within each source page.
            for (var row = 0; row < selected.Rows; row++)
            for (var column = 0; column < selected.Columns; column++)
                result.Add(new(index, row, column,
                    new Rect(column * (selected.Width - overlap), row * (selected.Height - overlap), selected.Width, selected.Height),
                    selected.Width, selected.Height));
        }
        return result;
    }

    private static (int Columns, int Rows, double Width, double Height) Grid(NotePage page, double width, double height, double overlap)
    {
        var columns = Math.Ceiling(Math.Max(0, page.Width - width - Tolerance) / (width - overlap)) + 1;
        var rows = Math.Ceiling(Math.Max(0, page.Height - height - Tolerance) / (height - overlap)) + 1;
        return ((int)Math.Min(MaximumSheets + 1, columns), (int)Math.Min(MaximumSheets + 1, rows), width, height);
    }
}
