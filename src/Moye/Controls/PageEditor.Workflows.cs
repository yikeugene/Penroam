using System.Windows;
using System.Windows.Controls;
using Moye.Models;

namespace Moye.Controls;

public sealed partial class PageEditor
{
    public Rect? SelectedContentBounds
    {
        get
        {
            var bounds = _ink.GetSelectedStrokes().GetBounds();
            foreach (var frame in _selectedFrames) bounds.Union(new Rect(Canvas.GetLeft(frame), Canvas.GetTop(frame), frame.Width, frame.Height));
            return bounds.IsEmpty ? null : bounds;
        }
    }
}
