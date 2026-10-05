using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;
using Moye.Services;
using Moye.ViewModels;

namespace Moye.Tests;

public sealed class PageExtensionTests
{
    [Fact]
    public void ExtensionPreservesOriginalPdfAndMovesEveryOverlayWithoutChangingOriginal()
    {
        Sta(() =>
        {
            var original = MixedPage();
            var before = JsonSerializer.Serialize(original, DocumentJson.Options);
            var originalStroke = ReadInk(original)[0];
            var extended = PageExtensionService.Extend(original, new(50, 70, 90, 110));
            Assert.Equal(740, extended.Width); Assert.Equal(980, extended.Height);
            Assert.Equal(new Rect(50, 70, 600, 800), PageExtensionService.GetPdfBounds(extended));
            Assert.Equal(original.Pdf!.AssetId, extended.Pdf!.AssetId);
            Assert.Equal(original.Pdf.CropX, extended.Pdf.CropX);
            Assert.Equal(original.Pdf.Rotation, extended.Pdf.Rotation);
            Assert.Equal(original.Texts[0] with { X = 62, Y = 93 }, extended.Texts[0]);
            Assert.Equal(original.Images[0] with { X = 81, Y = 112 }, extended.Images[0]);
            var moved = ReadInk(extended)[0];
            Assert.Equal(originalStroke.DrawingAttributes, moved.DrawingAttributes);
            for (var index = 0; index < moved.StylusPoints.Count; index++)
            {
                Assert.Equal(originalStroke.StylusPoints[index].X + 50, moved.StylusPoints[index].X, .1);
                Assert.Equal(originalStroke.StylusPoints[index].Y + 70, moved.StylusPoints[index].Y, .1);
                Assert.Equal(originalStroke.StylusPoints[index].PressureFactor, moved.StylusPoints[index].PressureFactor, .002);
            }
            Assert.Equal(before, JsonSerializer.Serialize(original, DocumentJson.Options));
            Assert.NotSame(original.InkData, extended.InkData);
        });
    }

    [Fact]
    public void RepeatedExtensionsKeepPdfSizeAndAccumulateMargins()
    {
        var original = new NotePage { Width = 600, Height = 800, Pdf = new() };
        var once = PageExtensionService.Extend(original, new(0, 0, 200, 100));
        var twice = PageExtensionService.Extend(once, new(30, 40, 50, 60));
        Assert.Equal(880, twice.Width); Assert.Equal(1000, twice.Height);
        Assert.Equal(new Rect(30, 40, 600, 800), PageExtensionService.GetPdfBounds(twice));
        Assert.Same(original.InkData, once.InkData);
        Assert.Equal(new Rect(0, 0, 600, 800), PageExtensionService.GetPdfBounds(original));
    }

    [Fact]
    public void ExtensionIsOneUndoStepAndRetainsTargetPageAndSection()
    {
        Sta(() =>
        {
            using var model = new MainViewModel(new WorkflowRepository());
            var first = new NoteSection(); var second = new NoteSection { Title = "PDF" };
            var original = MixedPage(); original.SectionId = second.Id;
            var book = new NotebookDocument { Sections = [first, second], Pages = [new() { SectionId = first.Id }, original] };
            model.ReplaceDocument(book, true);
            Assert.True(model.ExtendPage(original.Id, new(20, 30, 40, 50)));
            var expected = JsonSerializer.Serialize(model.SelectedPage!.Page, DocumentJson.Options);
            Assert.Equal(second.Id, model.SelectedSection!.Id);
            Assert.Equal(original.Id, model.SelectedPage.Page.Id);
            Assert.True(model.CanUndo);
            model.Undo();
            Assert.False(model.CanUndo);
            Assert.Equal(JsonSerializer.Serialize(original, DocumentJson.Options), JsonSerializer.Serialize(model.SelectedPage!.Page, DocumentJson.Options));
            model.Redo();
            Assert.Equal(expected, JsonSerializer.Serialize(model.SelectedPage!.Page, DocumentJson.Options));
            model.DuplicatePage();
            Assert.Equal(new Rect(20, 30, 600, 800), PageExtensionService.GetPdfBounds(model.SelectedPage!.Page));
        });
    }

    [Fact]
    public void BlankPageExtensionRetainsTemplateAndCanBeWrittenInNewSpace()
    {
        Sta(() =>
        {
            var page = PageExtensionService.Extend(new NotePage { Width = 400, Height = 500, Template = PaperTemplate.Grid }, new(0, 0, 200, 200));
            Assert.Null(page.Pdf); Assert.Equal(PaperTemplate.Grid, page.Template);
            var editor = new PageEditor(page, _ => throw new InvalidOperationException());
            var text = editor.AddTextAt(new Point(480, 600), "Margin notes");
            editor.InkCanvas.Strokes.Add(new Stroke(new StylusPointCollection([new StylusPoint(530, 650), new StylusPoint(570, 675)])));
            editor.CommitPendingEdits();
            Assert.Equal(480, text.X); Assert.Equal(600, text.Y);
            Assert.Single(page.Texts); Assert.True(ReadInk(page)[0].GetBounds().X > 500);
        });
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(double.NaN, 1, 1, 1)]
    [InlineData(1, double.PositiveInfinity, 1, 1)]
    [InlineData(0, 0, 20000, 0)]
    public void InvalidOrEmptyExtensionDoesNotCreateHistory(double left, double top, double right, double bottom)
    {
        using var model = new MainViewModel(new WorkflowRepository());
        var page = new NotePage(); model.ReplaceDocument(new NotebookDocument { Pages = [page] }, true);
        Assert.False(model.ExtendPage(page.Id, new(left, top, right, bottom)));
        Assert.False(model.CanUndo); Assert.Same(page, model.SelectedPage!.Page);
    }

    [Fact]
    public void MalformedInkCannotPartiallyChangeDimensionsObjectsOrPdfPlacement()
    {
        Sta(() =>
        {
            using var model = new MainViewModel(new WorkflowRepository());
            var page = new NotePage { InkData = [1, 2, 3], Pdf = new(), Texts = [new() { X = 42 }] };
            model.ReplaceDocument(new NotebookDocument { Pages = [page] }, true);
            var before = JsonSerializer.Serialize(page, DocumentJson.Options);
            Assert.ThrowsAny<Exception>(() => model.ExtendPage(page.Id, new(20, 30, 40, 50)));
            Assert.False(model.CanUndo); Assert.Same(page, model.Document!.Pages[0]);
            Assert.Equal(before, JsonSerializer.Serialize(page, DocumentJson.Options));
        });
    }

    [Fact]
    public void EditorAndReferencePreviewDrawOriginalPdfAtFixedSizeInsideWhiteCanvas()
    {
        Sta(() =>
        {
            var page = PageExtensionService.Extend(new NotePage { Width = 200, Height = 240, Pdf = new() }, new(40, 50, 60, 80));
            var background = SolidBitmap();
            var editor = new PageEditor(page, _ => throw new InvalidOperationException());
            editor.SetPdfBackground(background);
            editor.Measure(new Size(page.Width, page.Height)); editor.Arrange(new Rect(0, 0, page.Width, page.Height)); editor.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)page.Width, (int)page.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(editor);
            var preview = new PagePreviewRenderer(new FixturePdf(background), _ => throw new InvalidOperationException())
                .RenderAsync(page, page.Width).GetAwaiter().GetResult();
            foreach (var rendered in new BitmapSource[] { bitmap, preview })
            {
                Assert.Equal(Colors.White, Pixel(rendered, 10, 100));
                Assert.Equal(Colors.White, Pixel(rendered, 100, 10));
                Assert.Equal(Colors.White, Pixel(rendered, 260, 100));
                Assert.Equal(Colors.White, Pixel(rendered, 100, 330));
                Assert.Equal(Colors.Magenta, Pixel(rendered, 50, 60));
                Assert.Equal(Colors.Magenta, Pixel(rendered, 230, 280));
            }
        });
    }

    private static NotePage MixedPage()
    {
        var stroke = new Stroke(new StylusPointCollection([new StylusPoint(100, 200, .25f), new StylusPoint(150, 250, .8f)]));
        stroke.DrawingAttributes.Color = Colors.Blue; stroke.DrawingAttributes.Width = 7;
        using var stream = new MemoryStream(); new StrokeCollection([stroke]).Save(stream);
        return new NotePage { Width = 600, Height = 800, Pdf = new() { AssetId = "original", Rotation = 90, CropX = 10, CropY = 20, CropWidth = 600, CropHeight = 450 },
            InkData = stream.ToArray(), Texts = [new() { X = 12, Y = 23, Text = "Existing notes", Bold = true }],
            Images = [new() { AssetId = "image", X = 31, Y = 42 }] };
    }
    private static StrokeCollection ReadInk(NotePage page) => new(new MemoryStream(page.InkData, false));
    private static BitmapSource SolidBitmap()
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 0, 255, 255 }, 4);
        bitmap.Freeze(); return bitmap;
    }
    private static Color Pixel(BitmapSource bitmap, int x, int y)
    {
        var bytes = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0); return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }
    private sealed class FixturePdf(BitmapSource image) : IPdfService
    {
        public Task<BitmapSource> RenderAsync(NotePage page, double scale, CancellationToken cancellationToken = default) => Task.FromResult(image);
        public Task<IReadOnlyList<NotePage>> ImportAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExportAsync(string path, NotebookDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
