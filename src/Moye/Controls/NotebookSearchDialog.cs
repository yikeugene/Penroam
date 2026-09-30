using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Moye.ViewModels;

namespace Moye.Controls;

public sealed class NotebookSearchDialog : Window
{
    private readonly TextBox _query = new() { MinHeight = 44, Margin = new Thickness(0, 10, 0, 8) };
    private readonly CheckBox _bookmarks = new() { Content = "Bookmarked pages only", MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ListBox _results = new() { MinHeight = 60, BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _search;
    private bool _closed;
    public NotebookSearchResult? Result => _results.SelectedItem as NotebookSearchResult;

    public NotebookSearchDialog(Window owner, MainViewModel model, bool bookmarksOnly = false)
    {
        Owner = owner?.IsLoaded == true ? owner : null; Title = bookmarksOnly ? "Bookmarks" : "Search all notes"; Width = 760; Height = 650;
        MinWidth = 460; MinHeight = 380; MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(24) };
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top);
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Find notebook, section and page titles, typed text and extractable PDF text. Search stays on this device.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        AutomationProperties.SetName(_query, "Search notebook content"); _query.ToolTip = "Search titles, typed text and original PDF text";
        heading.Children.Add(_query); heading.Children.Add(_bookmarks); heading.Children.Add(_status); root.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var cancel = OrganizationDialogUi.Button(this, "Close", () => DialogResult = false); cancel.IsCancel = true;
        var open = OrganizationDialogUi.Button(this, "Open result", () => { if (Result is not null) DialogResult = true; });
        open.IsEnabled = false; buttons.Children.Add(cancel); buttons.Children.Add(open); root.Children.Add(buttons);
        _results.SelectionChanged += (_, _) => open.IsEnabled = Result is not null;
        _results.MouseDoubleClick += (_, _) => { if (Result is not null) DialogResult = true; };
        _results.KeyDown += (_, e) => { if (e.Key == Key.Enter && Result is not null) { DialogResult = true; e.Handled = true; } };
        var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 10, 8, 10));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding(nameof(NotebookSearchResult.Location)));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); panel.AppendChild(title);
        var snippet = new FrameworkElementFactory(typeof(TextBlock)); snippet.SetBinding(TextBlock.TextProperty, new Binding(nameof(NotebookSearchResult.Snippet)));
        snippet.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); snippet.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0)); panel.AppendChild(snippet);
        _results.ItemTemplate = new DataTemplate { VisualTree = panel }; AutomationProperties.SetName(_results, "Search results");
        ScrollViewer.SetHorizontalScrollBarVisibility(_results, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_results, ScrollBarVisibility.Auto);
        root.Children.Add(_results); Content = root;
        _bookmarks.IsChecked = bookmarksOnly;
        void Queue() { _search?.Cancel(); _debounce.Stop(); _debounce.Start(); }
        _query.TextChanged += (_, _) => Queue(); _bookmarks.Checked += (_, _) => Queue(); _bookmarks.Unchecked += (_, _) => Queue();
        _query.KeyDown += (_, e) => { if (e.Key == Key.Down && _results.Items.Count > 0) { _results.SelectedIndex = 0; _results.Focus(); e.Handled = true; } };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop(); _search?.Cancel(); _search?.Dispose();
            var cancellation = _search = new CancellationTokenSource();
            var query = _query.Text; var bookmarks = _bookmarks.IsChecked == true;
            _status.Text = "Searching…";
            try
            {
                var results = await model.SearchContentAsync(query, bookmarks, cancellation.Token);
                if (_closed || cancellation.IsCancellationRequested) return;
                _results.ItemsSource = results;
                _status.Text = results.Count == 250 ? "Showing the first 250 matches. Refine your search." : results.Count > 0 ? $"{results.Count} result(s) · Choose a result to jump to its page." :
                    query.Trim().Length == 0 && !bookmarks ? "Enter a search term, or show bookmarked pages." : "No matching pages. Try a shorter phrase or turn off the bookmark filter.";
                if (query.Trim().Length > 0) _status.Text += "\n" + model.SearchCoverageMessage;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!_closed && !cancellation.IsCancellationRequested) _status.Text = "Search could not finish: " + ex.Message; }
        };
        Loaded += (_, _) => { _query.Focus(); Queue(); };
        Closed += (_, _) => { _closed = true; _debounce.Stop(); _search?.Cancel(); _search?.Dispose(); };
    }
}
