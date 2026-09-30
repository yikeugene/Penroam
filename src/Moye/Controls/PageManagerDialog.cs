using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Models;
using Moye.ViewModels;

namespace Moye.Controls;

public sealed class PageManagerDialog : Window
{
    public sealed class PageCard : ObservableObject
    {
        public required NotePage Page { get; init; }
        public required string Section { get; init; }
        public required int Number { get; init; }
        private BitmapSource? _thumbnail;
        public BitmapSource? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }
        public string Caption => $"{(Page.IsBookmarked ? "★ " : "")}{Number} · {(Page.Title.Length == 0 ? (Page.Pdf is null ? Page.Template.ToString() : "PDF") : Page.Title)}";
    }
    private readonly ListBox _pages = new() { SelectionMode = SelectionMode.Extended, AllowDrop = true, BorderThickness = new Thickness(0) };
    private readonly ComboBox _sections = new() { MinHeight = 44, MinWidth = 180, DisplayMemberPath = "Title", Margin = new Thickness(3) };
    private readonly TextBlock _status = new() { Margin = new Thickness(3, 8, 3, 8), TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, BitmapSource> _thumbnails;
    private readonly ObservableCollection<PageCard> _cards = [];
    private readonly Func<NotePage, Task<BitmapSource?>>? _render;
    private readonly HashSet<string> _failedThumbnails = [];
    private Point _dragStart;
    private PageCard? _pendingSingleSelection;
    private bool _closed, _dragEligible, _rendering;
    public NotebookDocument EditedDocument { get; }
    public IReadOnlyList<string> TransferPageIds { get; private set; } = [];
    public bool TransferRequested => TransferPageIds.Count > 0;

    public PageManagerDialog(Window owner, NotebookDocument document, IReadOnlyDictionary<string, BitmapSource> thumbnails,
        Func<NotePage, Task<BitmapSource?>>? render = null)
    {
        Owner = owner?.IsLoaded == true ? owner : null; Title = "Organize pages"; Width = 940; Height = 720; MinWidth = 660; MinHeight = 440;
        MaxHeight = SystemParameters.WorkArea.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = Brushes.White;
        EditedDocument = document.Snapshot(); _thumbnails = new(thumbnails); _render = render;
        var root = new DockPanel { Margin = new Thickness(20) }; var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(new TextBlock { Text = "Organize pages", FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Use each page's checkbox or Ctrl/Shift-click to select. Drag pages before another page, or use Move up/down. Apply changes together as one undo step.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        var actions = new WrapPanel();
        void ActionButton(string text, Action action) => actions.Children.Add(OrganizationDialogUi.Button(this, text, action));
        ActionButton("Select all", () => _pages.SelectAll());
        ActionButton("Move up", () => Edit(ids => PageOrganization.MoveBy(EditedDocument, ids, -1)));
        ActionButton("Move down", () => Edit(ids => PageOrganization.MoveBy(EditedDocument, ids, 1)));
        ActionButton("Duplicate", () => { var ids = PageOrganization.Duplicate(EditedDocument, Selection()); Rebuild(ids); });
        ActionButton("Title / bookmark", () =>
        {
            if (_pages.SelectedItems.Count != 1) { _status.Text = "Select one page to edit its title and bookmark."; return; }
            var page = ((PageCard)_pages.SelectedItems[0]!).Page;
            var dialog = new PageOrganizationDetailsDialog(this, page);
            if (dialog.ShowDialog() == true) { page.Title = dialog.PageTitle; page.IsBookmarked = dialog.IsBookmarked; Rebuild([page.Id]); }
        });
        ActionButton("Delete selected", () => Edit(ids => PageOrganization.Delete(EditedDocument, ids)));
        header.Children.Add(actions);
        var destinations = new WrapPanel(); _sections.ItemsSource = EditedDocument.Sections; _sections.SelectedIndex = 0;
        AutomationProperties.SetName(_sections, "Destination section"); destinations.Children.Add(_sections);
        destinations.Children.Add(OrganizationDialogUi.Button(this, "Move to section", () => { if (_sections.SelectedItem is NoteSection section) Edit(ids => PageOrganization.MoveToSection(EditedDocument, ids, section.Id)); }));
        destinations.Children.Add(OrganizationDialogUi.Button(this, "Move / copy to notebook…", () =>
        {
            TransferPageIds = Selection();
            if (TransferPageIds.Count == 0) { _status.Text = "Select one or more pages first."; return; }
            DialogResult = true;
        }));
        header.Children.Add(destinations); header.Children.Add(_status); root.Children.Add(header);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var cancel = OrganizationDialogUi.Button(this, "Cancel", () => DialogResult = false); cancel.IsCancel = true;
        var apply = OrganizationDialogUi.Button(this, "Apply changes", () => DialogResult = true); apply.IsDefault = true;
        bottom.Children.Add(cancel); bottom.Children.Add(apply); root.Children.Add(bottom);
        ScrollViewer.SetHorizontalScrollBarVisibility(_pages, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_pages, ScrollBarVisibility.Auto);
        _pages.ItemsSource = _cards; _pages.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
        var card = new FrameworkElementFactory(typeof(StackPanel)); card.SetValue(FrameworkElement.WidthProperty, 154d); card.SetValue(FrameworkElement.MarginProperty, new Thickness(7));
        var select = new FrameworkElementFactory(typeof(CheckBox)); select.SetValue(ContentControl.ContentProperty, "Select page"); select.SetValue(FrameworkElement.MinHeightProperty, 44d);
        select.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding("IsSelected") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1), Mode = BindingMode.TwoWay }); card.AppendChild(select);
        var image = new FrameworkElementFactory(typeof(Image)); image.SetBinding(Image.SourceProperty, new Binding(nameof(PageCard.Thumbnail)));
        image.SetValue(FrameworkElement.HeightProperty, 160d); image.SetValue(Image.StretchProperty, Stretch.Uniform);
        var caption = new FrameworkElementFactory(typeof(TextBlock)); caption.SetBinding(TextBlock.TextProperty, new Binding(nameof(PageCard.Caption)));
        caption.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); caption.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); caption.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 0)); card.AppendChild(caption);
        var sectionLabel = new FrameworkElementFactory(typeof(TextBlock)); sectionLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(PageCard.Section)));
        sectionLabel.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); sectionLabel.SetValue(TextBlock.FontSizeProperty, 11d); card.AppendChild(sectionLabel);
        card.AppendChild(image);
        _pages.ItemTemplate = new DataTemplate { VisualTree = card }; AutomationProperties.SetName(_pages, "Page organizer grid");
        _pages.SelectionChanged += (_, _) => _status.Text = $"{_pages.SelectedItems.Count} selected · {_cards.Count} pages · Cancel discards all organizer changes.";
        _pages.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var selected = FindCard(e.OriginalSource as DependencyObject);
            _dragStart = e.GetPosition(_pages); _dragEligible = selected is not null;
            _pendingSingleSelection = null;
            if (selected is not null && _pages.SelectedItems.Count > 1 && _pages.SelectedItems.Contains(selected) &&
                Keyboard.Modifiers == ModifierKeys.None && !FromCheckBox(e.OriginalSource as DependencyObject))
            { _pendingSingleSelection = selected; e.Handled = true; }
        };
        _pages.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_pendingSingleSelection is { } selected) { _pages.SelectedItem = selected; _pendingSingleSelection = null; e.Handled = true; }
            _dragEligible = false;
        };
        _pages.PreviewMouseMove += (_, e) =>
        {
            if (!_dragEligible || e.LeftButton != MouseButtonState.Pressed || _pages.SelectedItems.Count == 0) return;
            var at = e.GetPosition(_pages);
            if (Math.Abs(at.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(at.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragEligible = false; _pendingSingleSelection = null;
            DragDrop.DoDragDrop(_pages, new DataObject("Moye.PageIds", Selection().ToArray()), DragDropEffects.Move); e.Handled = true;
        };
        _pages.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent("Moye.PageIds") && FindCard(e.OriginalSource as DependencyObject) is not null ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        _pages.Drop += (_, e) =>
        {
            if (e.Data.GetData("Moye.PageIds") is string[] ids && FindCard(e.OriginalSource as DependencyObject) is { } target)
            { PageOrganization.MoveBefore(EditedDocument, ids, target.Page.Id); Rebuild(ids); }
            e.Handled = true;
        };
        root.Children.Add(_pages); Content = root; Rebuild([]);
        Loaded += async (_, _) => await RenderMissingThumbnailsAsync(); Closed += (_, _) => _closed = true;
    }

    private IReadOnlyList<string> Selection() => _pages.SelectedItems.Cast<PageCard>().Select(card => card.Page.Id).ToArray();
    private void Edit(Action<IReadOnlyList<string>> edit)
    {
        var ids = Selection(); if (ids.Count == 0) { _status.Text = "Select one or more pages first."; return; }
        edit(ids); Rebuild(ids);
    }
    private void Rebuild(IEnumerable<string> selection)
    {
        var ids = selection.ToHashSet(); _cards.Clear();
        foreach (var section in EditedDocument.Sections)
        {
            var index = 0;
            foreach (var page in EditedDocument.Pages.Where(page => page.SectionId == section.Id))
                _cards.Add(new PageCard { Page = page, Section = section.Title, Number = ++index, Thumbnail = _thumbnails.GetValueOrDefault(page.Id) });
        }
        foreach (var card in _cards.Where(card => ids.Contains(card.Page.Id))) _pages.SelectedItems.Add(card);
        _status.Text = $"{_pages.SelectedItems.Count} selected · {_cards.Count} pages · Changes are not applied until you choose Apply.";
        if (IsLoaded) _ = RenderMissingThumbnailsAsync();
    }
    private async Task RenderMissingThumbnailsAsync()
    {
        if (_render is null || _rendering) return;
        _rendering = true;
        try
        {
            while (!_closed && _cards.FirstOrDefault(card => card.Thumbnail is null && !_failedThumbnails.Contains(card.Page.Id)) is { } card)
            {
                try
                {
                    var bitmap = await _render(card.Page.Snapshot());
                    if (bitmap is null) _failedThumbnails.Add(card.Page.Id);
                    else { _thumbnails[card.Page.Id] = bitmap; foreach (var current in _cards.Where(candidate => candidate.Page.Id == card.Page.Id)) current.Thumbnail = bitmap; }
                }
                catch { _failedThumbnails.Add(card.Page.Id); /* Opening the page reports damaged assets. */ }
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        finally { _rendering = false; }
    }
    private static PageCard? FindCard(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: PageCard card }) return card;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : null;
        }
        return null;
    }
    private static bool FromCheckBox(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is CheckBox) return true;
            if (element is ListBoxItem) return false;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : null;
        }
        return false;
    }
}
