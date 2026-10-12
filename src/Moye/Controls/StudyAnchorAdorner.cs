using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Moye.Models;

namespace Moye.Controls;

internal sealed class StudyAnchorAdorner : Adorner
{
    private readonly Rect _bounds;
    private StudyAnchorAdorner(UIElement element, StudyAnchor anchor) : base(element)
    {
        IsHitTestVisible = false; _bounds = new Rect(anchor.X, anchor.Y, anchor.Width, anchor.Height);
    }
    protected override void OnRender(DrawingContext context)
    {
        var fill = new SolidColorBrush(Color.FromArgb(32, 218, 147, 27));
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(189, 115, 14)), 2) { DashStyle = DashStyles.Dash };
        context.DrawRectangle(fill, pen, _bounds);
    }
    public static void Show(UIElement element, StudyAnchor anchor)
    {
        if (AdornerLayer.GetAdornerLayer(element) is not { } layer) return;
        foreach (var previous in layer.GetAdorners(element)?.OfType<StudyAnchorAdorner>().ToArray() ?? []) layer.Remove(previous);
        var adorner = new StudyAnchorAdorner(element, anchor); layer.Add(adorner);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timer.Stop(); layer.Remove(adorner); }; timer.Start();
    }
}
