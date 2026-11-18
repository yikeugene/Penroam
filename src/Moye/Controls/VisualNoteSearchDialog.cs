using System.Windows;
using System.Windows.Automation;
using System.Windows.Data;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Moye.Models;
using Moye.Services;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

public sealed class VisualNoteSearchDialog : Window
{
    private const int PreviewBatchSize = 60;
    private readonly NotebookDocument _book;
    private readonly PagePreviewRenderer _renderer;
    private readonly ComboBox _color = new() { MinHeight = 44, MinWidth = 130 };
    private readonly ComboBox _position = new() { MinHeight = 44, MinWidth = 145 };
    private readonly ComboBox _mark = new() { MinHeight = 44, MinWidth = 145 };
    private readonly ComboBox _section = new() { MinHeight = 44, MinWidth = 160, MaxWidth = 280 };
    private readonly ListBox _results = new() { BorderThickness = new Thickness(0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _loadMore;
    private IReadOnlyList<VisualNoteMatch> _matches = [];
    private int _nextResult;
    private CancellationTokenSource? _search;
    private bool _closed;
    public VisualNoteMatch? Result => (_results.SelectedItem as ListBoxItem)?.Tag as VisualNoteMatch;

    public VisualNoteSearchDialog(Window owner, NotebookDocument book, PagePreviewRenderer renderer)
    {
        Owner = owner?.IsLoaded == true ? owner : null; Title = L.Text("Controls.VisualSearch.Title"); Width = 780; Height = 660;
        MinWidth = 480; MinHeight = 420; MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = Brushes.White;
        _book = book.Snapshot(); _renderer = renderer;
        var root = new DockPanel { Margin = new Thickness(22) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top);
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = L.Text("Controls.VisualSearch.Intro"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
        var filters = new WrapPanel();
        var filterLabel = new FrameworkElementFactory(typeof(TextBlock));
        filterLabel.SetBinding(TextBlock.TextProperty, new Binding { Converter = new FilterLabelConverter() });
        var filterTemplate = new DataTemplate { VisualTree = filterLabel };
        _color.ItemTemplate = filterTemplate; _position.ItemTemplate = filterTemplate; _mark.ItemTemplate = filterTemplate;
        _color.ItemsSource = Enum.GetValues<NoteColor>(); _color.SelectedIndex = 0;
        _position.ItemsSource = Enum.GetValues<NotePosition>(); _position.SelectedIndex = 0;
        _mark.ItemsSource = Enum.GetValues<NoteMark>(); _mark.SelectedIndex = 0;
        _section.Items.Add(new ComboBoxItem { Content = L.Text("Controls.VisualSearch.AllSections"), Tag = "" });
        foreach (var section in _book.Sections) _section.Items.Add(new ComboBoxItem { Content = section.Title, Tag = section.Id });
        _section.SelectedIndex = 0;
        foreach (var (label, input) in new[] { (L.Text("Controls.Common.Color"), _color), (L.Text("Controls.VisualSearch.Position"), _position), (L.Text("Controls.VisualSearch.Mark"), _mark), (L.Text("Controls.Common.Section"), _section) })
        {
            var group = new StackPanel { Margin = new Thickness(0, 4, 10, 0) }; group.Children.Add(new Label { Content = label, Target = input }); group.Children.Add(input); filters.Children.Add(group);
            AutomationProperties.SetName(input, label); input.SelectionChanged += async (_, _) => { if (IsLoaded) await SearchAsync(); };
        }
        heading.Children.Add(filters); heading.Children.Add(_status); root.Children.Add(heading);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom);
        _loadMore = OrganizationDialogUi.Button(this, L.Text("Controls.VisualSearch.LoadMore"), async () => await LoadMoreAsync());
        _loadMore.Name = "LoadMoreVisualSearchResults"; _loadMore.Visibility = Visibility.Collapsed;
        L.Bind(_loadMore, ContentControl.ContentProperty, "Controls.VisualSearch.LoadMore");
        L.Bind(_loadMore, AutomationProperties.NameProperty, "Controls.VisualSearch.LoadMore");
        var close = OrganizationDialogUi.Button(this, L.Text("Controls.Common.Close"), () => DialogResult = false); close.IsCancel = true;
        var open = OrganizationDialogUi.Button(this, L.Text("Controls.VisualSearch.Open"), () => { if (Result is not null) DialogResult = true; }); open.IsEnabled = false;
        buttons.Children.Add(_loadMore); buttons.Children.Add(close); buttons.Children.Add(open); root.Children.Add(buttons);
        _results.SelectionChanged += (_, _) => open.IsEnabled = Result is not null;
        _results.MouseDoubleClick += (_, _) => { if (Result is not null) DialogResult = true; };
        _results.KeyDown += (_, e) => { if (e.Key == Key.Enter && Result is not null) { DialogResult = true; e.Handled = true; } };
        L.Bind(_results, AutomationProperties.NameProperty, "Controls.VisualSearch.Results");
        ScrollViewer.SetHorizontalScrollBarVisibility(_results, ScrollBarVisibility.Disabled);
        root.Children.Add(_results); Content = root;
        Loaded += async (_, _) => await SearchAsync();
        Closed += (_, _) => { _closed = true; _search?.Cancel(); };
    }

    private sealed class FilterLabelConverter : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture) =>
            value switch
            {
                NoteColor.Any => L.Text("Controls.VisualSearch.NoteColor.Any"),
                NoteColor.Red => L.Text("Controls.VisualSearch.NoteColor.Red"),
                NoteColor.Orange => L.Text("Controls.VisualSearch.NoteColor.Orange"),
                NoteColor.Yellow => L.Text("Controls.VisualSearch.NoteColor.Yellow"),
                NoteColor.Green => L.Text("Controls.VisualSearch.NoteColor.Green"),
                NoteColor.Blue => L.Text("Controls.VisualSearch.NoteColor.Blue"),
                NoteColor.Purple => L.Text("Controls.VisualSearch.NoteColor.Purple"),
                NoteColor.Black => L.Text("Controls.VisualSearch.NoteColor.Black"),
                NoteColor.Gray => L.Text("Controls.VisualSearch.NoteColor.Gray"),
                NotePosition.Anywhere => L.Text("Controls.VisualSearch.NotePosition.Anywhere"),
                NotePosition.TopLeft => L.Text("Controls.VisualSearch.NotePosition.TopLeft"),
                NotePosition.Top => L.Text("Controls.VisualSearch.NotePosition.Top"),
                NotePosition.TopRight => L.Text("Controls.VisualSearch.NotePosition.TopRight"),
                NotePosition.Left => L.Text("Controls.VisualSearch.NotePosition.Left"),
                NotePosition.Center => L.Text("Controls.VisualSearch.NotePosition.Center"),
                NotePosition.Right => L.Text("Controls.VisualSearch.NotePosition.Right"),
                NotePosition.BottomLeft => L.Text("Controls.VisualSearch.NotePosition.BottomLeft"),
                NotePosition.Bottom => L.Text("Controls.VisualSearch.NotePosition.Bottom"),
                NotePosition.BottomRight => L.Text("Controls.VisualSearch.NotePosition.BottomRight"),
                NoteMark.Any => L.Text("Controls.VisualSearch.NoteMark.Any"),
                NoteMark.Handwriting => L.Text("Controls.VisualSearch.NoteMark.Handwriting"),
                NoteMark.Highlighter => L.Text("Controls.VisualSearch.NoteMark.Highlighter"),
                NoteMark.TypedText => L.Text("Controls.VisualSearch.NoteMark.TypedText"),
                _ => value?.ToString() ?? ""
            };
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    internal async Task SearchAsync()
    {
        if (_closed) return;
        _search?.Cancel();
        using var request = new CancellationTokenSource(); _search = request;
        _matches = []; _nextResult = 0; UpdateLoadMore();
        _results.Items.Clear(); _status.Text = L.Text("Controls.VisualSearch.Searching");
        try
        {
            var color = (NoteColor)_color.SelectedItem; var position = (NotePosition)_position.SelectedItem; var mark = (NoteMark)_mark.SelectedItem;
            var section = (string)((ComboBoxItem)_section.SelectedItem).Tag;
            var matches = await Task.Run(() => VisualNoteSearch.Find(_book, color, position, mark, section, request.Token), request.Token);
            request.Token.ThrowIfCancellationRequested(); if (_closed) return;
            _matches = matches;
            await AppendBatchAsync(request.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowFailure(request, ex); }
        finally { if (ReferenceEquals(_search, request)) { _search = null; UpdateLoadMore(); } }
    }

    internal async Task LoadMoreAsync()
    {
        if (_closed || _search is not null || _nextResult >= _matches.Count) return;
        var returnFocus = _loadMore.IsKeyboardFocusWithin;
        using var request = new CancellationTokenSource(); _search = request; UpdateLoadMore();
        _status.Text = L.Text("Controls.VisualSearch.LoadingMore", _nextResult, _matches.Count);
        try { await AppendBatchAsync(request.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowFailure(request, ex); }
        finally
        {
            if (ReferenceEquals(_search, request))
            {
                _search = null; UpdateLoadMore();
                if (returnFocus && IsActive && _loadMore.Visibility != Visibility.Visible) _results.Focus();
            }
        }
    }

    private async Task AppendBatchAsync(CancellationToken token)
    {
        var end = Math.Min(_nextResult + PreviewBatchSize, _matches.Count);
        while (_nextResult < end)
        {
            token.ThrowIfCancellationRequested();
            var match = _matches[_nextResult];
            var page = _book.Pages.First(p => p.Id == match.PageId);
            var preview = await _renderer.RenderAsync(page, 300, token);
            token.ThrowIfCancellationRequested();
            var row = new DockPanel { Margin = new Thickness(8), LastChildFill = true };
            var image = new Image { Source = preview, Width = 110, Height = 140, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 18, 0) }; DockPanel.SetDock(image, Dock.Left); row.Children.Add(image);
            var description = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            description.Children.Add(new TextBlock { Text = match.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            description.Children.Add(new TextBlock { Text = L.Text("Controls.VisualSearch.MarkCount", match.Count), Margin = new Thickness(0, 8, 0, 0) });
            row.Children.Add(description);
            var item = new ListBoxItem { Content = row, Tag = match, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(item, match.Title);
            _results.Items.Add(item); _nextResult++;
        }
        _status.Text = ResultCountText();
    }

    private string ResultCountText() => _matches.Count == 0 ? L.Text("Controls.VisualSearch.NoMatches")
        : L.Text("Controls.VisualSearch.ShowingCount", _nextResult, _matches.Count);

    private void ShowFailure(CancellationTokenSource request, Exception exception)
    {
        if (_closed || request.IsCancellationRequested || !ReferenceEquals(_search, request)) return;
        _status.Text = (_matches.Count > 0 ? ResultCountText() + "\n" : "") + L.Text("Controls.Common.SearchFailed", exception.Message);
    }

    private void UpdateLoadMore()
    {
        _loadMore.Visibility = _nextResult < _matches.Count ? Visibility.Visible : Visibility.Collapsed;
        _loadMore.IsEnabled = _search is null && _nextResult < _matches.Count;
    }
}
