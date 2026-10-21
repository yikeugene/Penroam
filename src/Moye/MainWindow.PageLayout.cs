using L = Moye.Localization.LocalizationService;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Moye.ViewModels;

namespace Moye;

public partial class MainWindow
{
    private readonly PageSpreadCollection _pageSpreads = new();
    private readonly Dictionary<PageViewModel, PageSpreadViewModel> _spreadByPage = [];
    private DispatcherOperation? _pageSpreadRefresh;
    private bool _twoPageView, _pageSpreadsDirty = true, _syncingPageRows, _changingPageLayout;
    public ObservableCollection<PageSpreadViewModel> PageSpreads => _pageSpreads;

    private sealed class PageSpreadCollection : ObservableCollection<PageSpreadViewModel>
    {
        public void ReplaceRows(IReadOnlyList<PageSpreadViewModel> rows)
        {
            // One reset for an entire section/import, rather than one notification
            // and a full re-pairing for each page in the flat collection.
            Items.Clear();
            foreach (var row in rows) Items.Add(row);
            OnPropertyChanged(new(nameof(Count)));
            OnPropertyChanged(new("Item[]"));
            OnCollectionChanged(new(NotifyCollectionChangedAction.Reset));
        }
    }

    private void InitializePageLayout()
    {
        // An explicit source also survives the detached production-template preview.
        PageList.ItemsSource = PageSpreads;
        ViewModel.Pages.CollectionChanged += PageCollectionChanged;
        ViewModel.PropertyChanged += PageLayoutPropertyChanged;
        ViewModel.DocumentReplaced += (_, _) =>
        {
            _zoomNavigationRevision++; _zoomNavigationActive = false;
            EnsurePageSpreadsCurrent();
        };
        EnsurePageSpreadsCurrent(); UpdatePageLayoutUi();
    }

    private void PageCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _pageSpreadsDirty = true;
        if (_pageSpreadRefresh is { Status: DispatcherOperationStatus.Pending }) return;
        _pageSpreadRefresh = Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            _pageSpreadRefresh = null;
            if (!_closing) EnsurePageSpreadsCurrent();
        }));
    }

    private void PageLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedPage) && !_pageSpreadsDirty) SyncPageRowSelection();
    }

    private void EnsurePageSpreadsCurrent()
    {
        if (!_pageSpreadsDirty) return;
        _pageSpreadRefresh?.Abort(); _pageSpreadRefresh = null;
        _pageSpreadsDirty = false;
        var rows = PageSpreadViewModel.CreateRows(ViewModel.Pages, _twoPageView);
        var previous = _suppressPageSelection;
        _suppressPageSelection = true; _syncingPageRows = true;
        try
        {
            _spreadByPage.Clear();
            foreach (var row in rows)
            {
                _spreadByPage.Add(row.First, row);
                if (row.Second is not null) _spreadByPage.Add(row.Second, row);
            }
            _pageSpreads.ReplaceRows(rows);
            PageList.SelectedItem = ViewModel.SelectedPage is { } page ? _spreadByPage.GetValueOrDefault(page) : null;
        }
        finally { _syncingPageRows = false; _suppressPageSelection = previous; }
    }

    private PageSpreadViewModel? SpreadForPage(PageViewModel page)
    {
        EnsurePageSpreadsCurrent();
        return _spreadByPage.GetValueOrDefault(page);
    }

    private void SyncPageRowSelection()
    {
        if (_pageSpreadsDirty || _syncingPageRows) return;
        var previous = _suppressPageSelection;
        _suppressPageSelection = true; _syncingPageRows = true;
        try { PageList.SelectedItem = ViewModel.SelectedPage is { } page ? _spreadByPage.GetValueOrDefault(page) : null; }
        finally { _syncingPageRows = false; _suppressPageSelection = previous; }
    }

    private void PageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, PageList) || _syncingPageRows || _pageSpreadsDirty ||
            _suppressPageSelection || AnyPenDown || ViewModel.IsBusy || PageList.SelectedItem is not PageSpreadViewModel row) return;
        // A click/focus in the right editor has already activated that page.
        // Selecting its enclosing row must not turn it back into the left page.
        if (!row.Contains(ViewModel.SelectedPage))
        {
            var previous = _suppressPageSelection; _suppressPageSelection = true;
            try { ViewModel.SelectedPage = row.First; }
            finally { _suppressPageSelection = previous; }
        }
        SyncPageTemplate(); UpdateTextToolbar();
    }

    private void PageHostGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_closing && !ViewModel.IsBusy && sender is Border host) ActivatePage(host);
    }

    private void PageLayoutKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || !_ready || _closing || ViewModel.IsBusy || ViewModel.IsLibraryVisible || AnyPenDown ||
            ViewModel.SelectedPage is not { } current || Keyboard.Modifiers != ModifierKeys.None ||
            HasInputAncestor<TextBoxBase>(e.OriginalSource as DependencyObject) ||
            HasInputAncestor<ButtonBase>(e.OriginalSource as DependencyObject) ||
            HasInputAncestor<Slider>(e.OriginalSource as DependencyObject)) return;
        // WindowKeyDown gets first refusal for nudging a selected object. These
        // keys navigate individual pages only after editing has declined them.
        var index = ViewModel.Pages.IndexOf(current);
        var step = _twoPageView ? 2 : 1;
        var target = e.Key switch
        {
            Key.Left => index - 1,
            Key.Right => index + 1,
            Key.Up or Key.PageUp => index - step,
            Key.Down or Key.PageDown => index + step,
            Key.Home => 0,
            Key.End => ViewModel.Pages.Count - 1,
            _ => -1
        };
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)) return;
        e.Handled = true;
        target = Math.Clamp(target, 0, ViewModel.Pages.Count - 1);
        if (target == index) return;
        CommitEditors(); ClearTouches();
        var previous = _suppressPageSelection; _suppressPageSelection = true;
        try { ViewModel.SelectedPage = ViewModel.Pages[target]; SyncPageTemplate(); UpdateTextToolbar(); }
        finally { _suppressPageSelection = previous; }
        ScrollToSelected(); PageList.Focus();
    }

    private bool ScrollPageIntoView(PageViewModel page, bool revealPage = true)
    {
        if (AnyPenDown || SpreadForPage(page) is not { } row) return false;
        PageList.ScrollIntoView(row);
        if (!revealPage) return false;
        PageList.UpdateLayout();
        RevealPageHorizontally(page);
        PageList.UpdateLayout();
        Border? FindHost() => _editors.Keys.FirstOrDefault(host => host.IsLoaded && ReferenceEquals(host.DataContext, page));
        var host = FindHost();
        if (host is null) return false;
        var viewport = PageViewportBounds();
        var bounds = new Rect(host.TranslatePoint(new Point(), Viewport), host.RenderSize);
        // A tall partner can keep the row visible while this entire child is
        // above the viewport. Reveal that child, without moving a visible page.
        if (!AnyPenDown && viewport.Height > 0 && bounds.Height > 0 &&
            (bounds.Bottom <= viewport.Top || bounds.Top >= viewport.Bottom) && GetScroll() is { } scroll)
        {
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + bounds.Top - viewport.Top);
            PageList.UpdateLayout();
            host = FindHost();
            if (host is null) return false;
            viewport = PageViewportBounds();
            bounds = new(host.TranslatePoint(new Point(), Viewport), host.RenderSize);
        }
        var visible = Rect.Intersect(viewport, bounds);
        return !visible.IsEmpty && visible.Width > 0 && visible.Height > 0;
    }

    private Rect PageViewportBounds()
    {
        if (GetScroll() is { } scroll && Descendants<ScrollContentPresenter>(scroll).FirstOrDefault() is { } presenter)
            return new(presenter.TranslatePoint(new Point(), Viewport), presenter.RenderSize);
        return new(0, 0, Viewport.ActualWidth, Viewport.ActualHeight);
    }

    private void RevealPageHorizontally(PageViewModel page)
    {
        if (AnyPenDown || GetScroll() is not { } scroll) return;
        var host = _editors.Keys.FirstOrDefault(host => host.IsLoaded && ReferenceEquals(host.DataContext, page) && host.ActualWidth > 0);
        if (host is null) return;
        var viewport = PageViewportBounds();
        var bounds = new Rect(host.TranslatePoint(new Point(), Viewport), host.RenderSize);
        var delta = bounds.Left < viewport.Left || bounds.Width > viewport.Width
            ? bounds.Left - viewport.Left : bounds.Right > viewport.Right ? bounds.Right - viewport.Right : 0;
        if (Math.Abs(delta) > .5) scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + delta);
    }

    private PageZoomAnchor? CaptureSelectedPageAnchor(Point point)
    {
        if (ViewModel.SelectedPage is not { } page) return null;
        var host = _editors.Keys.FirstOrDefault(host => host.IsLoaded && ReferenceEquals(host.DataContext, page) && host.ActualWidth > 0 && host.ActualHeight > 0);
        if (host is null) return null;
        var origin = host.TranslatePoint(new Point(), Viewport);
        var bounds = new Rect(origin, host.RenderSize);
        var visible = Rect.Intersect(bounds, PageViewportBounds());
        if (visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0) return null;
        var paperPoint = new Point(Math.Clamp(point.X, visible.Left, visible.Right), Math.Clamp(point.Y, visible.Top, visible.Bottom));
        return new(page, new((paperPoint.X - origin.X) * page.Page.Width / host.ActualWidth,
            (paperPoint.Y - origin.Y) * page.Page.Height / host.ActualHeight), paperPoint);
    }

    private void SinglePageClick(object sender, RoutedEventArgs e) => ChangePageLayout(false);
    private void TwoPagesClick(object sender, RoutedEventArgs e) => ChangePageLayout(true);

    private void ApplyInitialPageLayout()
    {
        _twoPageView = _workspace.TwoPageView;
        _pageSpreadsDirty = true;
        EnsurePageSpreadsCurrent(); UpdatePageLayoutUi();
    }

    private void ChangePageLayout(bool twoPageView)
    {
        if (_closing || _changingPageLayout || ViewModel.IsBusy || AnyPenDown || _twoPageView == twoPageView)
        { UpdatePageLayoutUi(); return; }
        _changingPageLayout = true;
        var previous = _suppressPageSelection;
        try
        {
            CommitEditors(); EndTemporaryPan(); ClearTouches(); ResetViewportInputStability(); CloseSettingsPopups();
            var target = ViewModel.SelectedPage;
            var point = new Point(Viewport.ActualWidth / 2, Math.Min(80, Viewport.ActualHeight / 4));
            var anchor = CaptureSelectedPageAnchor(point) ?? (target is null ? null : new PageZoomAnchor(target, new(0, 0), point));
            var fitWidth = _fitWidthActive;
            var revision = BeginZoomNavigation();
            _suppressPageSelection = true;
            _twoPageView = twoPageView; _workspace.TwoPageView = twoPageView;
            _pageSpreadsDirty = true;
            EnsurePageSpreadsCurrent(); UpdatePageLayoutUi();
            _suppressPageSelection = previous;
            PageList.UpdateLayout();
            if (target is not null) ScrollPageIntoView(target, false);
            PageList.UpdateLayout();
            if (anchor is not null) RestorePageZoomAnchor(anchor);
            if (fitWidth)
            {
                FitWidth();
                // Keep the reading line while Fit Width owns horizontal centering.
                // Bringing the entire tall row into view again would lose that line.
                var fittedAnchor = anchor is null ? null : anchor with { RestoreHorizontal = false };
                if (fittedAnchor is not null) RestorePageZoomAnchor(fittedAnchor);
                CompleteZoomAfterLayout(_zoomNavigationRevision, ViewModel.Document, fittedAnchor, null);
            }
            else CompleteZoomAfterLayout(revision, ViewModel.Document, anchor, null);
            QueueWorkspaceSave();
        }
        catch { _zoomNavigationRevision++; _zoomNavigationActive = false; throw; }
        finally { _suppressPageSelection = previous; _changingPageLayout = false; }
    }

    private void UpdatePageLayoutUi()
    {
        SinglePageMenuItem.IsChecked = !_twoPageView;
        TwoPagesMenuItem.IsChecked = _twoPageView;
        var key = _twoPageView ? "Shell.FitSpread" : "Shell.FitPage";
        L.Bind(FitPageMenuItem, HeaderedItemsControl.HeaderProperty, key);
        L.Bind(FitPageButton, ContentControl.ContentProperty, key);
        L.Bind(FitPageButton, FrameworkElement.ToolTipProperty, _twoPageView ? "Shell.FitSpreadHint" : "Shell.FitPageHint");
        L.Bind(FitPageButton, AutomationProperties.NameProperty, key);
        L.Bind(FitWidthButton, FrameworkElement.ToolTipProperty, _twoPageView ? "Shell.FitSpreadWidthHint" : "Shell.FitWidthHint");
        UpdateFeatureNavigation();
    }
}
