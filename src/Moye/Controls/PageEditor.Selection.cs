using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using Moye.Models;

namespace Moye.Controls;

public sealed partial class PageEditor
{
    // Native InkCanvas lasso selection and transforms operate on these invisible
    // rectangles; the native text editor stays in its own layer for IME/clipboard.
    private readonly Dictionary<NoteItemFrame, Border> _selectionProxies = [];
    private readonly HashSet<NoteItemFrame> _selectedFrames = [];

    public bool HasSelection => _selectedFrames.Count > 0 || _ink.GetSelectedStrokes().Count > 0;

    private void AddSelectionProxy(NoteItemFrame frame)
    {
        var proxy = new Border { Opacity = 0, Background = Brushes.Transparent, IsHitTestVisible = false, Focusable = false };
        _selectionProxies.Add(frame, proxy);
        _ink.Children.Add(proxy);
        UpdateSelectionProxy(frame);
        proxy.Visibility = _tool == InkTool.Lasso ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSelectionProxy(NoteItemFrame frame)
    {
        if (!_selectionProxies.TryGetValue(frame, out var proxy)) return;
        System.Windows.Controls.InkCanvas.SetLeft(proxy, Canvas.GetLeft(frame));
        System.Windows.Controls.InkCanvas.SetTop(proxy, Canvas.GetTop(frame));
        proxy.Width = frame.Width; proxy.Height = frame.Height;
    }

    private void UpdateMixedSelection()
    {
        var selected = _ink.GetSelectedElements();
        var frames = _selectionProxies.Where(pair => selected.Contains(pair.Value)).Select(pair => pair.Key).ToArray();
        var previous = SelectedText;
        foreach (var frame in _selectedFrames) frame.SetSelected(false);
        _selectedFrames.Clear();
        foreach (var frame in frames) _selectedFrames.Add(frame);
        _selectedItem = frames.Length == 1 && _ink.GetSelectedStrokes().Count == 0 ? frames[0] : null;
        if (previous is not null || SelectedText is not null) NotifyTextSelectionChanged();
    }

    private Rect ConstrainSelection(Rect rectangle)
    {
        var width = Math.Clamp(rectangle.Width, 1, Page.Width);
        var height = Math.Clamp(rectangle.Height, 1, Page.Height);
        return new Rect(Math.Clamp(rectangle.X, 0, Math.Max(0, Page.Width - width)),
            Math.Clamp(rectangle.Y, 0, Math.Max(0, Page.Height - height)), width, height);
    }

    private void CompleteSelectionTransform()
    {
        foreach (var frame in _selectedFrames)
        {
            var proxy = _selectionProxies[frame];
            SetFrameGeometry(frame, System.Windows.Controls.InkCanvas.GetLeft(proxy), System.Windows.Controls.InkCanvas.GetTop(proxy), proxy.Width, proxy.Height);
        }
        if (_selectedFrames.Count > 0) MarkContentDirty();
        if (_ink.GetSelectedStrokes().Count > 0) MarkInkDirty();
        FlushChanges();
    }

    private void SetFrameGeometry(NoteItemFrame frame, double x, double y, double width, double height)
    {
        Canvas.SetLeft(frame, x); Canvas.SetTop(frame, y);
        frame.Width = width; frame.Height = height;
        if (frame.Item is NoteText text)
        {
            text.X = x; text.Y = y; text.Width = width; text.Height = height;
            RefreshTextLayout(frame, grow: false);
        }
        else if (frame.Item is NoteImage image)
        { image.X = x; image.Y = y; image.Width = width; image.Height = height; }
        UpdateSelectionProxy(frame);
    }

    private void RemoveFrame(NoteItemFrame frame)
    {
        if (frame.Item is NoteText text) { Page.Texts.Remove(text); _overflowingTexts.Remove(text.Id); }
        if (frame.ItemContent is TextBox box) _composingTexts.Remove(box);
        if (frame.Item is NoteImage image) Page.Images.Remove(image);
        if (_selectionProxies.Remove(frame, out var proxy)) _ink.Children.Remove(proxy);
        _items.Children.Remove(frame); _frames.Remove(frame);
    }

    public void SelectAllContent()
    {
        _tool = InkTool.Lasso; ApplyTool();
        _ink.Select(_ink.Strokes, _selectionProxies.Values.Cast<UIElement>());
    }

    public bool BeginTypingText(string textId)
    {
        var frame = _frames.FirstOrDefault(frame => frame.Item is NoteText text && text.Id == textId);
        if (frame is null) return false;
        _tool = InkTool.Text; ApplyTool(); SelectItem(frame); FocusSelectedText();
        return true;
    }

    public bool NudgeSelection(double dx, double dy)
    {
        if (!HasSelection || !double.IsFinite(dx) || !double.IsFinite(dy) || IsTextComposing) return false;
        CommitPendingEdits();
        var strokes = _ink.GetSelectedStrokes();
        var bounds = strokes.Count > 0 ? strokes.GetBounds() : Rect.Empty;
        foreach (var frame in _selectedFrames) bounds.Union(new Rect(Canvas.GetLeft(frame), Canvas.GetTop(frame), frame.Width, frame.Height));
        dx = Math.Clamp(dx, -bounds.Left, Math.Max(-bounds.Left, Page.Width - bounds.Right));
        dy = Math.Clamp(dy, -bounds.Top, Math.Max(-bounds.Top, Page.Height - bounds.Bottom));
        if (dx == 0 && dy == 0) return true;
        foreach (var frame in _selectedFrames) SetFrameGeometry(frame, Canvas.GetLeft(frame) + dx, Canvas.GetTop(frame) + dy, frame.Width, frame.Height);
        if (_selectedFrames.Count > 0) MarkContentDirty();
        if (strokes.Count > 0) { strokes.Transform(new Matrix(1, 0, 0, 1, dx, dy), false); MarkInkDirty(); }
        // Rebuild the native adornment after attached-property changes.
        if (_tool == InkTool.Lasso) _ink.Select(strokes, _selectedFrames.Select(frame => (UIElement)_selectionProxies[frame]).ToArray());
        FlushChanges();
        return true;
    }

    public async Task<NoteSelection?> ExportSelectionAsync()
    {
        CommitPendingEdits();
        var payload = new NoteSelection
        {
            InkData = ExportSelectedInk() ?? [],
            Texts = _selectedFrames.Select(frame => frame.Item).OfType<NoteText>().Select(text => text with { }).ToList(),
            Images = _selectedFrames.Select(frame => frame.Item).OfType<NoteImage>().Select(image => image with { }).ToList()
        };
        if (payload.IsEmpty) return null;
        foreach (var id in payload.Images.Select(image => image.AssetId).Distinct()) payload.Assets.Add(await _loadAsset(id));
        payload.Validate();
        return payload;
    }

    /// <summary>Caller stores/remaps assets before this single undoable document edit.</summary>
    public void ImportSelection(NoteSelection payload, Point position, IReadOnlyDictionary<string, string>? assetMap = null)
    {
        payload.Validate();
        if (payload.IsEmpty) return;
        var bounds = payload.Bounds();
        var scale = Math.Min(1, Math.Min(Math.Max(1, Page.Width - 32) / Math.Max(1, bounds.Width), Math.Max(1, Page.Height - 32) / Math.Max(1, bounds.Height)));
        var x = Math.Clamp(position.X, 0, Math.Max(0, Page.Width - bounds.Width * scale - 1));
        var y = Math.Clamp(position.Y, 0, Math.Max(0, Page.Height - bounds.Height * scale - 1));
        var strokes = payload.InkData.Length == 0 ? new StrokeCollection() : new StrokeCollection(new MemoryStream(payload.InkData, false));
        strokes.Transform(new Matrix(scale, 0, 0, scale, x - bounds.X * scale, y - bounds.Y * scale), scale != 1);
        var texts = payload.Texts.Select(text => text with
        {
            Id = Guid.NewGuid().ToString("N"), X = x + (text.X - bounds.X) * scale, Y = y + (text.Y - bounds.Y) * scale,
            Width = text.Width * scale, Height = text.Height * scale, FontSize = Math.Clamp(text.FontSize * scale, 6, 128)
        }).ToArray();
        var images = payload.Images.Select(image => image with
        {
            Id = Guid.NewGuid().ToString("N"), AssetId = assetMap is null ? image.AssetId : assetMap[image.AssetId],
            X = x + (image.X - bounds.X) * scale, Y = y + (image.Y - bounds.Y) * scale,
            Width = image.Width * scale, Height = image.Height * scale
        }).ToArray();
        CommitPendingEdits();
        _tool = InkTool.Lasso; ApplyTool();
        var added = new List<NoteItemFrame>();
        foreach (var image in images) { Page.Images.Add(image); added.Add(AddImageFrame(image)); }
        foreach (var text in texts) { Page.Texts.Add(text); added.Add(AddTextFrame(text)); }
        _ink.Strokes.Add(strokes);
        _ink.Select(strokes, added.Select(frame => (UIElement)_selectionProxies[frame]));
        if (added.Count > 0) MarkContentDirty();
        if (strokes.Count > 0) MarkInkDirty();
        FlushChanges();
    }

    private void DuplicateMixedSelection()
    {
        if (!HasSelection) return;
        CommitPendingEdits();
        var originalFrames = _selectedFrames.ToArray();
        var copy = _ink.GetSelectedStrokes().Clone();
        var bounds = copy.Count > 0 ? copy.GetBounds() : Rect.Empty;
        foreach (var frame in originalFrames) bounds.Union(new Rect(Canvas.GetLeft(frame), Canvas.GetTop(frame), frame.Width, frame.Height));
        var dx = Math.Min(20, Math.Max(0, Page.Width - bounds.Right));
        var dy = Math.Min(20, Math.Max(0, Page.Height - bounds.Bottom));
        var added = new List<NoteItemFrame>();
        foreach (var frame in originalFrames)
        {
            if (frame.Item is NoteText text)
            {
                var item = text with { Id = Guid.NewGuid().ToString("N"), X = text.X + dx, Y = text.Y + dy };
                Page.Texts.Add(item); added.Add(AddTextFrame(item));
            }
            else if (frame.Item is NoteImage image)
            {
                var item = image with { Id = Guid.NewGuid().ToString("N"), X = image.X + dx, Y = image.Y + dy };
                Page.Images.Add(item); added.Add(AddImageFrame(item));
            }
        }
        copy.Transform(new Matrix(1, 0, 0, 1, dx, dy), false);
        _ink.Strokes.Add(copy);
        if (copy.Count == 0 && added.Count == 1 && _tool != InkTool.Lasso) SelectItem(added[0]);
        else { _tool = InkTool.Lasso; ApplyTool(); _ink.Select(copy, added.Select(frame => (UIElement)_selectionProxies[frame])); }
        if (added.Count > 0) MarkContentDirty();
        if (copy.Count > 0) MarkInkDirty();
        FlushChanges();
    }
}
