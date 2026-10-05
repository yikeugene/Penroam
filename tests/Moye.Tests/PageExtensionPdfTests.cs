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
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;

namespace Moye.Tests;

public sealed class PageExtensionPdfTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PenroamExtensionPdf-" + Guid.NewGuid().ToString("N"));
    public PageExtensionPdfTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task ExtendedCroppedRotatedPdfKeepsOriginalScaleAndAnnotationsWithWritableMargins(int rotation)
    {
        var source = await CreateSourceAsync(rotation);
        using var repository = new MemoryRepository();
        var service = new PdfService(repository);
        var note = Assert.Single(await service.ImportAsync(source));
        var originalBitmap = await service.RenderAsync(note, 1);
        var redBefore = Bounds(originalBitmap, (r, g, b) => r > 190 && g < 70 && b < 70);
        var orangeBefore = Bounds(originalBitmap, Orange);
        Assert.True(redBefore.Width > 10);
        Assert.True(orangeBefore.Width > 5);
        Assert.True(Bounds(originalBitmap, Green).IsEmpty);
        var originalWidth = note.Width;
        var originalHeight = note.Height;
        Expand(note, 80, 60, 120, 100);
        // Expanding the writing canvas reuses the source raster, at the same pixel density.
        Assert.Same(originalBitmap, await service.RenderAsync(note, 1));
        note.InkData = await Sta(() => Serialize(
            Line(20, 20, 50, 20), Line(note.Width - 50, 20, note.Width - 20, 20),
            Line(20, note.Height - 20, 50, note.Height - 20),
            Line(note.Width - 50, note.Height - 20, note.Width - 20, note.Height - 20)));
        note.Texts.Add(new NoteText { X = 100, Y = 14, Width = 180, Height = 36, FontSize = 20,
            Text = "Margin notes", Color = "#FF990099" });
        var output = Path.Combine(_directory, "expanded.pdf");
        await service.ExportAsync(output, new NotebookDocument { Pages = [note] });
        var roundTrip = Assert.Single(await service.ImportAsync(output));
        Assert.InRange(Math.Abs(roundTrip.Width - (originalWidth + 200)), 0, .1);
        Assert.InRange(Math.Abs(roundTrip.Height - (originalHeight + 160)), 0, .1);
        Assert.Equal(rotation, roundTrip.Pdf!.Rotation);
        var bitmap = await service.RenderAsync(roundTrip, 1);
        AssertShiftedBounds(redBefore, Bounds(bitmap, (r, g, b) => r > 190 && g < 70 && b < 70), 80, 60);
        AssertShiftedBounds(orangeBefore, Bounds(bitmap, Orange), 80, 60);
        Assert.InRange(Math.Abs(Count(originalBitmap, Orange) - Count(bitmap, Orange)), 0, 100);
        Assert.True(Bounds(bitmap, Green).IsEmpty, "Extending a cropped PDF must not reveal formerly hidden source content.");
        var blue = Bounds(bitmap, (r, g, b) => b > 180 && r < 80 && g < 80);
        Assert.InRange(blue.Left, 15, 20);
        Assert.InRange(blue.Top, 15, 20);
        Assert.InRange(blue.Right, note.Width - 20, note.Width - 15);
        Assert.InRange(blue.Bottom, note.Height - 20, note.Height - 15);
        var text = Bounds(bitmap, (r, g, b) => r is > 90 and < 190 && b is > 90 and < 190 && g < 60);
        Assert.True(text.Width > 80 && text.Bottom < 60, "Typed notes must export in the new top margin.");
        using var originalPdf = PdfReader.Open(source, PdfDocumentOpenMode.Import);
        using var exported = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        var originalAnnotations = originalPdf.Pages[0].Elements.GetArray("/Annots")!;
        var exportedAnnotations = exported.Pages[0].Elements.GetArray("/Annots")!;
        Assert.Equal(originalAnnotations.Elements.Count, exportedAnnotations.Elements.Count);
        for (var index = 0; index < originalAnnotations.Elements.Count; index++)
            Assert.Equal(originalAnnotations.Elements.GetDictionary(index)!.Elements.GetRectangle("/Rect"),
                exportedAnnotations.Elements.GetDictionary(index)!.Elements.GetRectangle("/Rect"));
        Assert.NotEqual(0, exportedAnnotations.Elements.GetDictionary(2)!.Elements.GetInteger("/F") & 2);
        using var searchable = UglyToad.PdfPig.PdfDocument.Open(output);
        Assert.Contains("Original searchable text", searchable.GetPage(1).Text);
    }

    [Fact]
    public async Task DuplicatedSourcePagesAndRepeatedExportHaveIndependentExtensions()
    {
        var source = await CreateSourceAsync(90);
        using var repository = new MemoryRepository();
        var service = new PdfService(repository);
        var original = Assert.Single(await service.ImportAsync(source));
        var before = await service.RenderAsync(original, 1);
        var expected = Bounds(before, (r, g, b) => r > 190 && g < 70 && b < 70);
        var left = original.Snapshot(); Expand(left, 120, 0, 0, 0);
        var bottom = original.Snapshot(); Expand(bottom, 0, 0, 0, 160);
        var document = new NotebookDocument { Pages = [left, original, bottom, left.Snapshot()] };
        var firstPath = Path.Combine(_directory, "first.pdf");
        var secondPath = Path.Combine(_directory, "second.pdf");
        await service.ExportAsync(firstPath, document);
        await service.ExportAsync(secondPath, document);
        var first = await service.ImportAsync(firstPath);
        var second = await service.ImportAsync(secondPath);
        Assert.Equal(4, first.Count);
        for (var index = 0; index < first.Count; index++)
        {
            var image = await service.RenderAsync(first[index], 1);
            Assert.Equal(Pixels(image), Pixels(await service.RenderAsync(second[index], 1)));
            AssertShiftedBounds(expected, Bounds(image, (r, g, b) => r > 190 && g < 70 && b < 70), index is 0 or 3 ? 120 : 0, 0);
        }
        Assert.Equal(Pixels(before), Pixels(await service.RenderAsync(first[1], 1)));
        Assert.Equal(original.Width, original.Pdf!.DisplayWidth == 0 ? original.Width : original.Pdf.DisplayWidth);
        Assert.Equal(0, original.Pdf.OffsetX);
    }

    [Fact]
    public async Task ReimportedExtendedPageCanBeExtendedAgainWithoutMovingExistingMargins()
    {
        using var repository = new MemoryRepository();
        var service = new PdfService(repository);
        var note = Assert.Single(await service.ImportAsync(await CreateSourceAsync(270)));
        Expand(note, 80, 60, 0, 0);
        note.InkData = await Sta(() => Serialize(Line(15, 20, 50, 20)));
        var firstPath = Path.Combine(_directory, "first-extension.pdf");
        await service.ExportAsync(firstPath, new NotebookDocument { Pages = [note] });
        var reimported = Assert.Single(await service.ImportAsync(firstPath));
        var firstBitmap = await service.RenderAsync(reimported, 1);
        var firstBlue = Bounds(firstBitmap, (r, g, b) => b > 180 && r < 80 && g < 80);
        Expand(reimported, 40, 100, 80, 120);
        var secondPath = Path.Combine(_directory, "second-extension.pdf");
        await service.ExportAsync(secondPath, new NotebookDocument { Pages = [reimported] });
        var final = Assert.Single(await service.ImportAsync(secondPath));
        var bitmap = await service.RenderAsync(final, 1);
        AssertShiftedBounds(firstBlue, Bounds(bitmap, (r, g, b) => b > 180 && r < 80 && g < 80), 40, 100);
        Assert.InRange(Math.Abs(final.Width - reimported.Width), 0, .1);
        Assert.InRange(Math.Abs(final.Height - reimported.Height), 0, .1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundaryAnnotationWithoutAppearanceFailsSafelyOnlyWhenPageIsExtended(bool emptyAppearance)
    {
        using var repository = new MemoryRepository();
        var service = new PdfService(repository);
        var note = Assert.Single(await service.ImportAsync(await CreateSourceAsync(0, true, emptyAppearance)));
        var destination = Path.Combine(_directory, "existing.pdf");
        await service.ExportAsync(destination, new NotebookDocument { Pages = [note] });
        var originalBytes = await File.ReadAllBytesAsync(destination);
        Expand(note, 100, 0, 0, 0);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ExportAsync(destination, new NotebookDocument { Pages = [note] }));
        Assert.Contains("Flatten", exception.Message);
        Assert.Contains("undo the page extension", exception.Message);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    private Task<string> CreateSourceAsync(int rotation, bool edgeWithoutAppearance = false, bool emptyAppearance = false) => Sta(() =>
    {
        var path = Path.Combine(_directory, "source.pdf");
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.MediaBox = new PdfRectangle(new XPoint(12, 18), new XPoint(372, 498));
        page.CropBox = new PdfRectangle(new XPoint(48, 66), new XPoint(324, 432));
        page.Rotate = rotation;
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            graphics.DrawRectangle(XBrushes.Red, 110, 150, 18, 18);
            graphics.DrawRectangle(XBrushes.Lime, 16, 150, 18, 18); // Outside the original crop.
            graphics.DrawString("Original searchable text", new XFont("Arial", 12), XBrushes.Black, 80, 230);
        }
        page.Annotations.Add(new PdfTextAnnotation(document)
        { Rectangle = new PdfRectangle(new XPoint(150, 260), new XPoint(175, 285)), Contents = "Original note",
            Icon = PdfTextAnnotationIcon.Comment, Color = XColors.Yellow });
        var sharedAppearance = AddAppearanceAnnotation(document, page, new PdfRectangle(new XPoint(30, 320), new XPoint(70, 350)), "1 .4 0 rg");
        AddAppearanceAnnotation(document, page, new PdfRectangle(new XPoint(14, 100), new XPoint(34, 125)), "0 1 0 rg");
        AddAppearanceAnnotation(document, page, new PdfRectangle(new XPoint(200, 320), new XPoint(240, 350)), "1 .4 0 rg", sharedAppearance);
        if (edgeWithoutAppearance)
        {
            var annotation = new PdfTextAnnotation(document)
            { Rectangle = new PdfRectangle(new XPoint(30, 180), new XPoint(60, 210)), Contents = "Partially cropped reader-generated icon" };
            if (emptyAppearance)
            {
                var appearances = new PdfDictionary(document);
                appearances.Elements["/N"] = new PdfDictionary(document);
                annotation.Elements["/AP"] = appearances;
            }
            page.Annotations.Add(annotation);
        }
        document.Save(path);
        return path;
    });

    private static PdfDictionary AddAppearanceAnnotation(PdfDocument document, PdfPage page, PdfRectangle bounds, string color,
        PdfDictionary? sharedAppearance = null)
    {
        var appearance = new PdfDictionary(document);
        appearance.Elements.SetName("/Type", "/XObject");
        appearance.Elements.SetName("/Subtype", "/Form");
        appearance.Elements.SetRectangle("/BBox", new PdfRectangle(new XPoint(0, 0), new XPoint(40, 30)));
        var matrix = new PdfArray(document);
        foreach (var value in new double[] { 2, 0, 0, 3, 7, 11 }) matrix.Elements.Add(new PdfReal(value));
        appearance.Elements["/Matrix"] = matrix;
        appearance.CreateStream(Encoding.ASCII.GetBytes(color + " 0 0 40 30 re f\n"));
        document.Internals.AddObject(appearance);
        var appearances = new PdfDictionary(document);
        appearances.Elements["/N"] = appearance.Reference!;
        if (sharedAppearance is not null) appearances = sharedAppearance;
        else document.Internals.AddObject(appearances);
        var annotation = new PdfDictionary(document);
        annotation.Elements.SetName("/Type", "/Annot");
        annotation.Elements.SetName("/Subtype", "/Square");
        annotation.Elements.SetRectangle("/Rect", bounds);
        annotation.Elements["/AP"] = appearances.Reference!;
        document.Internals.AddObject(annotation);
        page.Elements.GetArray("/Annots")!.Elements.Add(annotation.Reference!);
        return appearances;
    }

    private static void Expand(NotePage page, double left, double top, double right, double bottom)
    {
        page.Pdf!.DisplayWidth = page.Width;
        page.Pdf.DisplayHeight = page.Height;
        page.Pdf.OffsetX = left;
        page.Pdf.OffsetY = top;
        page.Width += left + right;
        page.Height += top + bottom;
    }

    private static Stroke Line(double x1, double y1, double x2, double y2) => new(
        new StylusPointCollection { new StylusPoint(x1, y1), new StylusPoint(x2, y2) },
        new DrawingAttributes { Color = Colors.Blue, Width = 6, Height = 6, IgnorePressure = true });
    private static byte[] Serialize(params Stroke[] strokes)
    { using var stream = new MemoryStream(); new StrokeCollection(strokes).Save(stream); return stream.ToArray(); }
    private static bool Green(byte r, byte g, byte b) => r < 70 && g > 190 && b < 70;
    private static bool Orange(byte r, byte g, byte b) => r > 190 && g is > 70 and < 150 && b < 60;
    private static void AssertShiftedBounds(Rect before, Rect after, double x, double y)
    {
        Assert.False(before.IsEmpty); Assert.False(after.IsEmpty);
        Assert.InRange(Math.Abs(after.Left - before.Left - x), 0, 2);
        Assert.InRange(Math.Abs(after.Top - before.Top - y), 0, 2);
        Assert.InRange(Math.Abs(after.Width - before.Width), 0, 2);
        Assert.InRange(Math.Abs(after.Height - before.Height), 0, 2);
    }
    private static Rect Bounds(BitmapSource bitmap, Func<byte, byte, byte, bool> matches)
    {
        var pixels = Pixels(bitmap);
        var bounds = Rect.Empty;
        for (var y = 0; y < bitmap.PixelHeight; y++)
            for (var x = 0; x < bitmap.PixelWidth; x++)
            {
                var offset = (y * bitmap.PixelWidth + x) * 4;
                if (matches(pixels[offset + 2], pixels[offset + 1], pixels[offset])) bounds.Union(new Rect(x, y, 1, 1));
            }
        return bounds;
    }
    private static int Count(BitmapSource bitmap, Func<byte, byte, byte, bool> matches)
    {
        var pixels = Pixels(bitmap);
        var count = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
            if (matches(pixels[offset + 2], pixels[offset + 1], pixels[offset])) count++;
        return count;
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var result = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        converted.CopyPixels(result, bitmap.PixelWidth * 4, 0); return result;
    }
    private static Task<T> Sta<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(action()); } catch (Exception ex) { completion.SetException(ex); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private sealed class MemoryRepository : INotebookRepository
    {
        private readonly Dictionary<string, AssetData> _assets = [];
        public Task<AssetData> PutAssetAsync(string fileName, string contentType, byte[] bytes)
        { var asset = new AssetData(Guid.NewGuid().ToString("N"), fileName, contentType, bytes); _assets.Add(asset.Id, asset); return Task.FromResult(asset); }
        public Task<AssetData> GetAssetAsync(string id) => Task.FromResult(_assets[id]);
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>([]);
        public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult<NotebookDocument?>(null);
        public Task SaveAsync(NotebookDocument document) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
        public void Dispose() { }
    }
}
