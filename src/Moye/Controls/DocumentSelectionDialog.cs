using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Models;
using Moye.Services;

namespace Moye.Controls;

public enum ImportDestinationKind { Section, NewSection, NewNotebook }
public sealed record ImportDestination(ImportDestinationKind Kind, string Title, string? SectionId = null);

/// <summary>Page choices are made on a private snapshot; closing the dialog changes nothing.</summary>
public sealed class DocumentSelectionDialog : Window
{
    private readonly List<PageChoice> _choices;
    private readonly ListBox _list = new();
    private readonly TextBlock _count = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, MinHeight = 24 };
    private readonly TextBlock _previewLabel = new() { Text = "Select a page to preview", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
    private readonly ComboBox _destination = new() { DisplayMemberPath = nameof(ImportDestination.Title), MinHeight = 44 };
    private ComboBox? _scope;
    private readonly ComboBox _position = new() { ItemsSource = new[] { "At the end of the section", "After the current page" }, SelectedIndex = 0, MinHeight = 44 };
    private readonly TextBox _newTitle = new() { Text = "Imported document", MaxLength = 160, MinHeight = 44 };
    private readonly Button _accept;
    private readonly PagePreviewRenderer _renderer;
    private readonly SemaphoreSlim _thumbnailGate = new(2);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Queue<PageChoice> _thumbnailCache = new();
    private CancellationTokenSource? _previewCancellation;
    private bool _updating, _closed;
    private readonly string? _currentSectionId;
    public IReadOnlyList<NotePage> SelectedPages => _choices.Where(choice => choice.Included).Select(choice => choice.Page).ToArray();
    public ImportDestination? Destination => _destination.SelectedItem as ImportDestination;
    public string NewTitle => _newTitle.Text.Trim();
    public bool InsertAfterCurrent => _position.IsEnabled && _position.SelectedIndex == 1;

    public DocumentSelectionDialog(Window owner, string title, IReadOnlyList<NotePage> pages, PagePreviewRenderer renderer,
        NotebookDocument? notebook, string? currentPageId, string? currentSectionId, bool importing, string? suggestedTitle = null)
    {
        Owner = owner; Title = title; Width = 900; Height = 740; MinWidth = 660; MinHeight = 500;
        MaxWidth = Math.Max(660, SystemParameters.WorkArea.Width - 24); MaxHeight = Math.Max(500, SystemParameters.WorkArea.Height - 24);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = Brushes.White;
        _renderer = renderer; _currentSectionId = currentSectionId;
        var sections = notebook?.Sections.ToDictionary(section => section.Id, section => section.Title) ?? [];
        _choices = pages.Select((page, index) => new PageChoice(page.Snapshot(), index + 1,
            $"Page {index + 1}" + (string.IsNullOrWhiteSpace(page.Title) ? "" : $" · {page.Title}") +
            (!importing && sections.TryGetValue(page.SectionId, out var section) ? $" · {section}" : ""))
            { Included = importing || page.SectionId == currentSectionId }).ToList();
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = importing ? "Choose pages before adding them. Your document stays unchanged." : "Choose pages in their notebook order. Added text is outlined in the PDF.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10) });
        var selection = new WrapPanel();
        if (!importing)
        {
            var scope = new ComboBox { ItemsSource = new[] { "Current section", "Current page", "Whole notebook", "Choose pages below" }, SelectedIndex = 0, MinWidth = 175, MinHeight = 44, Margin = new Thickness(0, 0, 8, 0) };
            _scope = scope;
            AutomationProperties.SetName(scope, "Export scope");
            scope.SelectionChanged += (_, _) =>
            {
                if (scope.SelectedIndex == 3) return;
                ApplySelection(choice => scope.SelectedIndex switch { 0 => choice.Page.SectionId == currentSectionId, 1 => choice.Page.Id == currentPageId, _ => true });
            };
            selection.Children.Add(scope);
        }
        AddButton(selection, "Select all", () => ApplySelection(_ => true, true));
        AddButton(selection, "Clear", () => ApplySelection(_ => false, true));
        var range = new TextBox { Width = 155, MinHeight = 44, Margin = new Thickness(8, 0, 8, 0), ToolTip = "Page numbers in the list, for example 1–5, 8" };
        AutomationProperties.SetName(range, "Page range"); selection.Children.Add(range);
        AddButton(selection, "Apply range", () =>
        {
            try { var indexes = DocumentWorkflows.ParsePageRange(range.Text, _choices.Count).ToHashSet(); ApplySelection(choice => indexes.Contains(choice.Number - 1), true); }
            catch (FormatException ex) { _message.Text = ex.Message; }
            catch (ArgumentException ex) { _message.Text = ex.Message; }
        });
        header.Children.Add(selection); header.Children.Add(_count);
        root.Children.Add(header);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1); root.Children.Add(body);
        _list.ItemsSource = _choices; _list.Margin = new Thickness(0, 0, 16, 0);
        VirtualizingPanel.SetIsVirtualizing(_list, true); VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_list, true);
        var row = new FrameworkElementFactory(typeof(StackPanel)); row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var checkbox = new FrameworkElementFactory(typeof(CheckBox)); checkbox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(PageChoice.Included)) { Mode = BindingMode.TwoWay });
        checkbox.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(PageChoice.Label))); checkbox.SetValue(MinWidthProperty, 44d); checkbox.SetValue(MinHeightProperty, 96d); row.AppendChild(checkbox);
        var thumbnail = new FrameworkElementFactory(typeof(Image)); thumbnail.SetValue(WidthProperty, 64d); thumbnail.SetValue(HeightProperty, 90d); thumbnail.SetValue(MarginProperty, new Thickness(0, 4, 10, 4)); thumbnail.SetBinding(Image.SourceProperty, new Binding(nameof(PageChoice.Thumbnail)));
        thumbnail.AddHandler(LoadedEvent, new RoutedEventHandler(ThumbnailLoaded)); row.AppendChild(thumbnail);
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding(nameof(PageChoice.Label))); label.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); label.SetValue(MaxWidthProperty, 150d); label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); row.AppendChild(label);
        _list.ItemTemplate = new DataTemplate { VisualTree = row };
        _list.SelectionChanged += async (_, _) => { if (_list.SelectedItem is PageChoice choice) await PreviewAsync(choice); };
        body.Children.Add(_list);
        var previewPanel = new DockPanel(); Grid.SetColumn(previewPanel, 1); body.Children.Add(previewPanel);
        DockPanel.SetDock(_previewLabel, Dock.Top); previewPanel.Children.Add(_previewLabel);
        previewPanel.Children.Add(new ScrollViewer { Content = _preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(242, 241, 237)) });
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        if (importing)
        {
            var destinations = notebook?.Sections.Select(section => new ImportDestination(ImportDestinationKind.Section, section.Title, section.Id)).ToList() ?? [];
            if (notebook is not null) destinations.Add(new(ImportDestinationKind.NewSection, "New section…"));
            destinations.Add(new(ImportDestinationKind.NewNotebook, "New notebook…"));
            _destination.ItemsSource = destinations;
            _destination.SelectedItem = destinations.FirstOrDefault(item => item.SectionId == currentSectionId) ?? destinations.Last();
            _newTitle.Text = suggestedTitle ?? "Imported document";
            var placement = new Grid(); placement.ColumnDefinitions.Add(new()); placement.ColumnDefinitions.Add(new());
            _destination.Margin = new Thickness(0, 0, 8, 0); placement.Children.Add(_destination); Grid.SetColumn(_position, 1); placement.Children.Add(_position);
            footer.Children.Add(new TextBlock { Text = "Destination", Margin = new Thickness(0, 0, 0, 4) }); footer.Children.Add(placement);
            _newTitle.Margin = new Thickness(0, 8, 0, 0); footer.Children.Add(_newTitle);
            AutomationProperties.SetName(_destination, "Import destination"); AutomationProperties.SetName(_position, "Page placement"); AutomationProperties.SetName(_newTitle, "New notebook or section name");
            _destination.SelectionChanged += (_, _) => UpdateDestination(); UpdateDestination();
        }
        footer.Children.Add(_message);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinHeight = 44, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), Style = TryFindResource("SecondaryButton") as Style });
        _accept = new Button { Content = importing ? "Import selected pages" : "Continue to export", MinHeight = 44, IsDefault = true, Margin = new Thickness(8, 0, 0, 0), Style = TryFindResource("PrimaryButton") as Style };
        _accept.Click += (_, _) =>
        {
            if (SelectedPages.Count == 0) { _message.Text = "Select at least one page."; return; }
            if (importing && Destination?.Kind != ImportDestinationKind.Section && string.IsNullOrWhiteSpace(NewTitle)) { _message.Text = "Enter a name for the new notebook or section."; return; }
            DialogResult = true;
        };
        actions.Children.Add(_accept); footer.Children.Add(actions); Content = root;
        foreach (var choice in _choices) choice.PropertyChanged += (_, e) =>
        {
            if (!_updating && e.PropertyName == nameof(PageChoice.Included)) { if (_scope is not null) _scope.SelectedIndex = 3; _message.Text = ""; UpdateCount(); }
        };
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _previewCancellation?.Cancel(); };
        UpdateCount(); if (_choices.Count > 0) _list.SelectedIndex = _choices.FindIndex(choice => choice.Included) is var selected && selected >= 0 ? selected : 0;
    }

    private void UpdateDestination()
    {
        _newTitle.Visibility = Destination?.Kind == ImportDestinationKind.Section ? Visibility.Collapsed : Visibility.Visible;
        _position.IsEnabled = Destination?.Kind == ImportDestinationKind.Section && Destination.SectionId == _currentSectionId;
    }
    private void ApplySelection(Func<PageChoice, bool> predicate, bool custom = false)
    {
        _updating = true; try { foreach (var choice in _choices) choice.Included = predicate(choice); } finally { _updating = false; }
        if (custom && _scope is not null) _scope.SelectedIndex = 3;
        _message.Text = ""; UpdateCount();
    }
    private void UpdateCount() { var count = _choices.Count(choice => choice.Included); _count.Text = $"{count} of {_choices.Count} pages selected"; if (_accept is not null) _accept.IsEnabled = count > 0; }
    private void AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, MinHeight = 44, Margin = new Thickness(0, 0, 6, 0), Style = TryFindResource("SecondaryButton") as Style };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    private async void ThumbnailLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PageChoice choice } element || choice.Thumbnail is not null || choice.Loading || _closed) return;
        choice.Loading = true;
        try
        {
            await _thumbnailGate.WaitAsync(_lifetime.Token);
            try
            {
                if (!element.IsVisible || !ReferenceEquals(element.DataContext, choice)) return;
                choice.Thumbnail = await _renderer.RenderAsync(choice.Page, 64, _lifetime.Token); _thumbnailCache.Enqueue(choice);
                while (_thumbnailCache.Count > 128) _thumbnailCache.Dequeue().Thumbnail = null;
            }
            finally { _thumbnailGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* The full preview reports a readable error when this page is selected. */ }
        finally { choice.Loading = false; }
    }
    private async Task PreviewAsync(PageChoice choice)
    {
        _previewCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _previewCancellation = cancellation;
        _previewLabel.Text = $"{choice.Label} · Loading preview…";
        try
        {
            var image = await _renderer.RenderAsync(choice.Page, 700, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested) return;
            _preview.Source = image; _previewLabel.Text = choice.Label;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed && !cancellation.IsCancellationRequested) { _preview.Source = null; _previewLabel.Text = "Preview unavailable: " + ex.Message; } }
        finally { if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null; cancellation.Dispose(); }
    }
    private sealed class PageChoice(NotePage page, int number, string label) : INotifyPropertyChanged
    {
        private bool _included; private BitmapSource? _thumbnail;
        public NotePage Page { get; } = page; public int Number { get; } = number; public string Label { get; } = label;
        public bool Loading { get; set; }
        public bool Included { get => _included; set { if (_included == value) return; _included = value; Changed(); } }
        public BitmapSource? Thumbnail { get => _thumbnail; set { _thumbnail = value; Changed(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    }
}
