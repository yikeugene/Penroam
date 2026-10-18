using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace Moye.Controls;

public sealed partial class PenInkCanvas
{
    private Point? _eraserPointer, _footprintCenter;
    private StylusDevice? _eraserPointerDevice;
    private StylusShape? _footprintShape;
    public Geometry? EraserFootprint { get; private set; }
    public event EventHandler? EraserFootprintChanged;

    private void InitializeEraserFeedback()
    {
        AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(TrackEraserMouse), true);
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(TrackEraserMouse), true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(TrackEraserMouse), true);
        MouseEnter += TrackEraserMouse;
        MouseLeave += (_, e) => { if (e.StylusDevice is null && _eraserPointerDevice is null) ClearEraserPointer(); };
        AddHandler(Stylus.PreviewStylusMoveEvent, new StylusEventHandler(TrackEraserStylus), true);
        AddHandler(Stylus.PreviewStylusInAirMoveEvent, new StylusEventHandler(TrackEraserStylus), true);
        AddHandler(Stylus.PreviewStylusDownEvent, new StylusDownEventHandler(TrackEraserStylus), true);
        AddHandler(Stylus.PreviewStylusUpEvent, new StylusEventHandler(TrackEraserStylus), true);
        StylusEnter += TrackEraserStylus;
        StylusLeave += (_, e) => { if (e.StylusDevice == _eraserPointerDevice) ClearEraserPointer(); };
        ActiveEditingModeChanged += (_, _) => UpdateEraserFootprint();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) ClearEraserPointer(); };
        IsHitTestVisibleChanged += (_, _) => { if (!IsHitTestVisible) ClearEraserPointer(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) ClearEraserPointer(); };
        // Zoom and scrolling move page coordinates under a stationary pointer.
        LayoutUpdated += (_, _) => { if (IsLoaded && _eraserPointer is not null) RefreshEraserPointer(); };
    }

    public void SetEraserShape(StylusShape shape)
    {
        EraserShape = shape;
        UpdateEraserFootprint();
    }

    private void TrackEraserMouse(object sender, MouseEventArgs e)
    {
        // Stylus events already carry the correct position, including inversion.
        // Promoted mouse events must not let touch move a pen's footprint.
        if (e.StylusDevice is not null || _isPenDown) return;
        SetEraserPointer(e.GetPosition(this), null);
    }

    private void TrackEraserStylus(object sender, StylusEventArgs e)
    {
        if (IsTouch(e.StylusDevice))
        {
            if (_eraserPointerDevice is null && !_isPenDown) ClearEraserPointer();
            return;
        }
        SetEraserPointer(e.GetPosition(this), e.StylusDevice);
    }

    private void SetEraserPointer(Point position, StylusDevice? device)
    {
        _eraserPointer = position;
        _eraserPointerDevice = device;
        UpdateEraserFootprint();
    }

    private void RefreshEraserPointer()
    {
        if (!IsLoaded || !IsVisible || !IsEnabled || !IsHitTestVisible || Window.GetWindow(this) is { IsActive: false })
        {
            ClearEraserPointer();
            return;
        }
        var device = _eraserPointerDevice ?? (IsStylusOver ? Stylus.CurrentStylusDevice : null);
        if (device is not null && !IsTouch(device) && (IsStylusOver || IsStylusCaptured))
            SetEraserPointer(device.GetPosition(this), device);
        else if (IsMouseOver && !IsStylusOver && _touchDevices.Count == 0)
            SetEraserPointer(Mouse.GetPosition(this), null);
        else ClearEraserPointer();
    }

    private void UpdateEraserFootprint()
    {
        if (_eraserPointer is not { } center || _finishingInput || !IsEnabled || !IsHitTestVisible ||
            ActiveEditingMode is not (InkCanvasEditingMode.EraseByPoint or InkCanvasEditingMode.EraseByStroke) ||
            (_touchDevices.Count > 0 && !_isPenDown) ||
            !new Rect(RenderSize).Contains(center))
        {
            SetEraserFootprint(null);
            return;
        }
        if (_footprintCenter == center && ReferenceEquals(_footprintShape, EraserShape) && EraserFootprint is not null) return;
        _footprintCenter = center;
        _footprintShape = EraserShape;
        // These are the very same page-DIP dimensions used by WPF hit testing.
        // The sibling visual inherits page zoom and monitor DPI along with ink.
        var bounds = new Rect(center.X - EraserShape.Width / 2, center.Y - EraserShape.Height / 2,
            EraserShape.Width, EraserShape.Height);
        Geometry geometry = EraserShape is EllipseStylusShape ? new EllipseGeometry(bounds) : new RectangleGeometry(bounds);
        if (EraserShape.Rotation != 0) geometry.Transform = new RotateTransform(EraserShape.Rotation, center.X, center.Y);
        geometry.Freeze();
        SetEraserFootprint(geometry);
    }

    private void ClearEraserPointer()
    {
        _eraserPointer = null;
        _eraserPointerDevice = null;
        SetEraserFootprint(null);
    }

    private void SetEraserFootprint(Geometry? geometry)
    {
        if (ReferenceEquals(EraserFootprint, geometry)) return;
        var wasVisible = EraserFootprint is not null;
        EraserFootprint = geometry;
        if (wasVisible != (geometry is not null))
        {
            UseCustomCursor = geometry is not null;
            if (geometry is not null) Cursor = Cursors.None;
            else ClearValue(CursorProperty);
        }
        EraserFootprintChanged?.Invoke(this, EventArgs.Empty);
    }
}
