using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Moye.Models;
using Moye.Services;

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
        var close = Button("Close", () => CloseRequested?.Invoke(this, EventArgs.Empty)); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        heading.Children.Add(new TextBlock { Text = "Reference", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(new TextBlock { Text = "Read-only copy · your writing stays in place", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) });
        AutomationProperties.SetName(_notebooks, "Reference notebook"); AutomationProperties.SetName(_pages, "Reference page");
        header.Children.Add(_notebooks); header.Children.Add(_pages);
        var tools = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; header.Children.Add(tools);
        tools.Children.Add(Button("Previous", () => { if (_pages.SelectedIndex > 0) _pages.SelectedIndex--; }));
        tools.Children.Add(Button("Next", () => { if (_pages.SelectedIndex + 1 < _pages.Items.Count) _pages.SelectedIndex++; }));
        tools.Children.Add(Button("−", () => Zoom(.8), "Zoom reference out")); tools.Children.Add(Button("+", () => Zoom(1.25), "Zoom reference in"));
        tools.Children.Add(Button("Fit", () => { _fit = true; _ = RenderAsync(); }, "Fit reference to pane width"));
        tools.Children.Add(Button("Refresh", () => { _ = LoadNotebookAsync(); }));
        tools.Children.Add(Button("Back to writing", () => ReturnToEditor?.Invoke(this, EventArgs.Empty)));
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
        catch (Exception ex) { if (!_disposed) _status.Text = "Unable to load references: " + ex.Message; }
    }
    private async Task LoadNotebookAsync()
    {
        var revision = ++_loadRevision;
        _renderCancellation?.Cancel();
        if (_notebooks.SelectedItem is not NotebookSummary summary) { _status.Text = "Create a notebook to use a reference."; return; }
        var selectedId = _document?.Id == summary.Id && _pages.SelectedIndex >= 0 && _pages.SelectedIndex < _document.Pages.Count ? _document.Pages[_pages.SelectedIndex].Id : null;
        _status.Text = "Loading reference…";
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
                _pages.ItemsSource = _document?.Pages.Select((page, index) => $"Page {index + 1}" +
                    (string.IsNullOrWhiteSpace(page.Title) ? "" : $" · {page.Title}") + $" · {sections.GetValueOrDefault(page.SectionId, "General")}").ToArray() ?? [];
                var index = _document?.Pages.FindIndex(page => page.Id == selectedId) ?? -1;
                _pages.SelectedIndex = _pages.Items.Count > 0 ? Math.Max(0, index) : -1;
            }
            finally { _changing = false; }
            await RenderAsync();
        }
        catch (Exception ex) { if (!_disposed && revision == _loadRevision) _status.Text = "Unable to load this notebook: " + ex.Message; }
    }
    private void Zoom(double factor) { _fit = false; _zoom = Math.Clamp(_zoom * factor, .2, 2.5); _ = RenderAsync(); }
    private async Task RenderAsync()
    {
        if (_disposed) return;
        _renderCancellation?.Cancel();
        if (_document is null || _pages.SelectedIndex < 0 || _pages.SelectedIndex >= _document.Pages.Count) { _image.Source = null; _status.Text = "No pages to display."; return; }
        var page = _document.Pages[_pages.SelectedIndex];
        if (_fit) _zoom = Math.Clamp(Math.Max(220, _scroll.ActualWidth - 24) / page.Width, .2, 2.5);
        var cancellation = new CancellationTokenSource(); _renderCancellation = cancellation;
        _status.Text = "Loading page…";
        try
        {
            var bitmap = await _renderer.RenderAsync(page, page.Width * _zoom, cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested) return;
            _image.Source = bitmap; _image.Width = page.Width * _zoom; _image.Height = page.Height * _zoom;
            _status.Text = $"Page {_pages.SelectedIndex + 1} of {_document.Pages.Count} · {_zoom:P0} · Read only";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed && !cancellation.IsCancellationRequested) { _image.Source = null; _status.Text = "Unable to display this page: " + ex.Message; } }
        finally { if (ReferenceEquals(_renderCancellation, cancellation)) _renderCancellation = null; cancellation.Dispose(); }
    }
    private Button Button(string label, Action action, string? name = null)
    {
        var button = new Button { Content = label, MinHeight = 44, MinWidth = 44, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 4, 8, 4), Style = TryFindResource("SecondaryButton") as Style };
        AutomationProperties.SetName(button, name ?? label); button.Click += (_, _) => action(); return button;
    }
    public void Dispose() { _disposed = true; _loadRevision++; _renderCancellation?.Cancel(); }
}
