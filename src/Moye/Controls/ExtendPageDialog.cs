using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Models;
using Moye.Services;

namespace Moye.Controls;

/// <summary>Stages additional writing space without changing the source page.</summary>
public sealed class ExtendPageDialog : Window
{
    private const double DipPerMillimeter = 96 / 25.4;
    private readonly NotePage _page;
    private readonly Canvas _canvas = new() { ClipToBounds = true, MinHeight = 150 };
    private readonly ExtensionPreview _preview;
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly List<(Thumb Handle, int X, int Y)> _handles = [];
    private readonly TextBlock _dimensions = new() { FontWeight = FontWeights.SemiBold, FontSize = 13 };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MinHeight = 17 };
    private readonly Button _apply;
    private PageMargins _margins;
    private bool _settingFields;
    private DragSession? _drag;

    public PageMargins Margins => _margins;

    public ExtendPageDialog(NotePage page, BitmapSource? preview = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page.Snapshot();
        Title = "Extend page"; Width = 640; Height = 640; MinWidth = 540; MinHeight = 610;
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = Brushes.White;
        _preview = new ExtensionPreview(preview);
        _canvas.Children.Add(_preview);
        _canvas.SizeChanged += (_, _) => UpdatePreview();

        var root = new Grid { Margin = new Thickness(16) };
        foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto })
            root.RowDefinitions.Add(new RowDefinition { Height = height });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock { Text = "More room for your notes", FontSize = 23, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "Drag an edge or corner, or enter extra space below.\nYour PDF and notes keep their original size.",
            FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#65736D"), Margin = new Thickness(0, 4, 0, 0)
        });
        root.Children.Add(header);
        var previewFrame = new Border
        {
            Background = Brush("#F5F4F0"), BorderBrush = Brush("#DDE3DC"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 8), Child = _canvas
        };
        Grid.SetRow(previewFrame, 1); root.Children.Add(previewFrame);
        AddHandle(-1, -1, "Top left", Cursors.SizeNWSE); AddHandle(0, -1, "Top", Cursors.SizeNS);
        AddHandle(1, -1, "Top right", Cursors.SizeNESW); AddHandle(-1, 0, "Left", Cursors.SizeWE);
        AddHandle(1, 0, "Right", Cursors.SizeWE); AddHandle(-1, 1, "Bottom left", Cursors.SizeNESW);
        AddHandle(0, 1, "Bottom", Cursors.SizeNS); AddHandle(1, 1, "Bottom right", Cursors.SizeNWSE);

        var actions = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var _ in Enumerable.Range(0, 4)) actions.ColumnDefinitions.Add(new ColumnDefinition());
        AddAction("Right +50 mm", 0, () => AddSpace(50, 0));
        AddAction("Bottom +50 mm", 1, () => AddSpace(0, 50));
        AddAction("Both +50 mm", 2, () => AddSpace(50, 50));
        AddAction("Reset", 3, () => SetMargins(default));
        void AddAction(string title, int column, Action action)
        {
            var button = MakeButton(title, action); button.Padding = new Thickness(6); button.FontSize = 12;
            Grid.SetColumn(button, column); actions.Children.Add(button);
        }
        Grid.SetRow(actions, 2); root.Children.Add(actions);

        var inputs = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        var sides = new[] { "Left", "Top", "Right", "Bottom" };
        for (var index = 0; index < sides.Length; index++)
        {
            var side = sides[index]; inputs.ColumnDefinitions.Add(new ColumnDefinition());
            var group = new StackPanel { Margin = new Thickness(index == 0 ? 0 : 4, 0, index == 3 ? 0 : 4, 0) };
            var input = new TextBox
            {
                Text = "0", MinHeight = 44, MinWidth = 44, Padding = new Thickness(10, 6, 10, 6),
                VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Right
            };
            AutomationProperties.SetName(input, $"{side} extension in millimeters");
            AutomationProperties.SetHelpText(input, "Extra blank space. Use Up or Down for 1 mm, or Shift with Up or Down for 10 mm.");
            input.TextChanged += (_, _) => { if (!_settingFields) ReadFields(); };
            input.PreviewKeyDown += (_, e) => AdjustNumber(input, e);
            group.Children.Add(new Label { Content = $"{side} (mm)", Target = input, FontSize = 12, Padding = new Thickness(0, 0, 0, 3) });
            group.Children.Add(input); _fields.Add(side, input); Grid.SetColumn(group, index); inputs.Children.Add(group);
        }
        Grid.SetRow(inputs, 3); root.Children.Add(inputs);
        var summary = new StackPanel(); summary.Children.Add(_dimensions); summary.Children.Add(_status);
        Grid.SetRow(summary, 4); root.Children.Add(summary);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = MakeButton("Cancel", () => DialogResult = false); cancel.IsCancel = true;
        _apply = MakeButton("Extend page", () => { if (ReadFields()) DialogResult = true; });
        _apply.IsDefault = true; _apply.Style = TryFindResource("PrimaryButton") as Style;
        footer.Children.Add(cancel); footer.Children.Add(_apply); Grid.SetRow(footer, 5); root.Children.Add(footer);
        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _drag is null) return;
            var session = _drag; _drag = null; session.Handle.CancelDrag(); SetMargins(session.Before); e.Handled = true;
        };
        SetMargins(default);
    }

    private Button MakeButton(string text, Action action)
    {
        var button = new Button { Content = text, MinHeight = 44, MinWidth = 44, Margin = new Thickness(2, 0, 2, 0), Style = TryFindResource("SecondaryButton") as Style };
        button.Click += (_, _) => action(); AutomationProperties.SetName(button, text); return button;
    }

    private void AddSpace(double rightMm, double bottomMm)
    {
        if (!TryReadMargins(out var margins)) return;
        SetMargins(margins with { Right = margins.Right + rightMm * DipPerMillimeter, Bottom = margins.Bottom + bottomMm * DipPerMillimeter });
    }

    private static void AdjustNumber(TextBox input, KeyEventArgs args)
    {
        if (args.Key is not (Key.Up or Key.Down) || !TryNumber(input.Text, out var value)) return;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        input.Text = Math.Max(0, value + (args.Key == Key.Up ? step : -step)).ToString("0.##", CultureInfo.CurrentCulture);
        input.SelectAll(); args.Handled = true;
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.CurrentCulture, out value) && double.IsFinite(value) && value >= 0;

    private bool TryReadMargins(out PageMargins result)
    {
        var values = new List<double>(); var valid = true;
        foreach (var field in _fields.Values)
        {
            var accepted = TryNumber(field.Text, out var value);
            field.BorderBrush = Brush(accepted ? "#DDE3DC" : "#AA413C");
            values.Add(value * DipPerMillimeter); valid &= accepted;
        }
        result = valid ? new(values[0], values[1], values[2], values[3]) : default;
        return valid;
    }

    private bool ReadFields()
    {
        var valid = TryReadMargins(out var margins);
        var withinLimit = valid && _page.Width + margins.Left + margins.Right <= PageExtensionService.MaxDimension &&
            _page.Height + margins.Top + margins.Bottom <= PageExtensionService.MaxDimension;
        _apply.IsEnabled = withinLimit && PageExtensionService.CanExtend(_page, margins);
        foreach (var (handle, _, _) in _handles) handle.IsEnabled = withinLimit;
        _status.Foreground = Brush(!valid || !withinLimit ? "#AA413C" : "#65736D");
        _status.Text = !valid ? "Enter a number of 0 or more for each side." : !withinLimit
            ? $"The page can be at most {PageExtensionService.MaxDimension / DipPerMillimeter:0} mm wide or tall."
            : _apply.IsEnabled ? "Green shows the extra writing space. Apply once, undo once." : "Add blank space to one or more sides.";
        if (withinLimit)
        {
            _margins = margins;
            _dimensions.Text = $"Page size: {(_page.Width + margins.Left + margins.Right) / DipPerMillimeter:0.#} × {(_page.Height + margins.Top + margins.Bottom) / DipPerMillimeter:0.#} mm";
            UpdatePreview();
        }
        return _apply.IsEnabled;
    }

    private void SetMargins(PageMargins margins)
    {
        _settingFields = true;
        var values = new[] { margins.Left, margins.Top, margins.Right, margins.Bottom };
        for (var index = 0; index < values.Length; index++) _fields.ElementAt(index).Value.Text = (values[index] / DipPerMillimeter).ToString("0.##", CultureInfo.CurrentCulture);
        _settingFields = false; ReadFields();
    }

    private void AddHandle(int x, int y, string name, Cursor cursor)
    {
        var handle = new Thumb { Width = 44, Height = 44, Cursor = cursor, Focusable = false, ToolTip = $"Drag to extend {name.ToLowerInvariant()}" };
        var hitArea = new FrameworkElementFactory(typeof(Grid)); hitArea.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        var grip = new FrameworkElementFactory(typeof(Border)); grip.SetValue(WidthProperty, x == 0 ? 28d : 10d); grip.SetValue(HeightProperty, y == 0 ? 28d : 10d);
        grip.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); grip.SetValue(Border.BackgroundProperty, Brush("#236451"));
        grip.SetValue(Border.BorderBrushProperty, Brushes.White); grip.SetValue(Border.BorderThicknessProperty, new Thickness(1)); hitArea.AppendChild(grip);
        handle.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = hitArea };
        AutomationProperties.SetName(handle, $"Extend {name.ToLowerInvariant()} edge");
        handle.DragStarted += (_, _) =>
        {
            if (!TryReadMargins(out var margins)) { handle.CancelDrag(); return; }
            var (bounds, scale) = Geometry();
            _drag = new(handle, margins, new Point(bounds.Left + margins.Left * scale, bounds.Top + margins.Top * scale), scale);
        };
        handle.DragDelta += (_, e) =>
        {
            if (_drag is not { } drag || drag.Handle != handle) return;
            drag.DeltaX += e.HorizontalChange; drag.DeltaY += e.VerticalChange;
            var before = drag.Before;
            var left = x < 0 ? Math.Clamp(before.Left - drag.DeltaX / drag.Scale, 0, Math.Max(0, PageExtensionService.MaxDimension - _page.Width - before.Right)) : before.Left;
            var right = x > 0 ? Math.Clamp(before.Right + drag.DeltaX / drag.Scale, 0, Math.Max(0, PageExtensionService.MaxDimension - _page.Width - before.Left)) : before.Right;
            var top = y < 0 ? Math.Clamp(before.Top - drag.DeltaY / drag.Scale, 0, Math.Max(0, PageExtensionService.MaxDimension - _page.Height - before.Bottom)) : before.Top;
            var bottom = y > 0 ? Math.Clamp(before.Bottom + drag.DeltaY / drag.Scale, 0, Math.Max(0, PageExtensionService.MaxDimension - _page.Height - before.Top)) : before.Bottom;
            SetMargins(new(left, top, right, bottom));
        };
        handle.DragCompleted += (_, e) =>
        {
            if (_drag is not { } drag) return;
            _drag = null; if (e.Canceled) SetMargins(drag.Before); else UpdatePreview();
        };
        _handles.Add((handle, x, y)); _canvas.Children.Add(handle);
    }

    private (Rect Bounds, double Scale) Geometry()
    {
        var width = _page.Width + _margins.Left + _margins.Right;
        var height = _page.Height + _margins.Top + _margins.Bottom;
        if (_drag is { } drag)
            return (new Rect(drag.Original.X - _margins.Left * drag.Scale, drag.Original.Y - _margins.Top * drag.Scale, width * drag.Scale, height * drag.Scale), drag.Scale);
        var scale = Math.Max(.0001, Math.Min(Math.Max(1, _canvas.ActualWidth - 100) / width, Math.Max(1, _canvas.ActualHeight - 66) / height));
        return (new Rect((_canvas.ActualWidth - width * scale) / 2, (_canvas.ActualHeight - height * scale) / 2, width * scale, height * scale), scale);
    }

    private void UpdatePreview()
    {
        if (_canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0) return;
        var (bounds, scale) = Geometry();
        _preview.Width = _canvas.ActualWidth; _preview.Height = _canvas.ActualHeight;
        _preview.Update(bounds, new Rect(bounds.Left + _margins.Left * scale, bounds.Top + _margins.Top * scale, _page.Width * scale, _page.Height * scale));
        foreach (var (handle, x, y) in _handles)
        {
            Canvas.SetLeft(handle, (x < 0 ? bounds.Left : x > 0 ? bounds.Right : bounds.Left + bounds.Width / 2) - 22);
            Canvas.SetTop(handle, (y < 0 ? bounds.Top : y > 0 ? bounds.Bottom : bounds.Top + bounds.Height / 2) - 22);
        }
    }

    private sealed class DragSession(Thumb handle, PageMargins before, Point original, double scale)
    {
        public Thumb Handle { get; } = handle;
        public PageMargins Before { get; } = before;
        public Point Original { get; } = original;
        public double Scale { get; } = scale;
        public double DeltaX { get; set; }
        public double DeltaY { get; set; }
    }

    private sealed class ExtensionPreview(BitmapSource? image) : FrameworkElement
    {
        private Rect _bounds, _original;
        public void Update(Rect bounds, Rect original) { _bounds = bounds; _original = original; InvalidateVisual(); }
        protected override void OnRender(DrawingContext drawing)
        {
            if (_bounds.IsEmpty || _bounds.Width <= 0) return;
            drawing.DrawRectangle(Brush("#E0EEE4"), new Pen(Brush("#236451"), 1.5), _bounds);
            drawing.DrawRectangle(Brushes.White, null, _original);
            if (image is not null) drawing.DrawImage(image, _original);
            else if (_original.Width > 70 && _original.Height > 45)
            {
                var label = new FormattedText("Current page", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 11, Brush("#65736D"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                drawing.DrawText(label, new Point(_original.Left + (_original.Width - label.Width) / 2, _original.Top + (_original.Height - label.Height) / 2));
            }
            drawing.DrawRectangle(null, new Pen(Brush("#A9B8AF"), 1) { DashStyle = DashStyles.Dash }, _original);
        }
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush;
    }
}
