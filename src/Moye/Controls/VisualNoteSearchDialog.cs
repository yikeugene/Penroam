using System.Windows;
using System.Windows.Automation;
using System.Windows.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;
using Moye.Models;
using Moye.Services;

namespace Moye.Controls;

public sealed class VisualNoteSearchDialog : Window
{
    private readonly NotebookDocument _book;
    private readonly PagePreviewRenderer _renderer;
    private readonly ComboBox _color = new() { MinHeight = 44, MinWidth = 130 };
    private readonly ComboBox _position = new() { MinHeight = 44, MinWidth = 145 };
    private readonly ComboBox _mark = new() { MinHeight = 44, MinWidth = 145 };
    private readonly ComboBox _section = new() { MinHeight = 44, MinWidth = 160, MaxWidth = 280 };
    private readonly ListBox _results = new() { BorderThickness = new Thickness(0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private CancellationTokenSource? _search;
    private bool _closed;
    public VisualNoteMatch? Result => (_results.SelectedItem as ListBoxItem)?.Tag as VisualNoteMatch;

    public VisualNoteSearchDialog(Window owner, NotebookDocument book, PagePreviewRenderer renderer)
    {
        Owner = owner?.IsLoaded == true ? owner : null; Title = "Find by appearance"; Width = 780; Height = 660;
        MinWidth = 480; MinHeight = 420; MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = Brushes.White;
        _book = book.Snapshot(); _renderer = renderer;
        var root = new DockPanel { Margin = new Thickness(22) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top);
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Find ink and typed notes in this notebook by color and position. PDF and image colors are not indexed.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
        var filters = new WrapPanel();
        var filterLabel = new FrameworkElementFactory(typeof(TextBlock));
        filterLabel.SetBinding(TextBlock.TextProperty, new Binding { Converter = new FilterLabelConverter() });
        var filterTemplate = new DataTemplate { VisualTree = filterLabel };
        _color.ItemTemplate = filterTemplate; _position.ItemTemplate = filterTemplate; _mark.ItemTemplate = filterTemplate;
        _color.ItemsSource = Enum.GetValues<NoteColor>(); _color.SelectedIndex = 0;
        _position.ItemsSource = Enum.GetValues<NotePosition>(); _position.SelectedIndex = 0;
        _mark.ItemsSource = Enum.GetValues<NoteMark>(); _mark.SelectedIndex = 0;
        _section.Items.Add(new ComboBoxItem { Content = "All sections", Tag = "" });
        foreach (var section in _book.Sections) _section.Items.Add(new ComboBoxItem { Content = section.Title, Tag = section.Id });
        _section.SelectedIndex = 0;
        foreach (var (label, input) in new[] { ("Color", _color), ("Position on page", _position), ("Mark", _mark), ("Section", _section) })
        {
            var group = new StackPanel { Margin = new Thickness(0, 4, 10, 0) }; group.Children.Add(new Label { Content = label, Target = input }); group.Children.Add(input); filters.Children.Add(group);
            AutomationProperties.SetName(input, label); input.SelectionChanged += async (_, _) => await SearchAsync();
        }
        heading.Children.Add(filters); heading.Children.Add(_status); root.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var close = OrganizationDialogUi.Button(this, "Close", () => DialogResult = false); close.IsCancel = true;
        var open = OrganizationDialogUi.Button(this, "Open page", () => { if (Result is not null) DialogResult = true; }); open.IsEnabled = false;
        buttons.Children.Add(close); buttons.Children.Add(open); root.Children.Add(buttons);
        _results.SelectionChanged += (_, _) => open.IsEnabled = Result is not null;
        _results.MouseDoubleClick += (_, _) => { if (Result is not null) DialogResult = true; };
        ScrollViewer.SetHorizontalScrollBarVisibility(_results, ScrollBarVisibility.Disabled);
        root.Children.Add(_results); Content = root;
        Loaded += async (_, _) => await SearchAsync();
        Closed += (_, _) => { _closed = true; _search?.Cancel(); };
    }

    private sealed class FilterLabelConverter : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture) =>
            Regex.Replace(value?.ToString() ?? "", "([a-z])([A-Z])", "$1 $2");
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    private async Task SearchAsync()
    {
        if (!IsLoaded || _closed) return;
        _search?.Cancel();
        using var request = new CancellationTokenSource(); _search = request;
        _results.Items.Clear(); _status.Text = "Looking through your notes…";
        try
        {
            var color = (NoteColor)_color.SelectedItem; var position = (NotePosition)_position.SelectedItem; var mark = (NoteMark)_mark.SelectedItem;
            var section = (string)((ComboBoxItem)_section.SelectedItem).Tag;
            var matches = await Task.Run(() => VisualNoteSearch.Find(_book, color, position, mark, section, request.Token), request.Token);
            request.Token.ThrowIfCancellationRequested(); if (_closed) return;
            _status.Text = matches.Count == 0 ? "No matches. Try another color or a wider area." : $"{matches.Count} matching pages · showing up to 60 previews";
            foreach (var match in matches.Take(60))
            {
                request.Token.ThrowIfCancellationRequested();
                var page = _book.Pages.First(p => p.Id == match.PageId);
                var preview = await _renderer.RenderAsync(page, 300, request.Token);
                request.Token.ThrowIfCancellationRequested();
                var row = new DockPanel { Margin = new Thickness(8), LastChildFill = true };
                var image = new Image { Source = preview, Width = 110, Height = 140, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 18, 0) }; DockPanel.SetDock(image, Dock.Left); row.Children.Add(image);
                var description = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                description.Children.Add(new TextBlock { Text = match.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                description.Children.Add(new TextBlock { Text = $"{match.Count} matching mark(s)", Margin = new Thickness(0, 8, 0, 0) });
                row.Children.Add(description);
                _results.Items.Add(new ListBoxItem { Content = row, Tag = match, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed && !request.IsCancellationRequested) _status.Text = "Search could not finish: " + ex.Message; }
        finally { if (ReferenceEquals(_search, request)) _search = null; }
    }
}
