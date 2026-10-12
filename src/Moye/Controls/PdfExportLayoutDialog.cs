using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Models;
using Moye.Services;

namespace Moye.Controls;

/// <summary>Shows the actual sheet grid before any destination file is written.</summary>
public sealed class PdfExportLayoutDialog : Window
{
    private readonly IReadOnlyList<NotePage> _pages;
    private readonly PagePreviewRenderer _renderer;
    private readonly ComboBox _layout = new() { MinHeight = 44, ItemsSource = new[] { "Original page sizes", "A4 sheets · actual size" }, SelectedIndex = 0 };
    private readonly ComboBox _orientation = new() { MinHeight = 44, ItemsSource = new[] { "Automatic · fewest sheets", "Portrait", "Landscape" }, SelectedIndex = 0 };
    private readonly TextBox _overlap = new() { MinHeight = 44, Text = "10", VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 6, 0, 6) };
    private readonly SheetPreview _preview = new() { MinHeight = 200, Height = 230, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox _page = new() { MinHeight = 44 };
    private readonly Button _accept;
    private CancellationTokenSource? _previewCancellation;
    private IReadOnlyList<PdfExportTile> _tiles = [];
    private bool _closed;
    public PdfExportOptions Options { get; private set; } = PdfExportOptions.Original;
    public int OutputPageCount => _tiles.Count;

    public PdfExportLayoutDialog(Window owner, IReadOnlyList<NotePage> pages, PagePreviewRenderer renderer)
    {
        Owner = owner; Title = "PDF page layout"; Width = 640; Height = 700; MinWidth = 520; MinHeight = 560;
        MaxHeight = SystemParameters.WorkArea.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _pages = pages; _renderer = renderer;
        var root = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinHeight = 44, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), Style = TryFindResource("SecondaryButton") as Style });
        _accept = new Button { Content = "Continue to save", IsDefault = true, MinHeight = 44, Margin = new Thickness(8, 0, 0, 0), Style = TryFindResource("PrimaryButton") as Style };
        _accept.Click += (_, _) => { if (_accept.IsEnabled) DialogResult = true; }; footer.Children.Add(_accept);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = "Keep expanded notes readable", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        body.Children.Add(new TextBlock { Text = "Keep each canvas intact, or divide it across A4 sheets without shrinking handwriting or text.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        AddField(body, "Page layout", _layout);
        var settings = new Grid { Margin = new Thickness(0, 10, 0, 0) }; settings.ColumnDefinitions.Add(new()); settings.ColumnDefinitions.Add(new() { Width = new GridLength(150) });
        var orientationPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; AddField(orientationPanel, "A4 orientation", _orientation); settings.Children.Add(orientationPanel);
        var overlapPanel = new StackPanel(); AddField(overlapPanel, "Overlap (0–30 mm)", _overlap); Grid.SetColumn(overlapPanel, 1); settings.Children.Add(overlapPanel); body.Children.Add(settings);
        body.Children.Add(_summary); AddField(body, "Preview source page", _page); body.Children.Add(_preview);
        body.Children.Add(new TextBlock { Text = "A4 sheets read left to right, then top to bottom. Overlap repeats a narrow strip so notes remain legible at joins. Print at Actual size / 100% to retain scale.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = Brushes.DimGray });
        body.Children.Add(_error); Content = root;
        _page.ItemsSource = pages.Select((page, index) => $"Page {index + 1} · {page.Width / PdfExportPagination.DipsPerMillimeter:0.#} × {page.Height / PdfExportPagination.DipsPerMillimeter:0.#} mm").ToArray();
        _layout.SelectionChanged += (_, _) => UpdatePlan(); _orientation.SelectionChanged += (_, _) => UpdatePlan(); _overlap.TextChanged += (_, _) => UpdatePlan();
        _page.SelectionChanged += async (_, _) => await UpdatePreviewAsync();
        Closed += (_, _) => { _closed = true; _previewCancellation?.Cancel(); };
        UpdatePlan(); _page.SelectedIndex = 0;
    }

    private static void AddField(Panel parent, string label, FrameworkElement field)
    {
        parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) }); parent.Children.Add(field); AutomationProperties.SetName(field, label);
    }

    private void UpdatePlan()
    {
        var tiled = _layout.SelectedIndex == 1; _orientation.IsEnabled = tiled; _overlap.IsEnabled = tiled;
        try
        {
            var overlap = 10d;
            if (tiled && (!double.TryParse(_overlap.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out overlap) || !double.IsFinite(overlap)))
                throw new ArgumentException("Enter an overlap from 0 to 30 mm.");
            Options = new PdfExportOptions { Layout = tiled ? PdfExportLayout.A4Tiles : PdfExportLayout.OriginalSize, Orientation = (PdfTileOrientation)Math.Max(0, _orientation.SelectedIndex), OverlapMillimeters = overlap };
            _tiles = PdfExportPagination.Plan(_pages, Options); _accept.IsEnabled = _tiles.Count > 0; _error.Text = "";
            _summary.Text = $"{_pages.Count} source {(_pages.Count == 1 ? "page" : "pages")} → {_tiles.Count} PDF {(_tiles.Count == 1 ? "page" : "pages")}" + (tiled ? " · A4 at 100% scale" : " · Original dimensions preserved");
            UpdateGrid();
        }
        catch (ArgumentException ex) { _error.Text = ex.Message; _accept.IsEnabled = false; _summary.Text = "Check the layout settings."; _preview.Tiles = []; _preview.InvalidateVisual(); }
    }

    private void UpdateGrid()
    {
        _preview.Tiles = _tiles.Where(tile => tile.SourcePageIndex == _page.SelectedIndex).ToArray();
        _preview.PageSize = _page.SelectedIndex >= 0 ? new Size(_pages[_page.SelectedIndex].Width, _pages[_page.SelectedIndex].Height) : new Size(1, 1);
        _preview.InvalidateVisual();
    }

    private async Task UpdatePreviewAsync()
    {
        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(); _previewCancellation = cancellation;
        _preview.Bitmap = null; UpdateGrid();
        if (_page.SelectedIndex < 0) return;
        try
        {
            var bitmap = await _renderer.RenderAsync(_pages[_page.SelectedIndex], 700, cancellation.Token);
            if (!_closed && !cancellation.IsCancellationRequested) { _preview.Bitmap = bitmap; _preview.InvalidateVisual(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed && !cancellation.IsCancellationRequested) _error.Text = "Page image unavailable. Sheet boundaries and count are still shown."; }
        finally { if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null; cancellation.Dispose(); }
    }

    private sealed class SheetPreview : FrameworkElement
    {
        public BitmapSource? Bitmap { get; set; }
        public Size PageSize { get; set; } = new(1, 1);
        public IReadOnlyList<PdfExportTile> Tiles { get; set; } = [];
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing); drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(239, 241, 245)), null, new Rect(RenderSize));
            if (ActualWidth <= 20 || ActualHeight <= 20) return;
            var width = Math.Max(PageSize.Width, Tiles.Count > 0 ? Tiles.Max(tile => tile.Bounds.Right) : 0);
            var height = Math.Max(PageSize.Height, Tiles.Count > 0 ? Tiles.Max(tile => tile.Bounds.Bottom) : 0);
            var scale = Math.Min((ActualWidth - 20) / width, (ActualHeight - 20) / height);
            var offset = new Point((ActualWidth - width * scale) / 2, (ActualHeight - height * scale) / 2);
            Rect Display(Rect bounds) => new(offset.X + bounds.X * scale, offset.Y + bounds.Y * scale, bounds.Width * scale, bounds.Height * scale);
            var source = Display(new Rect(new Point(), PageSize)); drawing.DrawRectangle(Brushes.White, null, source);
            if (Bitmap is not null) drawing.DrawImage(Bitmap, source);
            for (var index = 0; index < Tiles.Count; index++)
            {
                var tile = Display(Tiles[index].Bounds); drawing.DrawRectangle(null, new Pen(Brushes.RoyalBlue, 1), tile);
                var label = new FormattedText((index + 1).ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.DarkBlue, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var position = new Point(tile.Left + 2, tile.Top + 2); drawing.DrawRectangle(Brushes.White, null, new Rect(position, new Size(label.Width + 4, label.Height))); drawing.DrawText(label, new Point(position.X + 2, position.Y));
            }
        }
    }
}
