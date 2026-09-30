using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Moye.Controls;

namespace Moye;

public partial class MainWindow
{
    private readonly TouchNavigationSession _touchNavigation = new();
    private Point _requestedTouchOffset;
    private bool _touchRendering, _applyingTouchFrame;
    private bool _touchScrollPending;
    private TimeSpan _lastTouchFrame = TimeSpan.MinValue;
    private long _viewportQuietAfter;
    private bool DeferViewportBackgroundWork => _touchNavigation.Count > 0 || _touchNavigation.IsInertiaActive ||
        _touchNavigation.HasPendingFrame || _mousePanning || Environment.TickCount64 < _viewportQuietAfter;
    private static double TouchTime => Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency);

    private void InitializeTouchNavigation()
    {
        // A real mouse action (including a scrollbar or tool button) takes over
        // from a fling. Touch-promoted mouse events must not cancel their gesture.
        PreviewMouseDown += (_, e) => { if (e.StylusDevice is null) ClearTouches(true); };
        PreviewStylusDown += (_, e) => { if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Stylus) ClearTouches(true); };
        PreviewTouchDown += (_, e) =>
        {
            // A fresh Down can reuse a device ID after an interrupted contact.
            _blockedTouches.Remove(e.TouchDevice.Id);
            if (e.OriginalSource is DependencyObject source && source != Viewport && !Viewport.IsAncestorOf(source)) ClearTouches(true);
        };
        PreviewTouchMove += (_, e) => { if (_blockedTouches.Contains(e.TouchDevice.Id)) e.Handled = true; };
        PreviewTouchUp += (_, e) => { if (_blockedTouches.Remove(e.TouchDevice.Id)) e.Handled = true; };
        // Some drivers promote touch to mouse. Never let a document scrollbar
        // track button turn an accidental finger/palm contact into PageDown.
        Viewport.PreviewMouseDown += SuppressScrollbarTouchMouse;
        Viewport.PreviewMouseMove += SuppressScrollbarTouchMouse;
        Viewport.PreviewMouseUp += SuppressScrollbarTouchMouse;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsBusy) && ViewModel.IsBusy) ClearTouches();
        };
        Deactivated += (_, _) => _blockedTouches.Clear();
        Closed += (_, _) => { ClearTouches(); _blockedTouches.Clear(); ResetThumbnailWork(); };
    }

    private void ViewportTouchDown(object sender, TouchEventArgs e)
    {
        e.Handled = true;
        if (!_ready || _closing || ViewModel.IsBusy || ViewModel.IsLibraryVisible || AnyPenDown || Environment.TickCount64 < _ignoreTouchUntil)
        { _blockedTouches.Add(e.TouchDevice.Id); return; }
        var scroll = GetScroll();
        var source = e.OriginalSource as DependencyObject;
        if (IsDocumentScrollBar(source, scroll))
        { _blockedTouches.Add(e.TouchDevice.Id); return; }
        // Keep deliberate controls, including text-box scrollbars, available
        // after applying the pen guard. Only the outer page scrollbar is touch-free.
        if (HasInputAncestor<ButtonBase>(source) || HasInputAncestor<ScrollBar>(source))
        { e.Handled = false; ClearTouches(true); return; }
        if (scroll is null) return;
        if (_touches.Count == 0)
        {
            // Catch a fling at the displayed position, including queued scroll
            // commands that have not reached the layout pass yet.
            ClearTouches();
            _requestedTouchOffset = new Point(scroll.HorizontalOffset, scroll.VerticalOffset);
        }
        else ApplyTouchFrame(TouchTime);
        _blockedTouches.Remove(e.TouchDevice.Id);
        if (!e.TouchDevice.Capture(Viewport)) return;
        _touches[e.TouchDevice.Id] = e.TouchDevice;
        _touchNavigation.BeginContact(e.TouchDevice.Id, e.GetTouchPoint(Viewport).Position, TouchTime,
            minimumPanDistance: HasInputAncestor<PageEditor>(source) ? 0 : 12);
        _viewportQuietAfter = Environment.TickCount64 + 180;
    }

    private void SuppressScrollbarTouchMouse(object sender, MouseEventArgs e)
    {
        if (PenInkCanvas.IsTouch(e.StylusDevice) && IsDocumentScrollBar(e.OriginalSource as DependencyObject, GetScroll()))
            e.Handled = true;
    }

    private static bool IsDocumentScrollBar(DependencyObject? source, ScrollViewer? documentScroll)
    {
        if (documentScroll is null) return false;
        var bar = FindInputAncestor<ScrollBar>(source);
        return bar is not null && ReferenceEquals(FindInputAncestor<ScrollViewer>(bar), documentScroll);
    }

    private void ViewportTouchMove(object sender, TouchEventArgs e)
    {
        if (_blockedTouches.Contains(e.TouchDevice.Id)) { e.Handled = true; return; }
        if (!_touches.ContainsKey(e.TouchDevice.Id)) return;
        e.Handled = true;
        if (AnyPenDown) { ClearTouches(true); return; }
        _touchNavigation.MoveContact(e.TouchDevice.Id, e.GetTouchPoint(Viewport).Position, TouchTime);
        RequestTouchFrame();
    }

    private void ViewportTouchUp(object sender, TouchEventArgs e)
    {
        if (_blockedTouches.Remove(e.TouchDevice.Id)) { e.Handled = true; return; }
        if (!_touches.ContainsKey(e.TouchDevice.Id)) return;
        e.Handled = true;
        var now = TouchTime;
        _touchNavigation.MoveContact(e.TouchDevice.Id, e.GetTouchPoint(Viewport).Position, now);
        ApplyTouchFrame(now); // Include the last position even without a final Move packet.
        _touchNavigation.EndContact(e.TouchDevice.Id, now);
        _touches.Remove(e.TouchDevice.Id);
        if (e.TouchDevice.Captured == Viewport) e.TouchDevice.Capture(null);
        if (_touchNavigation.IsInertiaActive) RequestTouchFrame();
        else FinishTouchFrameWork();
    }

    private void ViewportLostTouch(object sender, TouchEventArgs e)
    {
        // Expected releases remove the device first. An unexpected capture loss
        // cancels the whole gesture, so no stale movement or fling survives it.
        if (_touches.ContainsKey(e.TouchDevice.Id)) ClearTouches(true);
    }

    private void RequestTouchFrame()
    {
        if (_touchRendering) return;
        _touchRendering = true;
        CompositionTarget.Rendering += RenderTouchFrame;
    }

    private void RenderTouchFrame(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs rendering)
        {
            // WPF can raise Rendering again after layout changes in this frame.
            if (rendering.RenderingTime == _lastTouchFrame) return;
            _lastTouchFrame = rendering.RenderingTime;
        }
        if (_closing || !IsActive || ViewModel.IsBusy || ViewModel.IsLibraryVisible || AnyPenDown)
        { ClearTouches(true); return; }
        ApplyTouchFrame(TouchTime);
        if (!_touchNavigation.HasPendingFrame && !_touchNavigation.IsInertiaActive) FinishTouchFrameWork();
    }

    private void ApplyTouchFrame(double now)
    {
        if (_applyingTouchFrame || AnyPenDown || !_touchNavigation.TryTakeFrame(now, out var frame)) return;
        var scroll = GetScroll();
        if (scroll is null) return;
        _applyingTouchFrame = true;
        try
        {
            // Virtualization can reconcile estimated extents as mixed-size PDF
            // pages appear. Rebase only after ScrollChanged acknowledges layout.
            if (!_touchScrollPending) _requestedTouchOffset = new Point(scroll.HorizontalOffset, scroll.VerticalOffset);
            if (Math.Abs(frame.Scale - 1) > .000001)
            {
                // Reconcile the preceding frame, then preserve the paper point
                // under the OLD centroid before moving it to the new centroid.
                PageList.UpdateLayout();
                ChangeZoom(ViewModel.Zoom * frame.Scale, frame.PreviousCenter);
                _requestedTouchOffset = new Point(scroll.HorizontalOffset, scroll.VerticalOffset);
            }
            var desired = _requestedTouchOffset + frame.ScrollDelta;
            var next = new Point(Math.Clamp(desired.X, 0, scroll.ScrollableWidth), Math.Clamp(desired.Y, 0, scroll.ScrollableHeight));
            if (frame.IsInertial)
                _touchNavigation.StopInertiaAxes(next.X != desired.X || scroll.ScrollableWidth <= 0,
                    next.Y != desired.Y || scroll.ScrollableHeight <= 0);
            // Keep the requested value between render callbacks: ScrollTo*
            // queues commands and actual offsets can still be one layout behind.
            _requestedTouchOffset = next;
            scroll.ScrollToHorizontalOffset(next.X);
            scroll.ScrollToVerticalOffset(next.Y);
            _touchScrollPending = true;
            _viewportQuietAfter = Environment.TickCount64 + 180;
        }
        finally { _applyingTouchFrame = false; }
    }

    private void FinishTouchFrameWork()
    {
        if (_touchRendering) { CompositionTarget.Rendering -= RenderTouchFrame; _touchRendering = false; }
        _viewportQuietAfter = Environment.TickCount64 + 180;
        if (_touchNavigation.Count == 0) ScheduleFitWidth();
    }

    private void ClearTouches(bool block = false)
    {
        bool moving = _touchNavigation.Count > 0 || _touchNavigation.IsInertiaActive || _touchNavigation.HasPendingFrame || _touchScrollPending;
        _touchScrollPending = false;
        if (_touchRendering) { CompositionTarget.Rendering -= RenderTouchFrame; _touchRendering = false; }
        _touchNavigation.Cancel();
        var devices = _touches.Values.ToArray();
        // A rejected palm stays rejected until Up, even when a pen, another
        // touch, navigation or a popup cancels the active gesture in between.
        if (block) foreach (var id in _touches.Keys) _blockedTouches.Add(id);
        _touches.Clear();
        foreach (var device in devices) if (device.Captured == Viewport) device.Capture(null);
        if (moving && GetScroll() is { } scroll)
        {
            _requestedTouchOffset = new Point(scroll.HorizontalOffset, scroll.VerticalOffset);
            // Overwrite any pending command. The pen's early gate additionally
            // drains layout synchronously before accepting its first ink sample.
            scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset);
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset);
        }
    }
}
