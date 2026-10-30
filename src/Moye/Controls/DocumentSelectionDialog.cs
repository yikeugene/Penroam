using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Models;
using Moye.Services;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

public enum ImportDestinationKind { Section, NewSection, NewNotebook }
public sealed record ImportDestination(ImportDestinationKind Kind, string Title, string? SectionId = null);

/// <summary>Page choices are made on a private snapshot; closing the dialog changes nothing.</summary>
public sealed class DocumentSelectionDialog : Window
{
    private readonly List<PageChoice> _choices;
    private readonly ListCollectionView _view;
    private readonly bool _importing;
    private readonly ListBox _list = new();
    private readonly TextBlock _count = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _empty = new() { Text = L.Text("Controls.DocumentSelection.NoMatches"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, MinHeight = 24 };
    private readonly TextBlock _previewLabel = new() { Text = L.Text("Controls.DocumentSelection.PreviewHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
    private readonly ComboBox _destination = new() { DisplayMemberPath = nameof(ImportDestination.Title), MinHeight = 44 };
    private ComboBox? _scope;
    private readonly TextBox _search = new() { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _range = new() { Width = 130, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _bookmarked = new() { Content = L.Text("Controls.DocumentSelection.BookmarkedOnly"), MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
    private Button? _selectAll, _clear;
    private HashSet<int>? _rangeIndexes;
    private string _appliedRange = "", _filterError = "";
    private bool _rangeValid = true;
    private readonly string? _currentPageId;
    private readonly ComboBox _position = new() { ItemsSource = new[] { L.Text("Controls.DocumentSelection.PositionEnd"), L.Text("Controls.DocumentSelection.PositionAfter") }, SelectedIndex = 0, MinHeight = 44 };
    private readonly TextBox _newTitle = new() { Text = "Imported document", MaxLength = 160, MinHeight = 44 };
    private readonly Button _accept;
    private readonly PagePreviewRenderer _renderer;
    private readonly SemaphoreSlim _thumbnailGate = new(2);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Queue<PageChoice> _thumbnailCache = new();
    private CancellationTokenSource? _previewCancellation;
    private bool _updating, _closed;
    private readonly string? _currentSectionId;
    public IReadOnlyList<NotePage> SelectedPages => _view.Cast<PageChoice>().Where(choice => choice.Included).Select(choice => choice.Page).ToArray();
    public ImportDestination? Destination => _destination.SelectedItem as ImportDestination;
    public string NewTitle => _newTitle.Text.Trim();
    public bool InsertAfterCurrent => _position.IsEnabled && _position.SelectedIndex == 1;

    public DocumentSelectionDialog(Window owner, string title, IReadOnlyList<NotePage> pages, PagePreviewRenderer renderer,
        NotebookDocument? notebook, string? currentPageId, string? currentSectionId, bool importing, string? suggestedTitle = null)
    {
        Owner = owner; Title = title; Width = 900; Height = 740; MinWidth = 660; MinHeight = importing ? 500 : 560;
        MaxWidth = Math.Max(660, SystemParameters.WorkArea.Width - 24); MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 24);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = Brushes.White;
        _renderer = renderer; _currentSectionId = currentSectionId; _currentPageId = currentPageId; _importing = importing;
        var sections = notebook?.Sections.ToDictionary(section => section.Id, section => section.Title) ?? [];
        _choices = pages.Select((page, index) => new PageChoice(page.Snapshot(), index + 1,
            L.Text("Controls.Common.PageNumber", index + 1) + (string.IsNullOrWhiteSpace(page.Title) ? "" : $" · {page.Title}") +
            (!importing && sections.TryGetValue(page.SectionId, out var section) ? $" · {section}" : ""))
            { Included = true }).ToList();
        _view = new ListCollectionView(_choices);
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = importing ? L.Text("Controls.DocumentSelection.ImportHint") : L.Text("Controls.DocumentSelection.ExportHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10) });
        if (!importing)
        {
            AddExportFilters(header, notebook);
        }
        var selection = new WrapPanel();
        _selectAll = AddButton(selection, L.Text(importing ? "Controls.Common.SelectAll" : "Controls.DocumentSelection.SelectAllFiltered"), () => ApplySelection(_ => true), "SelectAllDocumentPages");
        _clear = AddButton(selection, L.Text(importing ? "Controls.DocumentSelection.Clear" : "Controls.DocumentSelection.ClearFiltered"), () => ApplySelection(_ => false), "ClearDocumentPages");
        if (importing)
        {
            var range = new TextBox { Width = 155, MinHeight = 44, Margin = new Thickness(8, 0, 8, 0), ToolTip = L.Text("Controls.DocumentSelection.RangeHelp") };
            Identify(range, "ImportRangeSelection", L.Text("Controls.DocumentSelection.Range")); selection.Children.Add(range);
            AddButton(selection, L.Text("Controls.DocumentSelection.ApplyRange"), () =>
            {
                try { var indexes = DocumentWorkflows.ParsePageRange(range.Text, _choices.Count).ToHashSet(); ApplySelection(choice => indexes.Contains(choice.Number - 1)); }
                catch (FormatException ex) { _message.Text = ex.Message; }
                catch (ArgumentException ex) { _message.Text = ex.Message; }
            }, "ApplyImportRange");
        }
        else
        {
            Identify(_bookmarked, "ExportBookmarkedFilter", L.Text("Controls.DocumentSelection.BookmarkedOnly"));
            _bookmarked.Margin = new Thickness(8, 0, 0, 0); selection.Children.Add(_bookmarked);
            _bookmarked.Checked += (_, _) => RefreshFilter(); _bookmarked.Unchecked += (_, _) => RefreshFilter();
        }
        header.Children.Add(selection); header.Children.Add(_count);
        root.Children.Add(header);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1); root.Children.Add(body);
        _list.ItemsSource = _view; _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Identify(_list, "DocumentPageList", L.Text("Controls.DocumentSelection.PageList"));
        VirtualizingPanel.SetIsVirtualizing(_list, true); VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_list, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var checkbox = new FrameworkElementFactory(typeof(CheckBox)); checkbox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(PageChoice.Included)) { Mode = BindingMode.TwoWay });
        checkbox.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(PageChoice.Label))); checkbox.SetValue(MinWidthProperty, 44d); checkbox.SetValue(MinHeightProperty, 96d); row.AppendChild(checkbox);
        var thumbnail = new FrameworkElementFactory(typeof(Image)); thumbnail.SetValue(WidthProperty, 64d); thumbnail.SetValue(HeightProperty, 90d); thumbnail.SetValue(MarginProperty, new Thickness(0, 4, 10, 4)); thumbnail.SetBinding(Image.SourceProperty, new Binding(nameof(PageChoice.Thumbnail)));
        thumbnail.AddHandler(LoadedEvent, new RoutedEventHandler(ThumbnailLoaded)); row.AppendChild(thumbnail);
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding(nameof(PageChoice.Label))); label.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); row.AppendChild(label);
        _list.ItemTemplate = new DataTemplate { VisualTree = row };
        _list.SelectionChanged += async (_, _) =>
        {
            _previewCancellation?.Cancel(); _preview.Source = null; _previewLabel.Text = L.Text("Controls.DocumentSelection.PreviewHint");
            if (_list.SelectedItem is PageChoice choice) await PreviewAsync(choice);
        };
        var listPanel = new Grid { Margin = new Thickness(0, 0, 16, 0) }; listPanel.Children.Add(_list); listPanel.Children.Add(_empty); body.Children.Add(listPanel);
        var previewPanel = new DockPanel(); Grid.SetColumn(previewPanel, 1); body.Children.Add(previewPanel);
        DockPanel.SetDock(_previewLabel, Dock.Top); previewPanel.Children.Add(_previewLabel);
        previewPanel.Children.Add(new ScrollViewer { Content = _preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(242, 241, 237)) });
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        if (importing)
        {
            var destinations = notebook?.Sections.Select(section => new ImportDestination(ImportDestinationKind.Section, section.Title, section.Id)).ToList() ?? [];
            if (notebook is not null) destinations.Add(new(ImportDestinationKind.NewSection, L.Text("Controls.DocumentSelection.NewSection")));
            destinations.Add(new(ImportDestinationKind.NewNotebook, L.Text("Controls.DocumentSelection.NewNotebook")));
            _destination.ItemsSource = destinations;
            _destination.SelectedItem = destinations.FirstOrDefault(item => item.SectionId == currentSectionId) ?? destinations.Last();
            _newTitle.Text = suggestedTitle ?? "Imported document";
            var placement = new Grid(); placement.ColumnDefinitions.Add(new()); placement.ColumnDefinitions.Add(new());
            _destination.Margin = new Thickness(0, 0, 8, 0); placement.Children.Add(_destination); Grid.SetColumn(_position, 1); placement.Children.Add(_position);
            footer.Children.Add(new TextBlock { Text = L.Text("Controls.DocumentSelection.Destination"), Margin = new Thickness(0, 0, 0, 4) }); footer.Children.Add(placement);
            _newTitle.Margin = new Thickness(0, 8, 0, 0); footer.Children.Add(_newTitle);
            AutomationProperties.SetName(_destination, L.Text("Controls.DocumentSelection.ImportDestination")); AutomationProperties.SetName(_position, L.Text("Controls.DocumentSelection.Placement")); AutomationProperties.SetName(_newTitle, L.Text("Controls.DocumentSelection.NewName"));
            _destination.SelectionChanged += (_, _) => UpdateDestination(); UpdateDestination();
        }
        footer.Children.Add(_message);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(new Button { Content = L.Text("Controls.Common.Cancel"), IsCancel = true, MinHeight = 44, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), Style = TryFindResource("SecondaryButton") as Style });
        _accept = new Button { Content = importing ? L.Text("Controls.DocumentSelection.Import") : L.Text("Controls.DocumentSelection.Export"), MinHeight = 44, IsDefault = true, Margin = new Thickness(8, 0, 0, 0), Style = TryFindResource("PrimaryButton") as Style };
        Identify(_accept, "AcceptDocumentPages", _accept.Content.ToString()!);
        _accept.Click += (_, _) =>
        {
            if (!_accept.IsEnabled) return;
            if (SelectedPages.Count == 0) { _message.Text = L.Text("Controls.DocumentSelection.SelectRequired"); return; }
            if (importing && Destination?.Kind != ImportDestinationKind.Section && string.IsNullOrWhiteSpace(NewTitle)) { _message.Text = L.Text("Controls.DocumentSelection.NameRequired"); return; }
            DialogResult = true;
        };
        actions.Children.Add(_accept); footer.Children.Add(actions); Content = root;
        foreach (var choice in _choices) choice.PropertyChanged += (_, e) =>
        {
            if (!_updating && e.PropertyName == nameof(PageChoice.Included)) { UpdateCount(); }
        };
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _previewCancellation?.Cancel(); };
        if (importing) { UpdateCount(); if (_choices.Count > 0) _list.SelectedIndex = 0; }
        else RefreshFilter();
    }

    private void AddExportFilters(Panel header, NotebookDocument? notebook)
    {
        var scopes = new List<SectionFilter> { new(L.Text("Controls.DocumentSelection.ScopeNotebook")), new(L.Text("Controls.DocumentSelection.ScopePage"), CurrentPage: true) };
        if (notebook is not null) scopes.AddRange(notebook.Sections.Select(section => new SectionFilter(section.Title, section.Id)));
        _scope = new ComboBox { ItemsSource = scopes, DisplayMemberPath = nameof(SectionFilter.Title), MinHeight = 44, SelectedIndex = Math.Max(0, scopes.FindIndex(scope => scope.SectionId is not null && scope.SectionId == _currentSectionId)) };
        Identify(_scope, "ExportSectionFilter", L.Text("Controls.DocumentSelection.SectionFilter"));
        Identify(_search, "ExportSearchFilter", L.Text("Controls.DocumentSelection.SearchFilter"));
        var filters = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        filters.ColumnDefinitions.Add(new()); filters.ColumnDefinitions.Add(new());
        var sectionPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        sectionPanel.Children.Add(new TextBlock { Text = L.Text("Controls.DocumentSelection.SectionFilter"), Margin = new Thickness(0, 0, 0, 4) }); sectionPanel.Children.Add(_scope); filters.Children.Add(sectionPanel);
        var searchPanel = new StackPanel(); Grid.SetColumn(searchPanel, 1);
        searchPanel.Children.Add(new TextBlock { Text = L.Text("Controls.DocumentSelection.SearchFilter"), Margin = new Thickness(0, 0, 0, 4) }); searchPanel.Children.Add(_search); filters.Children.Add(searchPanel); header.Children.Add(filters);
        var rangePanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        rangePanel.Children.Add(new TextBlock { Text = L.Text("Controls.DocumentSelection.Range"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _range.Margin = new Thickness(0, 0, 8, 0); _range.ToolTip = L.Text("Controls.DocumentSelection.FilterRangeHelp");
        Identify(_range, "ExportRangeFilter", L.Text("Controls.DocumentSelection.FilterRangeHelp")); rangePanel.Children.Add(_range);
        AddButton(rangePanel, L.Text("Controls.DocumentSelection.FilterRange"), ApplyRangeFilter, "ApplyExportRange");
        AddButton(rangePanel, L.Text("Controls.DocumentSelection.ResetFilters"), () =>
        {
            _updating = true;
            try { _scope.SelectedIndex = 0; _search.Clear(); _range.Clear(); _bookmarked.IsChecked = false; _appliedRange = ""; _rangeIndexes = null; _rangeValid = true; _filterError = ""; }
            finally { _updating = false; }
            RefreshFilter();
        }, "ResetExportFilters");
        header.Children.Add(rangePanel);
        _scope.SelectionChanged += (_, _) => RefreshFilter(); _search.TextChanged += (_, _) => RefreshFilter();
        _range.TextChanged += (_, _) => { if (!_updating) UpdateCount(); };
        _range.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyRangeFilter(); e.Handled = true; } };
    }

    private void ApplyRangeFilter()
    {
        _appliedRange = _range.Text.Trim();
        try
        {
            _rangeIndexes = _appliedRange.Length == 0 ? null : DocumentWorkflows.ParsePageRange(_appliedRange, _choices.Count).ToHashSet();
            _rangeValid = true; _filterError = "";
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            _rangeValid = false; _filterError = ex.Message;
        }
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        if (_updating) return;
        var scope = _scope?.SelectedItem as SectionFilter;
        var terms = _search.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var selected = _list.SelectedItem;
        _view.Filter = item => item is PageChoice choice && _rangeValid &&
            (scope?.SectionId is null || choice.Page.SectionId == scope.SectionId) &&
            (scope?.CurrentPage != true || choice.Page.Id == _currentPageId) &&
            (_bookmarked.IsChecked != true || choice.Page.IsBookmarked) &&
            (_rangeIndexes is null || _rangeIndexes.Contains(choice.Number - 1)) &&
            terms.All(term => choice.Label.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        _empty.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        _list.SelectedItem = selected is not null && _view.Contains(selected) ? selected : _view.Cast<PageChoice>().FirstOrDefault();
        if (_list.SelectedItem is not null) _list.ScrollIntoView(_list.SelectedItem);
        UpdateCount();
    }

    private void UpdateDestination()
    {
        _newTitle.Visibility = Destination?.Kind == ImportDestinationKind.Section ? Visibility.Collapsed : Visibility.Visible;
        _position.IsEnabled = Destination?.Kind == ImportDestinationKind.Section && Destination.SectionId == _currentSectionId;
    }
    private void ApplySelection(Func<PageChoice, bool> predicate)
    {
        _updating = true; try { foreach (var choice in _view.Cast<PageChoice>()) choice.Included = predicate(choice); } finally { _updating = false; }
        UpdateCount();
    }
    private void UpdateCount()
    {
        var count = SelectedPages.Count;
        var pendingRange = !_importing && _range.Text.Trim() != _appliedRange;
        _count.Text = _importing ? L.Text("Controls.DocumentSelection.SelectedCount", count, _choices.Count) : L.Text("Controls.DocumentSelection.FilteredCount", _view.Count, _choices.Count, count);
        if (_accept is not null) _accept.IsEnabled = count > 0 && !pendingRange;
        if (_selectAll is not null) _selectAll.IsEnabled = !_view.IsEmpty;
        if (_clear is not null) _clear.IsEnabled = count > 0;
        _message.Text = pendingRange ? L.Text("Controls.DocumentSelection.ApplyRangeFirst") : _filterError;
    }
    private static void Identify(DependencyObject control, string id, string name)
    {
        AutomationProperties.SetAutomationId(control, id); AutomationProperties.SetName(control, name);
    }
    private Button AddButton(Panel panel, string text, Action action, string id)
    {
        var button = new Button { Content = text, MinHeight = 44, Margin = new Thickness(0, 0, 6, 0), Style = TryFindResource("SecondaryButton") as Style };
        Identify(button, id, text); button.Click += (_, _) => action(); panel.Children.Add(button); return button;
    }
    private sealed record SectionFilter(string Title, string? SectionId = null, bool CurrentPage = false);
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
        _previewLabel.Text = L.Text("Controls.DocumentSelection.LoadingPreview", choice.Label);
        try
        {
            var image = await _renderer.RenderAsync(choice.Page, 700, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested) return;
            _preview.Source = image; _previewLabel.Text = choice.Label;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed && !cancellation.IsCancellationRequested) { _preview.Source = null; _previewLabel.Text = L.Text("Controls.Common.PreviewUnavailable", ex.Message); } }
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
