using System.Collections;
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

/// <summary>Actual WPF selection geometry and edit transactions; not a native pointer or pen test.</summary>
public sealed class LiveSelectionDragTests
{
    [Fact]
    public void DragPreviewMovesAllVisibleContentWithoutChangingDocumentOrFlushingHistory()
    {
        Sta(() =>
        {
            var editor = CreateMixedEditor();
            var before = editor.Page.Snapshot();
            var point = Center(editor.InkCanvas.GetSelectionBounds());
            var changes = 0;
            editor.ContentChanged += (_, _) => changes++;

            Invoke(editor, "BeginSelectionDrag", point);
            Invoke(editor, "UpdateSelectionDrag", point + new Vector(32, 19));
            Layout(editor);

            Assert.True(editor.IsInputActive);
            AssertPreviewGeometry(editor, before, 32, 19);
            Assert.Equal(before.Texts[0], editor.Page.Texts[0]);
            Assert.Equal(before.Images[0], editor.Page.Images[0]);
            Assert.Same(before.InkData, editor.Page.InkData);
            Invoke(editor, "FlushChanges");
            Assert.NotEmpty(editor.CreateThumbnail());
            Assert.Equal(0, changes);
            Assert.Equal(2, editor.InkCanvas.GetSelectedElements().Count);
            Assert.Single(editor.InkCanvas.GetSelectedStrokes());
            Assert.True(editor.CancelSelectionDrag());
        });
    }

    [Fact]
    public void ReleaseCommitsTheWholeGroupAsOneUndoableEdit()
    {
        Sta(() =>
        {
            var editor = CreateMixedEditor();
            var before = editor.Page.Snapshot();
            var document = new NotebookDocument { Pages = [editor.Page] };
            var history = new NotebookHistory();
            history.Reset(document);
            var changes = 0;
            editor.ContentChanged += (_, _) => { changes++; history.Record(document); };
            var point = Center(editor.InkCanvas.GetSelectionBounds());

            Invoke(editor, "BeginSelectionDrag", point);
            Invoke(editor, "UpdateSelectionDrag", point + new Vector(35, 27));
            Invoke(editor, "FinishSelectionDrag", true);
            Invoke(editor, "FinishSelectionDrag", true);

            Assert.Equal(1, changes);
            Assert.False(editor.IsInputActive);
            AssertDocumentGeometry(editor.Page, before, 35, 27);
            var undo = history.Undo()!.Pages[0];
            Assert.Equal(before.Texts[0], undo.Texts[0]);
            Assert.Equal(before.Images[0], undo.Images[0]);
            Assert.Equal(before.InkData, undo.InkData);
            Assert.False(history.CanUndo);
            AssertDocumentGeometry(history.Redo()!.Pages[0], before, 35, 27);
        });
    }

    [Fact]
    public void CancelRestoresVisibleFramesProxiesAndPressureSamplesWithoutAnEdit()
    {
        Sta(() =>
        {
            var editor = CreateMixedEditor();
            var before = editor.Page.Snapshot();
            var point = Center(editor.InkCanvas.GetSelectionBounds());
            var changes = 0;
            editor.ContentChanged += (_, _) => changes++;

            Invoke(editor, "BeginSelectionDrag", point);
            Invoke(editor, "UpdateSelectionDrag", point + new Vector(63, 41));
            Assert.True(editor.CancelSelectionDrag());
            Assert.False(editor.CancelSelectionDrag());
            Layout(editor);
            editor.CommitPendingEdits();

            AssertPreviewGeometry(editor, before, 0, 0);
            Assert.Equal(before.Texts[0], editor.Page.Texts[0]);
            Assert.Equal(before.Images[0], editor.Page.Images[0]);
            Assert.Same(before.InkData, editor.Page.InkData);
            Assert.False(editor.IsInputActive);
            Assert.Equal(0, changes);
        });
    }

    [Fact]
    public void SuccessivePacketsUseTheOriginalAnchorAndKeepTheGroupInsideThePage()
    {
        Sta(() =>
        {
            var editor = CreateMixedEditor();
            var before = editor.Page.Snapshot();
            var bounds = editor.InkCanvas.GetSelectionBounds();
            var point = Center(bounds);
            Invoke(editor, "BeginSelectionDrag", point);
            Invoke(editor, "UpdateSelectionDrag", point + new Vector(20, 15));
            Invoke(editor, "UpdateSelectionDrag", point + new Vector(70, 35));
            Layout(editor);
            AssertPreviewGeometry(editor, before, 70, 35);

            Invoke(editor, "UpdateSelectionDrag", point + new Vector(10000, 10000));
            Layout(editor);
            AssertPreviewGeometry(editor, before, editor.Page.Width - bounds.Right, editor.Page.Height - bounds.Bottom);

            Invoke(editor, "UpdateSelectionDrag", point + new Vector(-10000, -10000));
            Layout(editor);
            AssertPreviewGeometry(editor, before, -bounds.Left, -bounds.Top);
            Invoke(editor, "FinishSelectionDrag", true);
            AssertDocumentGeometry(editor.Page, before, -bounds.Left, -bounds.Top);
        });
    }

    [Fact]
    public void ClickAndReturnToOriginDoNotProduceHistoryEntries()
    {
        Sta(() =>
        {
            foreach (var moveAway in new[] { false, true })
            {
                var editor = CreateMixedEditor();
                var before = editor.Page.Snapshot();
                var point = Center(editor.InkCanvas.GetSelectionBounds());
                var changes = 0;
                editor.ContentChanged += (_, _) => changes++;
                Invoke(editor, "BeginSelectionDrag", point);
                if (moveAway)
                {
                    Invoke(editor, "UpdateSelectionDrag", point + new Vector(17.25, 29.5));
                    Invoke(editor, "UpdateSelectionDrag", point);
                }
                Invoke(editor, "FinishSelectionDrag", true);
                editor.CommitPendingEdits();
                Assert.Equal(0, changes);
                Assert.Equal(before.Texts[0], editor.Page.Texts[0]);
                Assert.Equal(before.Images[0], editor.Page.Images[0]);
                Assert.Same(before.InkData, editor.Page.InkData);
                AssertPreviewGeometry(editor, before, 0, 0);
            }
        });
    }

    [Fact]
    public void CommitToolChangeAndReloadCompleteThePendingMoveExactlyOnce()
    {
        Sta(() =>
        {
            foreach (var finish in new Action<PageEditor>[]
            {
                editor => editor.CommitPendingEdits(),
                editor => editor.SetTool(InkTool.Pen, Colors.DarkBlue, 3),
                editor => editor.Reload(new NotePage())
            })
            {
                var editor = CreateMixedEditor();
                var editedPage = editor.Page;
                var before = editedPage.Snapshot();
                var point = Center(editor.InkCanvas.GetSelectionBounds());
                var changes = 0;
                editor.ContentChanged += (_, _) => changes++;
                Invoke(editor, "BeginSelectionDrag", point);
                Invoke(editor, "UpdateSelectionDrag", point + new Vector(28, 16));
                finish(editor);
                editor.CommitPendingEdits();
                AssertDocumentGeometry(editedPage, before, 28, 16);
                Assert.Equal(1, changes);
                Assert.False(editor.IsInputActive);
                Assert.False(editor.CancelSelectionDrag());
            }
        });
    }

    [Fact]
    public void ASelectedTextBodyCanStartDraggingWhileNativeResizeHandlesArePreserved()
    {
        Sta(() =>
        {
            var page = new NotePage
            {
                Texts = [new NoteText { X = 80, Y = 100, Width = 160, Height = 90, Text = "A selected note" }]
            };
            var editor = new PageEditor(page, _ => throw new InvalidOperationException());
            editor.SelectAllContent();
            Layout(editor);
            var bounds = editor.InkCanvas.GetSelectionBounds();
            var point = Center(bounds);

            // WPF reserves a sole element's interior for the element, so the
            // proxy layer needs an explicit selected-content body fallback.
            Assert.Equal(InkCanvasSelectionHitResult.None, editor.InkCanvas.HitTestSelection(point));
            Assert.True((bool)Invoke(editor, "CanStartSelectionDrag", point)!);
            Assert.False((bool)Invoke(editor, "CanStartSelectionDrag", new Point(500, 500))!);
            var handle = FindNativeResizeHandle(editor.InkCanvas, bounds);
            Assert.False((bool)Invoke(editor, "CanStartSelectionDrag", handle)!);
            editor.SetTool(InkTool.Pen, Colors.Black, 3);
            Assert.False((bool)Invoke(editor, "CanStartSelectionDrag", point)!);
        });
    }

    private static PageEditor CreateMixedEditor()
    {
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var imageStream = new MemoryStream();
        encoder.Save(imageStream);
        var asset = new AssetData("drag-image", "sample.png", "image/png", imageStream.ToArray());
        var stroke = new Stroke(new StylusPointCollection
        {
            new StylusPoint(40, 50, .2f), new StylusPoint(65, 85, .8f)
        }, new DrawingAttributes { Color = Colors.DarkBlue, Width = 3, Height = 3, IgnorePressure = false });
        using var inkStream = new MemoryStream();
        new StrokeCollection { stroke }.Save(inkStream);
        var page = new NotePage
        {
            Width = 400, Height = 500, InkData = inkStream.ToArray(),
            Texts = [new NoteText { X = 80, Y = 100, Width = 120, Height = 80, Text = "Live note", FontSize = 22 }],
            Images = [new NoteImage { AssetId = asset.Id, X = 200, Y = 260, Width = 100, Height = 60 }]
        };
        var editor = new PageEditor(page, _ => Task.FromResult(asset));
        editor.SelectAllContent();
        Layout(editor);
        return editor;
    }

    private static void AssertPreviewGeometry(PageEditor editor, NotePage before, double dx, double dy)
    {
        var frames = ((IEnumerable)typeof(PageEditor).GetField("_frames", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(editor)!).Cast<FrameworkElement>().ToArray();
        var textFrame = Assert.Single(frames, frame => frame.GetType().GetProperty("Item")!.GetValue(frame) is NoteText);
        var imageFrame = Assert.Single(frames, frame => frame.GetType().GetProperty("Item")!.GetValue(frame) is NoteImage);
        Assert.Equal(before.Texts[0].X + dx, Canvas.GetLeft(textFrame), 6);
        Assert.Equal(before.Texts[0].Y + dy, Canvas.GetTop(textFrame), 6);
        Assert.Equal(before.Images[0].X + dx, Canvas.GetLeft(imageFrame), 6);
        Assert.Equal(before.Images[0].Y + dy, Canvas.GetTop(imageFrame), 6);
        var proxies = editor.InkCanvas.GetSelectedElements().OrderBy(InkCanvas.GetLeft).ToArray();
        Assert.Equal(before.Texts[0].X + dx, InkCanvas.GetLeft(proxies[0]), 6);
        Assert.Equal(before.Texts[0].Y + dy, InkCanvas.GetTop(proxies[0]), 6);
        Assert.Equal(before.Images[0].X + dx, InkCanvas.GetLeft(proxies[1]), 6);
        Assert.Equal(before.Images[0].Y + dy, InkCanvas.GetTop(proxies[1]), 6);
        AssertStrokeOffset(Assert.Single(editor.InkCanvas.Strokes), before.InkData, dx, dy);
    }

    private static void AssertDocumentGeometry(NotePage page, NotePage before, double dx, double dy)
    {
        Assert.Equal(before.Texts[0] with { X = before.Texts[0].X + dx, Y = before.Texts[0].Y + dy }, page.Texts[0]);
        Assert.Equal(before.Images[0] with { X = before.Images[0].X + dx, Y = before.Images[0].Y + dy }, page.Images[0]);
        AssertStrokeOffset(Assert.Single(new StrokeCollection(new MemoryStream(page.InkData))), before.InkData, dx, dy, .06);
    }

    private static void AssertStrokeOffset(Stroke stroke, byte[] originalInk, double dx, double dy, double tolerance = .000001)
    {
        var original = Assert.Single(new StrokeCollection(new MemoryStream(originalInk)));
        Assert.Equal(original.StylusPoints.Count, stroke.StylusPoints.Count);
        for (var index = 0; index < original.StylusPoints.Count; index++)
        {
            Assert.InRange(stroke.StylusPoints[index].X, original.StylusPoints[index].X + dx - tolerance, original.StylusPoints[index].X + dx + tolerance);
            Assert.InRange(stroke.StylusPoints[index].Y, original.StylusPoints[index].Y + dy - tolerance, original.StylusPoints[index].Y + dy + tolerance);
            Assert.Equal(original.StylusPoints[index].PressureFactor, stroke.StylusPoints[index].PressureFactor, 5);
        }
        Assert.Equal(original.DrawingAttributes.Color, stroke.DrawingAttributes.Color);
        Assert.Equal(original.DrawingAttributes.Width, stroke.DrawingAttributes.Width);
        Assert.Equal(original.DrawingAttributes.Height, stroke.DrawingAttributes.Height);
    }

    private static Point FindNativeResizeHandle(InkCanvas canvas, Rect bounds)
    {
        for (var y = bounds.Top - 20; y <= bounds.Top + 20; y++)
        for (var x = bounds.Left - 20; x <= bounds.Left + 20; x++)
        {
            var point = new Point(x, y);
            var hit = canvas.HitTestSelection(point);
            if (hit is not (InkCanvasSelectionHitResult.None or InkCanvasSelectionHitResult.Selection)) return point;
        }
        throw new InvalidOperationException("No native resize handle was found around the selection's top-left corner.");
    }

    private static object? Invoke(PageEditor editor, string method, params object[] arguments) =>
        typeof(PageEditor).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, arguments);

    private static Point Center(Rect rectangle) => new(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);

    private static void Layout(PageEditor editor)
    {
        editor.Measure(new Size(editor.Width, editor.Height));
        editor.Arrange(new Rect(0, 0, editor.Width, editor.Height));
        editor.UpdateLayout();
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Selection drag test exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
