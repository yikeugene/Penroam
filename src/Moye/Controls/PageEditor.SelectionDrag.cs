using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using Moye.Models;

namespace Moye.Controls;

public sealed partial class PageEditor
{
    private sealed record DragFrame(NoteItemFrame Frame, double X, double Y);
    private sealed record DragStroke(Stroke Stroke, StylusPointCollection Points);
    private sealed class SelectionDrag(Point origin, Rect bounds, DragFrame[] frames, DragStroke[] strokes)
    {
        public Point Origin { get; } = origin;
        public Rect Bounds { get; } = bounds;
        public DragFrame[] Frames { get; } = frames;
        public DragStroke[] Strokes { get; } = strokes;
        public Vector Offset { get; set; }
    }

    private SelectionDrag? _selectionDrag;
    private bool _updatingSelectionDrag;

    private void InitializeSelectionDragging()
    {
        // WPF's selection feedback only moves an outline. Handle its body drag
        // before the native mouse handler, including mouse events promoted from
        // a pen. Resize handles and drawing a new lasso keep their native route.
        _ink.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || PenInkCanvas.IsTouch(e.StylusDevice) ||
                _selectionDrag is not null || !CanStartSelectionDrag(e.GetPosition(_ink))) return;
            // Do not FinishInput here: the pen-down contact must remain active
            // for palm rejection throughout this new drag.
            FlushChanges();
            _ink.Focus();
            if (!_ink.CaptureMouse()) return;
            BeginSelectionDrag(e.GetPosition(_ink));
            e.Handled = true;
        };
        _ink.PreviewMouseMove += (_, e) =>
        {
            if (_selectionDrag is null || PenInkCanvas.IsTouch(e.StylusDevice)) return;
            if (e.LeftButton != MouseButtonState.Pressed) FinishSelectionDrag(true);
            else UpdateSelectionDrag(e.GetPosition(_ink));
            e.Handled = true;
        };
        _ink.PreviewMouseUp += (_, e) =>
        {
            if (_selectionDrag is null || e.ChangedButton != MouseButton.Left || PenInkCanvas.IsTouch(e.StylusDevice)) return;
            UpdateSelectionDrag(e.GetPosition(_ink));
            FinishSelectionDrag(true);
            e.Handled = true;
        };
        _ink.LostMouseCapture += (_, _) =>
        {
            if (!_ink.IsMouseCaptured) FinishSelectionDrag(true);
        };
    }

    private bool CanStartSelectionDrag(Point point)
    {
        if (_tool != InkTool.Lasso || _ink.ActiveEditingMode != InkCanvasEditingMode.Select ||
            !_ink.MoveEnabled || !HasSelection || !IsFinite(point)) return false;
        var hit = _ink.HitTestSelection(point);
        if (hit == InkCanvasSelectionHitResult.Selection) return true;
        // InkCanvas intentionally returns None inside a single selected child.
        // Our child is an invisible proxy; dragging its visible text/image must
        // still move it. Never take over one of the native resize handles.
        return hit == InkCanvasSelectionHitResult.None && _selectedFrames.Count == 1 &&
            _ink.GetSelectedStrokes().Count == 0 && _ink.GetSelectionBounds().Contains(point);
    }

    private static bool IsFinite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private void BeginSelectionDrag(Point point)
    {
        _selectionDrag = new(point, _ink.GetSelectionBounds(),
            _selectedFrames.Select(frame => new DragFrame(frame, Canvas.GetLeft(frame), Canvas.GetTop(frame))).ToArray(),
            _ink.GetSelectedStrokes().Select(stroke => new DragStroke(stroke, stroke.StylusPoints.Clone())).ToArray());
    }

    private void UpdateSelectionDrag(Point point)
    {
        if (_selectionDrag is not { } drag || !IsFinite(point)) return;
        var offset = point - drag.Origin;
        offset.X = Math.Clamp(offset.X, -drag.Bounds.Left, Math.Max(-drag.Bounds.Left, Page.Width - drag.Bounds.Right));
        offset.Y = Math.Clamp(offset.Y, -drag.Bounds.Top, Math.Max(-drag.Bounds.Top, Page.Height - drag.Bounds.Bottom));
        if (offset == drag.Offset) return;
        drag.Offset = offset;
        ApplySelectionDragPreview(drag, offset);
    }

    private void ApplySelectionDragPreview(SelectionDrag drag, Vector offset)
    {
        _updatingSelectionDrag = true;
        try
        {
            foreach (var item in drag.Frames)
            {
                Canvas.SetLeft(item.Frame, item.X + offset.X);
                Canvas.SetTop(item.Frame, item.Y + offset.Y);
                UpdateSelectionProxy(item.Frame);
            }
            foreach (var item in drag.Strokes)
            {
                // Derive every packet from the original points: no accumulated
                // rounding, no pressure/format changes, and exact cancellation.
                var points = item.Points.Clone();
                for (var index = 0; index < points.Count; index++)
                {
                    var point = points[index]; point.X += offset.X; point.Y += offset.Y;
                    points[index] = point;
                }
                item.Stroke.StylusPoints = points;
            }
        }
        finally { _updatingSelectionDrag = false; }
    }

    private void FinishSelectionDrag(bool commit)
    {
        if (_selectionDrag is not { } drag) return;
        // Clear first: releasing capture, changing tools or saving can re-enter.
        _selectionDrag = null;
        if (!commit) ApplySelectionDragPreview(drag, new Vector());
        var changed = commit && drag.Offset != new Vector();
        if (changed)
        {
            foreach (var item in drag.Frames)
                SetFrameGeometry(item.Frame, item.X + drag.Offset.X, item.Y + drag.Offset.Y, item.Frame.Width, item.Frame.Height);
            if (drag.Frames.Length > 0) MarkContentDirty();
            if (drag.Strokes.Length > 0) MarkInkDirty();
        }
        if (_ink.IsMouseCaptured) _ink.ReleaseMouseCapture();
        if (changed) FlushChanges();
    }

    public bool CancelSelectionDrag()
    {
        if (_selectionDrag is null) return false;
        FinishSelectionDrag(false);
        return true;
    }
}
