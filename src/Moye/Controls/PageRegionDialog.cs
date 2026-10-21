using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Moye.Models;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

/// <summary>Selection is expressed in original page coordinates, independent of preview resolution.</summary>
public sealed class PageRegionDialog : Window
{
    private readonly Canvas _canvas;
    private readonly Rectangle _selection = new() { Stroke = Brushes.SeaGreen, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(35, 30, 130, 90)), IsHitTestVisible = false };
    private readonly double _scale;
    private Point? _start;
    private readonly NotePage _page;
    private readonly TextBlock _size = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox[] _coordinates;
    public Rect Region { get; private set; }

    public PageRegionDialog(Window owner, NotePage page, BitmapSource preview, string title, Rect? initial = null)
    {
        Owner = owner?.IsLoaded == true ? owner : null; Title = title; Width = 680; Height = 690;
        MinWidth = 470; MinHeight = 400; MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = Brushes.White;
        _page = page; _scale = Math.Min(580 / page.Width, 410 / page.Height);
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = L.Text("Controls.PageRegion.Intro"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(header);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom);
        var coordinateRow = new WrapPanel();
        _coordinates = new[] { L.Text("Controls.PageRegion.Left"), L.Text("Controls.PageRegion.Top"), L.Text("Controls.PageRegion.Width"), L.Text("Controls.PageRegion.Height") }.Select(label =>
        {
            var field = new TextBox { Width = 96, MinHeight = 44, FontSize = 12, Padding = new Thickness(8), Margin = new Thickness(3) };
            AutomationProperties.SetName(field, L.Text("Controls.PageRegion.CoordinateName", label));
            var group = new StackPanel(); group.Children.Add(new Label { Content = label, Target = field }); group.Children.Add(field); coordinateRow.Children.Add(group); return field;
        }).ToArray();
        footer.Children.Add(coordinateRow); footer.Children.Add(_size);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(OrganizationDialogUi.Button(this, L.Text("Controls.PageRegion.WholePage"), () => SetRegion(new Rect(0, 0, page.Width, page.Height))));
        var cancel = OrganizationDialogUi.Button(this, L.Text("Controls.Common.Cancel"), () => DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel);
        buttons.Children.Add(OrganizationDialogUi.Button(this, L.Text("Controls.PageRegion.Use"), () =>
        {
            var values = new double[4];
            for (var i = 0; i < 4; i++)
                if (!double.TryParse(_coordinates[i].Text, out values[i]) || !double.IsFinite(values[i])) { _size.Text = L.Text("Controls.PageRegion.InvalidNumbers"); return; }
            if (values[0] < 0 || values[1] < 0 || values[2] < 1 || values[3] < 1 || values[0] + values[2] > page.Width + .01 || values[1] + values[3] > page.Height + .01)
            { _size.Text = L.Text("Controls.PageRegion.InvalidRegion"); return; }
            Region = new Rect(values[0], values[1], Math.Min(values[2], page.Width - values[0]), Math.Min(values[3], page.Height - values[1]));
            DialogResult = true;
        }));
        footer.Children.Add(buttons); root.Children.Add(footer);
        _canvas = new Canvas { Width = page.Width * _scale, Height = page.Height * _scale, Background = Brushes.White, Cursor = Cursors.Cross, ClipToBounds = true };
        _canvas.Children.Add(new Image { Source = preview, Width = _canvas.Width, Height = _canvas.Height, Stretch = Stretch.Fill, IsHitTestVisible = false });
        _canvas.Children.Add(_selection);
        _canvas.MouseLeftButtonDown += (_, e) => { _start = Position(e); _canvas.CaptureMouse(); e.Handled = true; };
        _canvas.MouseMove += (_, e) => { if (_start is { } start) SetRegion(new Rect(start, Position(e))); };
        _canvas.MouseLeftButtonUp += (_, e) => { if (_start is { } start) SetRegion(new Rect(start, Position(e))); _start = null; _canvas.ReleaseMouseCapture(); e.Handled = true; };
        _canvas.LostMouseCapture += (_, _) => _start = null;
        root.Children.Add(new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root; SetRegion(initial is { IsEmpty: false } rect ? Rect.Intersect(rect, new Rect(0, 0, page.Width, page.Height)) : new Rect(0, 0, page.Width, page.Height));
    }

    private Point Position(MouseEventArgs e)
    {
        var point = e.GetPosition(_canvas);
        return new(Math.Clamp(point.X / _scale, 0, _page.Width), Math.Clamp(point.Y / _scale, 0, _page.Height));
    }

    private void SetRegion(Rect rect)
    {
        if (rect.IsEmpty) return;
        Region = rect; Canvas.SetLeft(_selection, rect.X * _scale); Canvas.SetTop(_selection, rect.Y * _scale);
        _selection.Width = rect.Width * _scale; _selection.Height = rect.Height * _scale;
        var values = new[] { rect.X, rect.Y, rect.Width, rect.Height };
        for (var i = 0; i < 4; i++) _coordinates[i].Text = values[i].ToString("0.######");
        _size.Text = L.Text("Controls.PageRegion.ScaleHint");
    }
}
