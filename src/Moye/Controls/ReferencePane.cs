using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Moye.Models;
using Moye.Services;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

/// <summary>Independent navigation over immutable snapshots; reference gestures never edit the working notebook.</summary>
public sealed class ReferencePane : UserControl, IDisposable
{
    private readonly INotebookRepository _repository;
    private readonly Func<NotebookDocument?> _current;
    private readonly PagePreviewRenderer _renderer;
    private readonly ComboBox _notebooks = new() { DisplayMemberPath = nameof(NotebookSummary.Title), MinHeight = 44 };
    private readonly ComboBox _pages = new() { MinHeight = 44, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Image _image = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.Both };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private NotebookDocument? _document;
    private CancellationTokenSource? _renderCancellation;
    private long _loadRevision;
    private bool _disposed, _changing, _fit = true;
    private double _zoom = .6;
    public event EventHandler? CloseRequested;
    public event EventHandler? ReturnToEditor;
    public ReferencePane(INotebookRepository repository, IPdfService pdf, Func<NotebookDocument?> current)
    {
        _repository = repository; _current = current; _renderer = new(pdf, repository.GetAssetAsync);
        MinWidth = 260;
        var root = new DockPanel { Margin = new Thickness(12), Background = Brushes.White };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var heading = new DockPanel(); header.Children.Add(heading);
        var close = Button("Controls.Common.Close", () => CloseRequested?.Invoke(this, EventArgs.Empty)); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        var title = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        L.Bind(title, TextBlock.TextProperty, "Controls.Reference.Title");
        heading.Children.Add(title);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) };
        L.Bind(hint, TextBlock.TextProperty, "Controls.Reference.Hint");
        header.Children.Add(hint);
        L.Bind(_notebooks, AutomationProperties.NameProperty, "Controls.Reference.Notebook");
        L.Bind(_pages, AutomationProperties.NameProperty, "Controls.Reference.Page");
        var pageLabel = new FrameworkElementFactory(typeof(TextBlock));
        pageLabel.SetBinding(TextBlock.TextProperty, L.FormatBinding("Controls.Reference.PageChoice",
            new Binding(nameof(ReferencePageChoice.Number)) { Mode = BindingMode.OneTime },
            new Binding(nameof(ReferencePageChoice.TitleSuffix)) { Mode = BindingMode.OneTime },
            new Binding(nameof(ReferencePageChoice.Section)) { Mode = BindingMode.OneTime }));
        _pages.ItemTemplate = new DataTemplate { VisualTree = pageLabel };
        header.Children.Add(_notebooks); header.Children.Add(_pages);
        var tools = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; header.Children.Add(tools);
        tools.Children.Add(Button("Controls.Reference.Previous", () => { if (_pages.SelectedIndex > 0) _pages.SelectedIndex--; }));
        tools.Children.Add(Button("Controls.Reference.Next", () => { if (_pages.SelectedIndex + 1 < _pages.Items.Count) _pages.SelectedIndex++; }));
        tools.Children.Add(Button("Controls.Reference.ZoomOut", () => Zoom(.8), "Controls.Reference.ZoomOutName")); tools.Children.Add(Button("Controls.Reference.ZoomIn", () => Zoom(1.25), "Controls.Reference.ZoomInName"));
        tools.Children.Add(Button("Controls.Reference.Fit", () => { _fit = true; _ = RenderAsync(); }, "Controls.Reference.FitName"));
        tools.Children.Add(Button("Controls.Reference.Refresh", () => { _ = LoadNotebookAsync(); }));
        tools.Children.Add(Button("Controls.Reference.Back", () => ReturnToEditor?.Invoke(this, EventArgs.Empty)));
        header.Children.Add(_status);
        _scroll.Background = new SolidColorBrush(Color.FromRgb(238, 238, 232)); _scroll.Content = _image; root.Children.Add(_scroll); Content = root;
        _notebooks.SelectionChanged += async (_, _) => { if (!_changing) await LoadNotebookAsync(); };
        _pages.SelectionChanged += async (_, _) => { if (!_changing) { _scroll.ScrollToTop(); await RenderAsync(); } };
        _scroll.SizeChanged += async (_, _) => { if (_fit && !_changing) await RenderAsync(); };
    }
    public async Task InitializeAsync()
    {
        try
        {
            var summaries = (await _repository.ListAsync()).ToList();
            var current = _current();
            if (current is not null && summaries.All(summary => summary.Id != current.Id)) summaries.Insert(0, new() { Id = current.Id, Title = current.Title, PageCount = current.Pages.Count });
            if (_disposed) return;
            _changing = true;
            try { _notebooks.ItemsSource = summaries; _notebooks.SelectedItem = summaries.FirstOrDefault(item => item.Id == current?.Id) ?? summaries.FirstOrDefault(); }
            finally { _changing = false; }
            await LoadNotebookAsync();
        }
        catch (Exception ex) { if (!_disposed) L.Bind(_status, TextBlock.TextProperty, "Controls.Reference.LoadReferencesError", ex.Message); }
    }
    private async Task LoadNotebookAsync()
    {
        var revision = ++_loadRevision;
        _renderCancellation?.Cancel();
        if (_notebooks.SelectedItem is not NotebookSummary summary) { L.Bind(_status, TextBlock.TextProperty, "Controls.Reference.CreateNotebook"); return; }
        var selectedId = _document?.Id == summary.Id && _pages.SelectedIndex >= 0 && _pages.SelectedIndex < _document.Pages.Count ? _document.Pages[_pages.SelectedIndex].Id : null;
        L.Bind(_status, TextBlock.TextProperty, "Controls.Reference.Loading");
        try
        {
            var current = _current();
            var document = current?.Id == summary.Id ? current.Snapshot() : await _repository.LoadAsync(summary.Id);
            if (_disposed || revision != _loadRevision) return;
            _document = document?.Snapshot();
            _changing = true;
            try
            {
                var sections = _document?.Sections.ToDictionary(section => section.Id, section => section.Title) ?? [];
                _pages.ItemsSource = _document?.Pages.Select((page, index) => new ReferencePageChoice(index + 1,
                    string.IsNullOrWhiteSpace(page.Title) ? "" : $" · {page.Title}", sections.GetValueOrDefault(page.SectionId, "General"))).ToArray() ?? [];
                var index = _document?.Pages.FindIndex(page => page.Id == selectedId) ?? -1;
                _pages.SelectedIndex = _pages.Items.Count > 0 ? Math.Max(0, index) : -1;
            }
            finally { _changing = false; }
            await RenderAsync();
        }
        catch (Exception ex) { if (!_disposed && revision == _loadRevision) L.Bind(_status, TextBlock.TextProperty, "Controls.Reference.LoadNotebookError", ex.Message); }
    }
    private void Zoom(double factor) { _fit = false; _zoom = Math.Clamp(_zoom * factor, .2, 2.5); _ = RenderAsync(); }
    private async Task RenderAsync()
    {
        if (_disposed) return;
        _renderCancellation?.Cancel();
        if (_document is null || _pages.SelectedIndex < 0 || _pages.SelectedIndex >= _document.Pages.Count) { _image.Source = null; L.Bind(_status, TextBlock.TextProperty, "Controls.Reference.Empty"); return; }
        var page = _document.Pages[_pages.SelectedIndex];
        if (_fit) _zoom = Math.Clamp(Math.Max(220, _scroll.ActualWidth - 24) / page.Width, .2, 2.5);
        var cancellation = new CancellationTokenSource(); _renderCancellation = cancellation;
        L.Bind(_status, TextBlock.TextProperty, "Controls.Reference.LoadingPage");
        try
        {
            var bitmap = await _renderer.RenderAsync(page, page.Width * _zoom, cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested) return;
            _image.Source = bitmap; _image.Width = page.Width * _zoom; _image.Height = page.Height * _zoom;
            L.Bind(_status, TextBlock.TextProperty, "Controls.Reference.PageStatus", _pages.SelectedIndex + 1, _document.Pages.Count, _zoom);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed && !cancellation.IsCancellationRequested) { _image.Source = null; L.Bind(_status, TextBlock.TextProperty, "Controls.Reference.DisplayError", ex.Message); } }
        finally { if (ReferenceEquals(_renderCancellation, cancellation)) _renderCancellation = null; cancellation.Dispose(); }
    }
    private Button Button(string labelKey, Action action, string? nameKey = null)
    {
        var button = new Button { MinHeight = 44, MinWidth = 44, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 4, 8, 4), Style = TryFindResource("SecondaryButton") as Style };
        L.Bind(button, ContentControl.ContentProperty, labelKey);
        L.Bind(button, AutomationProperties.NameProperty, nameKey ?? labelKey); button.Click += (_, _) => action(); return button;
    }
    private sealed record ReferencePageChoice(int Number, string TitleSuffix, string Section)
    {
        public override string ToString() => L.Text("Controls.Reference.PageChoice", Number, TitleSuffix, Section);
    }

    public void Dispose() { _disposed = true; _loadRevision++; _renderCancellation?.Cancel(); }
}
