using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;

namespace Moye.Tests;

public sealed class EraserFootprintTests
{
    [Theory]
    [InlineData(InkTool.PointEraser, 12)]
    [InlineData(InkTool.PointEraser, 20)]
    [InlineData(InkTool.PointEraser, 48)]
    [InlineData(InkTool.PointEraser, 120)]
    [InlineData(InkTool.StrokeEraser, 12)]
    [InlineData(InkTool.StrokeEraser, 48)]
    [InlineData(InkTool.StrokeEraser, 120)]
    public void VisibleFootprintMatchesNativeShapeAndHitBoundary(InkTool tool, double size)
    {
        Sta(() =>
        {
            var editor = CreateEditor(tool, size);
            var canvas = editor.InkCanvas;
            var center = new Point(200, 200);
            PointAt(canvas, center);
            var footprint = Assert.IsType<EllipseGeometry>(canvas.EraserFootprint);
            Assert.Equal(center, footprint.Center);
            Assert.Equal(canvas.EraserShape.Width / 2, footprint.RadiusX);
            Assert.Equal(canvas.EraserShape.Height / 2, footprint.RadiusY);
            Assert.True(canvas.UseCustomCursor);
            Assert.Same(Cursors.None, canvas.Cursor);

            var inside = Dot(center + new Vector(size / 2 - 1, 0));
            var outside = Dot(center + new Vector(size / 2 + 1, 0));
            var corner = Dot(center + new Vector(size * .425, size * .425));
            Assert.True(footprint.FillContains((Point)inside.StylusPoints[0]));
            Assert.False(footprint.FillContains((Point)outside.StylusPoints[0]));
            Assert.False(footprint.FillContains((Point)corner.StylusPoints[0]));
            canvas.Strokes.Add(new StrokeCollection { inside, outside, corner });
            var hitTester = canvas.Strokes.GetIncrementalStrokeHitTester(canvas.EraserShape);
            hitTester.StrokeHit += (_, hit) =>
            {
                if (tool == InkTool.StrokeEraser) canvas.Strokes.Remove(hit.HitStroke);
                else
                {
                    var remaining = hit.GetPointEraseResults();
                    if (remaining.Count == 0) canvas.Strokes.Remove(hit.HitStroke);
                    else canvas.Strokes.Replace(hit.HitStroke, remaining);
                }
            };
            hitTester.AddPoint(center);
            hitTester.EndHitTesting();
            Assert.Equal(2, canvas.Strokes.Count);
            Assert.Contains(outside, canvas.Strokes);
            Assert.Contains(corner, canvas.Strokes);
        });
    }

    [Theory]
    [InlineData(.25)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void RenderedOutlineAndInkUseTheSamePageTransform(double zoom)
    {
        Sta(() =>
        {
            var editor = CreateEditor(InkTool.PointEraser, 120);
            editor.LayoutTransform = new ScaleTransform(zoom, zoom);
            editor.HorizontalAlignment = HorizontalAlignment.Left;
            editor.VerticalAlignment = VerticalAlignment.Top;
            var root = new Grid();
            root.Children.Add(editor);
            root.Measure(new Size(2000, 2000));
            root.Arrange(new Rect(0, 0, 2000, 2000));
            PointAt(editor.InkCanvas, new Point(200, 200));
            root.UpdateLayout();
            var preview = Preview(editor);
            var expected = editor.InkCanvas.TransformToAncestor(root).TransformBounds(editor.InkCanvas.EraserFootprint!.Bounds);
            var drawing = VisualTreeHelper.GetDrawing(preview);
            Assert.NotNull(drawing);
            var outlines = GeometryDrawings(drawing).ToArray();
            Assert.Equal(2, outlines.Length);
            foreach (var outline in outlines)
            {
                Assert.Null(outline.Brush);
                var actual = preview.TransformToAncestor(root).TransformBounds(outline.Geometry.Bounds);
                Assert.Equal(expected, actual);
                Assert.Equal(120 * zoom, actual.Width, 6);
                Assert.Equal(120 * zoom, actual.Height, 6);
            }
            Assert.False(preview.IsHitTestVisible);
            Assert.False(preview.Focusable);
            Assert.True(preview.ClipToBounds);
        });
    }

    [Fact]
    public void ShapeAndNativeActiveModeRefreshWithoutChangingTheDocument()
    {
        Sta(() =>
        {
            var editor = CreateEditor(InkTool.Pen, 20);
            var canvas = editor.InkCanvas;
            var changes = 0;
            editor.ContentChanged += (_, _) => changes++;
            PointAt(canvas, new Point(150, 160));
            Assert.Null(canvas.EraserFootprint);
            // Change native active mode independently of the requested toolbar
            // tool, as the inverted pen path does. This does not fake a device.
            canvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
            Assert.NotNull(canvas.EraserFootprint);
            canvas.SetEraserShape(new EllipseStylusShape(48, 48));
            Assert.Equal(new Rect(126, 136, 48, 48), canvas.EraserFootprint!.Bounds);
            canvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
            Assert.Equal(new Rect(126, 136, 48, 48), canvas.EraserFootprint!.Bounds);
            canvas.EditingMode = InkCanvasEditingMode.Ink;
            AssertHidden(canvas);
            Assert.Empty(canvas.Strokes);
            Assert.Empty(editor.Page.InkData);
            Assert.Equal(0, changes);
        });
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("outside")]
    [InlineData("finish")]
    [InlineData("unload")]
    [InlineData("disabled")]
    [InlineData("hit-test-disabled")]
    [InlineData("tool")]
    public void InterruptedFeedbackRestoresTheNativeCursor(string interruption)
    {
        Sta(() =>
        {
            var editor = CreateEditor(InkTool.PointEraser, 48);
            var canvas = editor.InkCanvas;
            PointAt(canvas, new Point(200, 200));
            Assert.NotNull(canvas.EraserFootprint);
            switch (interruption)
            {
                case "leave": Invoke(canvas, "ClearEraserPointer"); break;
                case "outside": PointAt(canvas, new Point(-1, 200)); break;
                case "finish": canvas.FinishInput(); break;
                case "unload": canvas.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); break;
                case "disabled": canvas.IsEnabled = false; break;
                case "hit-test-disabled": canvas.IsHitTestVisible = false; break;
                case "tool": canvas.SetRequestedMode(InkCanvasEditingMode.Ink); break;
            }
            AssertHidden(canvas);
        });
    }

    [Fact]
    public void TouchSuppressionDoesNotOverrideAnActivePen()
    {
        Sta(() =>
        {
            var canvas = CreateEditor(InkTool.PointEraser, 48).InkCanvas;
            PointAt(canvas, new Point(200, 200));
            var touches = (HashSet<int>)Field("_touchDevices").GetValue(canvas)!;
            touches.Add(1);
            Invoke(canvas, "UpdateEraserFootprint");
            AssertHidden(canvas);
            Invoke(canvas, "SetPenContact", true);
            Invoke(canvas, "UpdateEraserFootprint");
            Assert.NotNull(canvas.EraserFootprint);
            canvas.FinishInput();
            AssertHidden(canvas);
        });
    }

    [Fact]
    public void StaleCaptureCleanupCannotClearNewerFeedback()
    {
        Sta(() =>
        {
            var canvas = CreateEditor(InkTool.PointEraser, 48).InkCanvas;
            PointAt(canvas, new Point(200, 200));
            Invoke(canvas, "CompleteLostCapture");
            Field("_inputRevision").SetValue(canvas, 1);
            PointAt(canvas, new Point(220, 200));
            PumpDispatcher();
            Assert.Equal(new Rect(196, 176, 48, 48), canvas.EraserFootprint!.Bounds);
        });
    }

    [Fact]
    public void ActiveInputThumbnailExcludesFeedbackAndRestoresTheLiveOutline()
    {
        Sta(() =>
        {
            var editor = CreateEditor(InkTool.PointEraser, 120);
            var canvas = editor.InkCanvas;
            var render = typeof(PageEditor).GetMethod("RenderThumbnail", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var expected = Assert.IsType<byte[]>(render.Invoke(editor, [400]));
            PointAt(canvas, new Point(200, 200));
            var footprint = canvas.EraserFootprint;
            editor.UpdateLayout();
            // Verify there really is an outline to exclude, not just empty state.
            Assert.NotEmpty(GeometryDrawings(VisualTreeHelper.GetDrawing(Preview(editor))!));
            Invoke(canvas, "SetPenContact", true);
            var changes = 0;
            editor.ContentChanged += (_, _) => changes++;
            var thumbnail = editor.CreateThumbnail(400);
            Assert.Equal(expected, thumbnail);
            Assert.Same(footprint, canvas.EraserFootprint);
            Assert.Equal(Visibility.Visible, Preview(editor).Visibility);
            Assert.True(canvas.IsPenDown);
            Assert.Equal(InkCanvasEditingMode.EraseByPoint, canvas.EditingMode);
            Assert.Equal(0, changes);
            canvas.FinishInput();
        });
    }

    private static PageEditor CreateEditor(InkTool tool, double size)
    {
        var editor = new PageEditor(new NotePage { Width = 400, Height = 400, Template = PaperTemplate.Plain },
            _ => throw new InvalidOperationException("No assets in fixture."));
        editor.ConfigureWriting(new WritingPreset(), size, false);
        editor.SetTool(tool, Colors.Black, 2);
        editor.Measure(new Size(400, 400));
        editor.Arrange(new Rect(0, 0, 400, 400));
        editor.UpdateLayout();
        return editor;
    }

    private static Stroke Dot(Point point) => new(new StylusPointCollection { new StylusPoint(point.X, point.Y) },
        new DrawingAttributes { Width = .2, Height = .2, IgnorePressure = true, FitToCurve = false });
    private static FrameworkElement Preview(PageEditor editor) => (FrameworkElement)editor.Children[editor.Children.Count - 1];
    private static void PointAt(PenInkCanvas canvas, Point point) => Invoke(canvas, "SetEraserPointer", point, null);
    private static FieldInfo Field(string name) => typeof(PenInkCanvas).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void Invoke(PenInkCanvas canvas, string method, params object?[] args) =>
        typeof(PenInkCanvas).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, args);
    private static void AssertHidden(PenInkCanvas canvas)
    {
        Assert.Null(canvas.EraserFootprint);
        Assert.False(canvas.UseCustomCursor);
        Assert.NotSame(Cursors.None, canvas.Cursor);
    }
    private static IEnumerable<GeometryDrawing> GeometryDrawings(Drawing drawing)
    {
        if (drawing is GeometryDrawing geometry) yield return geometry;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var item in GeometryDrawings(child)) yield return item;
    }
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Sta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Eraser feedback test exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
