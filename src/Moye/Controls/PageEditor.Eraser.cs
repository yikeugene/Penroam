using System.Windows;
using System.Windows.Media;

namespace Moye.Controls;

public sealed partial class PageEditor
{
    private sealed class EraserPreviewVisual : FrameworkElement
    {
        private static readonly Pen LightOutline = CreatePen(Brushes.White, 2);
        private static readonly Pen DarkOutline = CreatePen(Brushes.Black, 1);
        private Geometry? _footprint;

        public EraserPreviewVisual() { IsHitTestVisible = false; ClipToBounds = true; }

        public void SetFootprint(Geometry? footprint)
        {
            _footprint = footprint;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            if (_footprint is null) return;
            context.DrawGeometry(null, LightOutline, _footprint);
            context.DrawGeometry(null, DarkOutline, _footprint);
        }

        private static Pen CreatePen(Brush brush, double width)
        {
            var pen = new Pen(brush, width);
            pen.Freeze();
            return pen;
        }
    }
}
