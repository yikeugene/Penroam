using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Models;
using Moye.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Moye.Tests;

public sealed class PdfPaginationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PenroamPagination-" + Guid.NewGuid().ToString("N"));
    public PdfPaginationTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);
    private static double Mm(double value) => value * PdfExportPagination.DipsPerMillimeter;

    [Fact]
    public void PlanKeepsOriginalDimensionsAndMakesExactA4OneSheet()
    {
        var page = new NotePage { Width = Mm(210), Height = Mm(297) };
        var original = Assert.Single(PdfExportPagination.Plan([page], PdfExportOptions.Original));
        var tiled = Assert.Single(PdfExportPagination.Plan([page], new() { Layout = PdfExportLayout.A4Tiles }));
        Assert.Equal(original.Bounds, tiled.Bounds);
        Assert.Equal(page.Width, original.SheetWidth);
    }

    [Fact]
    public void PlanUsesReadingOrderOverlapAndFewestSheetOrientationWithoutChangingNotes()
    {
        var page = new NotePage { Width = Mm(380), Height = Mm(520) };
        var options = new PdfExportOptions { Layout = PdfExportLayout.A4Tiles, Orientation = PdfTileOrientation.Portrait, OverlapMillimeters = 10 };
        var tiles = PdfExportPagination.Plan([page], options);
        Assert.Equal(4, tiles.Count);
        Assert.Equal(new[] { (0, 0), (0, 1), (1, 0), (1, 1) }, tiles.Select(tile => (tile.Row, tile.Column)));
        Assert.Equal(Mm(200), tiles[1].Bounds.Left, 6); Assert.Equal(Mm(287), tiles[2].Bounds.Top, 6);
        Assert.True(tiles[3].Bounds.Contains(new Point(page.Width, page.Height)));
        var landscape = Assert.Single(PdfExportPagination.Plan([new() { Width = Mm(297), Height = Mm(210) }], options with { Orientation = PdfTileOrientation.Automatic }));
        Assert.Equal(Mm(297), landscape.SheetWidth); Assert.Equal(Mm(210), landscape.SheetHeight);
        Assert.Equal(Mm(380), page.Width); Assert.Equal(Mm(520), page.Height);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(31)]
    [InlineData(double.NaN)]
    public void InvalidOverlapIsRejectedBeforeExport(double overlap)
        => Assert.Throws<ArgumentException>(() => PdfExportPagination.Plan([new NotePage()], new() { Layout = PdfExportLayout.A4Tiles, OverlapMillimeters = overlap }));

    [Fact]
    public void UnboundedSheetCountIsRejectedWithoutPartialPlan()
        => Assert.Throws<ArgumentException>(() => PdfExportPagination.Plan([new NotePage { Width = 1e12, Height = 1e12 }], new() { Layout = PdfExportLayout.A4Tiles }));

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task TiledCroppedRotatedPdfPreservesActualScaleVectorsAnnotationsAndMarginInk(int rotation)
    {
        var source = await CreatePdf(rotation);
        using var repository = new MemoryRepository(); var service = new PdfService(repository);
        var page = Assert.Single(await service.ImportAsync(source));
        var sourceBitmap = await service.RenderAsync(page, 1);
        var redOriginal = Bounds(sourceBitmap, Red);
        page = PageExtensionService.Extend(page, new PageMargins(80, 60, 900, 800));
        page.InkData = await Sta(() =>
        {
            using var bytes = new MemoryStream();
            var strokes = new StrokeCollection
            {
                Line(20, 20, 40, 20), Line(page.Width - 40, page.Height - 30, page.Width - 20, page.Height - 30)
            };
            strokes.Save(bytes); return bytes.ToArray();
        });
        page.Texts.Add(new NoteText { X = page.Width - 260, Y = page.Height - 120, Width = 240, Height = 44, Text = "Margin writing", FontSize = 20, Color = "#FF990099" });
        var options = new PdfExportOptions { Layout = PdfExportLayout.A4Tiles, Orientation = PdfTileOrientation.Portrait, OverlapMillimeters = 10 };
        var plan = PdfExportPagination.Plan([page], options); Assert.Equal(4, plan.Count);
        var output = Path.Combine(_directory, "tiled.pdf"); await service.ExportAsync(output, new NotebookDocument { Pages = [page] }, options);
        var imported = await service.ImportAsync(output); Assert.Equal(plan.Count, imported.Count);
        using var searchable = UglyToad.PdfPig.PdfDocument.Open(output);
        using var vectorPdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        for (var index = 0; index < imported.Count; index++)
        {
            Assert.InRange(Math.Abs(imported[index].Width - Mm(210)), 0, .1);
            Assert.InRange(Math.Abs(imported[index].Height - Mm(297)), 0, .1);
            Assert.Equal(rotation, imported[index].Pdf!.Rotation);
            Assert.Contains("Original searchable text", searchable.GetPage(index + 1).Text);
            Assert.Single(vectorPdf.Pages[index].Elements.GetArray("/Annots")!.Elements);
            var bitmap = await service.RenderAsync(imported[index], 1);
            Assert.True(Bounds(bitmap, Green).IsEmpty, "Tiling must never expose hidden content outside the original crop.");
            if (index == 0)
            {
                var red = Bounds(bitmap, Red); Assert.False(red.IsEmpty);
                Assert.InRange(Math.Abs(red.Left - redOriginal.Left - 80), 0, 2);
                Assert.InRange(Math.Abs(red.Top - redOriginal.Top - 60), 0, 2);
                Assert.InRange(Math.Abs(red.Width - redOriginal.Width), 0, 2);
                Assert.InRange(Math.Abs(red.Height - redOriginal.Height), 0, 2);
                Assert.False(Bounds(bitmap, Orange).IsEmpty);
                Assert.False(Bounds(bitmap, Blue).IsEmpty);
            }
            if (index == plan.Count - 1)
            {
                var blue = Bounds(bitmap, Blue); Assert.False(blue.IsEmpty);
                Assert.InRange(Math.Abs(blue.Left - (page.Width - 43 - plan[index].Bounds.Left)), 0, 3);
                Assert.InRange(Math.Abs(blue.Top - (page.Height - 33 - plan[index].Bounds.Top)), 0, 3);
                Assert.False(Bounds(bitmap, Purple).IsEmpty);
            }
        }
    }

    [Fact]
    public async Task CancellationDuringTilingKeepsExistingFileAndCleansTemporaryFiles()
    {
        using var repository = new MemoryRepository(); var service = new PdfService(repository);
        var destination = Path.Combine(_directory, "existing.pdf"); byte[] existing = [11, 22, 33]; await File.WriteAllBytesAsync(destination, existing);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value => { if (value.Message.StartsWith("Preparing A4 sheet") && value.Completed == 1) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(destination,
            new NotebookDocument { Pages = [new NotePage { Width = Mm(400), Height = Mm(600) }] },
            new PdfExportOptions { Layout = PdfExportLayout.A4Tiles }, progress, cancellation.Token));
        Assert.Equal(existing, await File.ReadAllBytesAsync(destination)); Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task BlankPageTilingKeepsGuideGeometryAndOriginalExportDimensions()
    {
        using var repository = new MemoryRepository(); var service = new PdfService(repository);
        var page = PageExtensionService.Extend(new NotePage { Template = PaperTemplate.Ruled, Width = Mm(210), Height = Mm(297) }, new PageMargins(0, 0, Mm(100), 0));
        var original = Path.Combine(_directory, "original.pdf"); var tiles = Path.Combine(_directory, "paper.pdf");
        await service.ExportAsync(original, new NotebookDocument { Pages = [page] });
        await service.ExportAsync(tiles, new NotebookDocument { Pages = [page] }, new PdfExportOptions { Layout = PdfExportLayout.A4Tiles, Orientation = PdfTileOrientation.Portrait, OverlapMillimeters = 0 });
        var originalPage = Assert.Single(await service.ImportAsync(original)); Assert.InRange(Math.Abs(originalPage.Width - Mm(310)), 0, .1);
        var tiled = await service.ImportAsync(tiles); Assert.Equal(2, tiled.Count);
        var bitmap = await service.RenderAsync(tiled[1], 1);
        Assert.True(Bounds(bitmap, (r, g, b) => r < 245 || g < 245 || b < 245).IsEmpty, "The added margin and unused A4 area stay blank.");
    }

    private Task<string> CreatePdf(int rotation) => Sta(() =>
    {
        var path = Path.Combine(_directory, "source.pdf"); using var document = new PdfDocument(); var page = document.AddPage();
        page.MediaBox = new PdfRectangle(new XPoint(12, 18), new XPoint(372, 498));
        page.CropBox = new PdfRectangle(new XPoint(48, 66), new XPoint(324, 432)); page.Rotate = rotation;
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            graphics.DrawRectangle(XBrushes.Red, 110, 150, 18, 18);
            graphics.DrawRectangle(XBrushes.Lime, 16, 150, 18, 18);
            graphics.DrawString("Original searchable text", new XFont("Arial", 12), XBrushes.Black, 80, 230);
        }
        var appearance = new PdfDictionary(document); appearance.Elements.SetName("/Type", "/XObject"); appearance.Elements.SetName("/Subtype", "/Form");
        appearance.Elements.SetRectangle("/BBox", new PdfRectangle(new XPoint(0, 0), new XPoint(30, 30)));
        appearance.CreateStream(Encoding.ASCII.GetBytes("1 .4 0 rg 0 0 30 30 re f\n")); document.Internals.AddObject(appearance);
        var appearances = new PdfDictionary(document); appearances.Elements["/N"] = appearance.Reference!;
        var annotation = new PdfDictionary(document); annotation.Elements.SetName("/Type", "/Annot"); annotation.Elements.SetName("/Subtype", "/Square");
        annotation.Elements.SetRectangle("/Rect", new PdfRectangle(new XPoint(210, 300), new XPoint(240, 330))); annotation.Elements["/AP"] = appearances;
        document.Internals.AddObject(annotation); var annotations = new PdfArray(document); annotations.Elements.Add(annotation.Reference!); page.Elements["/Annots"] = annotations;
        document.Save(path); return path;
    });

    private static Stroke Line(double x1, double y1, double x2, double y2) => new(new StylusPointCollection { new StylusPoint(x1, y1), new StylusPoint(x2, y2) }, new DrawingAttributes { Color = Colors.Blue, Width = 6, Height = 6, IgnorePressure = true });
    private static bool Red(byte r, byte g, byte b) => r > 190 && g < 70 && b < 70;
    private static bool Green(byte r, byte g, byte b) => r < 70 && g > 190 && b < 70;
    private static bool Blue(byte r, byte g, byte b) => r < 70 && g < 70 && b > 190;
    private static bool Orange(byte r, byte g, byte b) => r > 190 && g is > 70 and < 150 && b < 60;
    private static bool Purple(byte r, byte g, byte b) => r is > 90 and < 190 && b is > 90 and < 190 && g < 60;
    private static Rect Bounds(BitmapSource bitmap, Func<byte, byte, byte, bool> matches)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        converted.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); var bounds = Rect.Empty;
        for (var y = 0; y < bitmap.PixelHeight; y++) for (var x = 0; x < bitmap.PixelWidth; x++)
        { var offset = (y * bitmap.PixelWidth + x) * 4; if (matches(pixels[offset + 2], pixels[offset + 1], pixels[offset])) bounds.Union(new Rect(x, y, 1, 1)); }
        return bounds;
    }
    private static Task<T> Sta<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(action()); } catch (Exception ex) { completion.SetException(ex); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private sealed class InlineProgress(Action<DocumentProgress> report) : IProgress<DocumentProgress> { public void Report(DocumentProgress value) => report(value); }
    private sealed class MemoryRepository : INotebookRepository
    {
        private readonly Dictionary<string, AssetData> _assets = [];
        public Task<AssetData> PutAssetAsync(string fileName, string contentType, byte[] bytes) { var asset = new AssetData(Guid.NewGuid().ToString("N"), fileName, contentType, bytes); _assets.Add(asset.Id, asset); return Task.FromResult(asset); }
        public Task<AssetData> GetAssetAsync(string id) => Task.FromResult(_assets[id]);
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>([]);
        public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult<NotebookDocument?>(null);
        public Task SaveAsync(NotebookDocument document) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
        public void Dispose() { }
    }
}
