using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;
using Moye.ViewModels;

namespace Moye.Tests;

/// <summary>Selection geometry/history and clipboard serialization; not a physical lasso/clipboard test.</summary>
public sealed class MixedSelectionTests
{
    [Fact]
    public void MixedCopyRoundTripsAssetsTypographyPressureAndRelativePlacementInOneEdit()
    {
        Sta(() =>
        {
            var asset = ImageAsset();
            var sourcePage = SamplePage(asset.Id);
            var source = new PageEditor(sourcePage, _ => Task.FromResult(asset));
            source.SelectAllContent();
            var encoded = source.ExportSelectionAsync().GetAwaiter().GetResult()!.Encode();
            var payload = NoteSelection.Decode(encoded);
            var sourceBefore = sourcePage.Snapshot();
            var targetPage = new NotePage();
            var target = new PageEditor(targetPage, _ => Task.FromResult(asset));
            var document = new NotebookDocument { Pages = [targetPage] };
            var history = new NotebookHistory(); history.Reset(document);
            var changes = 0;
            target.ContentChanged += (_, _) => { changes++; history.Record(document); };

            target.ImportSelection(payload, new Point(45, 130), new Dictionary<string, string> { [asset.Id] = "stored-asset" });

            Assert.Equal(1, changes);
            var text = Assert.Single(targetPage.Texts);
            var image = Assert.Single(targetPage.Images);
            Assert.Equal("stored-asset", image.AssetId);
            Assert.Equal(sourcePage.Texts[0].Text, text.Text);
            Assert.True(text.Bold); Assert.True(text.Italic);
            Assert.Equal(NoteTextAlignment.Center, text.Alignment);
            Assert.Equal(sourcePage.Images[0].X - sourcePage.Texts[0].X, image.X - text.X, 6);
            Assert.Equal(sourcePage.Images[0].Y - sourcePage.Texts[0].Y, image.Y - text.Y, 6);
            Assert.Equal(asset.Bytes, Assert.Single(payload.Assets).Bytes);
            Assert.NotEqual(sourcePage.Texts[0].Id, text.Id);
            Assert.NotEqual(sourcePage.Images[0].Id, image.Id);
            var stroke = Assert.Single(new StrokeCollection(new MemoryStream(targetPage.InkData)));
            Assert.InRange(stroke.StylusPoints[0].PressureFactor, .19f, .21f);
            Assert.InRange(stroke.StylusPoints[1].PressureFactor, .79f, .81f);
            Assert.Equal(Colors.DarkBlue, stroke.DrawingAttributes.Color);
            Assert.Equal(sourceBefore.InkData, sourcePage.InkData);
            Assert.Equal(sourceBefore.Texts[0], sourcePage.Texts[0]);
            var undo = history.Undo()!;
            Assert.Empty(undo.Pages[0].Texts); Assert.Empty(undo.Pages[0].Images); Assert.Empty(undo.Pages[0].InkData);
            Assert.Equal(text, Assert.Single(history.Redo()!.Pages[0].Texts));
            Assert.Equal(2, target.InkCanvas.GetSelectedElements().Count);
            Assert.Single(target.InkCanvas.GetSelectedStrokes());
        });
    }

    [Fact]
    public void MixedNudgeAndDuplicateKeepTheGroupTogetherAndDeleteIsOneUndoableEdit()
    {
        Sta(() =>
        {
            var asset = ImageAsset(); var page = SamplePage(asset.Id);
            var editor = new PageEditor(page, _ => Task.FromResult(asset));
            editor.SelectAllContent();
            var x = page.Texts[0].X; var imageX = page.Images[0].X;
            var changes = 0; editor.ContentChanged += (_, _) => changes++;
            Assert.True(editor.NudgeSelection(10, 5));
            Assert.Equal(x + 10, page.Texts[0].X); Assert.Equal(imageX + 10, page.Images[0].X);
            Assert.Equal(1, changes);
            editor.DuplicateSelection();
            Assert.Equal(2, changes);
            Assert.Equal(2, page.Texts.Count); Assert.Equal(2, page.Images.Count);
            Assert.Equal(2, editor.InkCanvas.Strokes.Count);
            Assert.Equal(20, page.Texts[1].X - page.Texts[0].X);
            Assert.Equal(20, page.Images[1].X - page.Images[0].X);
            var document = new NotebookDocument { Pages = [page] };
            var history = new NotebookHistory(); history.Reset(document);
            editor.ContentChanged += (_, _) => history.Record(document);
            editor.DeleteSelection();
            Assert.Equal(3, changes);
            Assert.Single(page.Texts); Assert.Single(page.Images); Assert.Single(editor.InkCanvas.Strokes);
            var restored = history.Undo()!.Pages[0];
            Assert.Equal(2, restored.Texts.Count); Assert.Equal(2, restored.Images.Count);
        });
    }

    [Fact]
    public void NativeElementResizeCommitUpdatesModelAndThumbnailKeepsMixedSelection()
    {
        Sta(() =>
        {
            var asset = ImageAsset(); var page = SamplePage(asset.Id);
            var editor = new PageEditor(page, _ => Task.FromResult(asset));
            editor.SelectAllContent(); Layout(editor);
            var elements = editor.InkCanvas.GetSelectedElements().Cast<FrameworkElement>().ToArray();
            foreach (var element in elements)
            {
                InkCanvas.SetLeft(element, InkCanvas.GetLeft(element) + 14);
                InkCanvas.SetTop(element, InkCanvas.GetTop(element) + 9);
                element.Width *= 1.2; element.Height *= 1.1;
            }
            var changes = 0; editor.ContentChanged += (_, _) => changes++;
            typeof(InkCanvas).GetMethod("OnSelectionResized", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor.InkCanvas, [EventArgs.Empty]);
            Assert.Equal(1, changes);
            Assert.Equal(94, page.Texts[0].X); Assert.Equal(109, page.Texts[0].Y);
            Assert.Equal(240, page.Texts[0].Width, 6);
            Assert.NotEmpty(editor.CreateThumbnail());
            Assert.Equal(2, editor.InkCanvas.GetSelectedElements().Count);
            Assert.Single(editor.InkCanvas.GetSelectedStrokes());
            Assert.Equal(1, changes);
            Assert.True(editor.NudgeSelection(1, 0));
            Assert.Equal(95, page.Texts[0].X);
        });
    }

    [Fact]
    public void SingleTextCopyAndNudgeDoNotRequireLassoAndMalformedPasteIsAtomic()
    {
        Sta(() =>
        {
            var editor = new PageEditor(new NotePage(), _ => throw new InvalidOperationException());
            var text = editor.AddTextAt(new Point(0, 0), "text");
            Assert.True(editor.NudgeSelection(-10, -10));
            Assert.Equal(0, text.X); Assert.Equal(0, text.Y);
            var payload = editor.ExportSelectionAsync().GetAwaiter().GetResult()!;
            Assert.Single(payload.Texts); Assert.Empty(payload.InkData);
            payload.Texts[0].Width = double.NaN;
            var changes = 0; editor.ContentChanged += (_, _) => changes++;
            Assert.Throws<InvalidDataException>(() => editor.ImportSelection(payload, new Point(20, 30)));
            Assert.Single(editor.Page.Texts); Assert.Equal(0, changes);
        });
    }

    private static NotePage SamplePage(string assetId)
    {
        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(40, 50, .2f), new StylusPoint(65, 85, .8f) }, new DrawingAttributes { Color = Colors.DarkBlue, Width = 3, Height = 3, IgnorePressure = false });
        using var stream = new MemoryStream(); new StrokeCollection { stroke }.Save(stream);
        return new NotePage
        {
            InkData = stream.ToArray(),
            Texts = [new NoteText { X = 80, Y = 100, Width = 200, Height = 100, Text = "1. 課堂筆記", FontFamily = "Arial", FontSize = 24, Bold = true, Italic = true, Alignment = NoteTextAlignment.Center }],
            Images = [new NoteImage { AssetId = assetId, X = 150, Y = 240, Width = 120, Height = 80 }]
        };
    }
    private static AssetData ImageAsset()
    {
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream);
        return new("source-asset", "sample.png", "image/png", stream.ToArray());
    }
    private static void Layout(PageEditor editor)
    {
        editor.Measure(new Size(editor.Width, editor.Height)); editor.Arrange(new Rect(0, 0, editor.Width, editor.Height)); editor.UpdateLayout();
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Selection test exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
