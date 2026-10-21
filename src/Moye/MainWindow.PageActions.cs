using L = Moye.Localization.LocalizationService;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Moye.Models;
using Moye.ViewModels;

namespace Moye;

public partial class MainWindow
{
    internal sealed record PageActionTarget(NotebookDocument Document, string SectionId, string PageId);
    private PageActionTarget? _paperActionTarget;

    // A menu retains the clicked page, not whichever page scrolling selects later.
    // Replaced documents (including undo), removed pages and section changes invalidate it.
    internal static PageViewModel? ResolvePageActionTarget(MainViewModel viewModel, PageActionTarget target)
    {
        if (!ReferenceEquals(viewModel.Document, target.Document) || viewModel.IsLibraryVisible ||
            viewModel.IsBusy || viewModel.SelectedSection?.Id != target.SectionId) return null;
        return viewModel.Pages.FirstOrDefault(page => page.Page.Id == target.PageId && page.Page.SectionId == target.SectionId);
    }

    private void ThumbnailRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // ListBoxItem normally selects on right-button down, which would scroll
        // the writing viewport before the page menu can appear.
        if (sender is ListBoxItem { DataContext: PageViewModel }) e.Handled = true;
    }

    private void PageContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.Handled || sender is not FrameworkElement { DataContext: PageViewModel page } owner ||
            HasInputAncestor<TextBoxBase>(e.OriginalSource as DependencyObject)) return;
        if (ViewModel.Document is null || ViewModel.IsBusy || ViewModel.IsLibraryVisible || AnyPenDown) return;
        var menu = CreatePageContextMenu(page);
        if (menu is null) return;
        ClearTouches(); CloseSettingsPopups();
        menu.PlacementTarget = owner;
        menu.Placement = e.CursorLeft < 0 ? PlacementMode.Center : PlacementMode.MousePoint;
        // Open explicitly instead of assigning an ancestor ContextMenu: text
        // boxes and future child controls keep their own editing menus.
        e.Handled = true;
        menu.IsOpen = true;
    }

    private ContextMenu? CreatePageContextMenu(PageViewModel page)
    {
        if (ViewModel.Document is not { } document) return null;
        var target = new PageActionTarget(document, page.Page.SectionId, page.Page.Id);
        if (ResolvePageActionTarget(ViewModel, target) is null) return null;
        var menu = new ContextMenu { Tag = target };
        menu.Items.Add(CreateLocalizedMenuHeading("Shell.PageHeading", page.Number));
        menu.Items.Add(new Separator());
        Add("Shell.RenamePageMenu", "\uE8AC", RenamePageClick);
        Add(page.Page.IsBookmarked ? "Shell.RemoveBookmark" : "Shell.BookmarkPage", "\uE734", TogglePageBookmarkClick);
        menu.Items.Add(new Separator());
        Add("Shell.DuplicatePage", "\uE8C8", DuplicatePageClick);
        Add("Shell.MovePageUp", "\uE74A", MovePageUpClick, ViewModel.Pages.IndexOf(page) > 0);
        Add("Shell.MovePageDown", "\uE74B", MovePageDownClick, ViewModel.Pages.IndexOf(page) < ViewModel.Pages.Count - 1);
        var sections = new MenuItem { Icon = CreateMenuIcon("\uE8DE"), Tag = target, IsEnabled = ViewModel.Sections.Count > 1 };
        L.Bind(sections, HeaderedItemsControl.HeaderProperty, "Shell.MovePageToSection");
        // A placeholder lets WPF show the submenu arrow before it is populated.
        sections.Items.Add(new MenuItem());
        sections.SubmenuOpened += MovePageSectionMenuOpened;
        menu.Items.Add(sections);
        menu.Items.Add(new Separator());
        Add("Shell.PaperStyleMenu", "\uE790", PageSettingsClick);
        Add("Shell.ExtendPageMenu", "\uE740", ExtendPageClick);
        menu.Items.Add(new Separator());
        Add("Shell.MarkQuestionMenu", "\uE897", AddStudyQuestionClick);
        Add("Shell.ResumeHintMenu", "\uE70B", SetResumeHintClick);
        Add("Shell.NewPracticeMenu", "\uE710", NewPracticeClick);
        Add("Shell.RestoreRegionMenu", "\uE81C", RestoreRegionClick);
        menu.Items.Add(new Separator());
        Add("Shell.DeletePage", "\uE74D", DeletePageClick, danger: true);
        return menu;

        void Add(string key, string glyph, RoutedEventHandler click, bool enabled = true, bool danger = false)
        {
            var item = new MenuItem { Icon = CreateMenuIcon(glyph), Tag = target, IsEnabled = enabled };
            L.Bind(item, HeaderedItemsControl.HeaderProperty, key);
            if (danger)
            {
                item.Style = (Style)FindResource("DangerMenuItem");
                L.Bind(item, FrameworkElement.ToolTipProperty, "Shell.DeletePageHint");
            }
            item.Click += click; menu.Items.Add(item);
        }
    }

    private PageActionTarget? CapturePageActionTarget(object? sender = null)
    {
        if (sender is MenuItem { Tag: PageActionTarget target }) return target;
        if (ViewModel.IsLibraryVisible || ViewModel.Document is not { } document) return null;
        var page = (sender as FrameworkElement)?.DataContext as PageViewModel ?? ViewModel.SelectedPage;
        return page is null ? null : new(document, page.Page.SectionId, page.Page.Id);
    }

    private void PageActionsClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || ViewModel.IsLibraryVisible || sender is not FrameworkElement owner ||
            ViewModel.SelectedPage is not { } page || CreatePageContextMenu(page) is not { } menu) return;
        ClearTouches(); CloseSettingsPopups();
        menu.PlacementTarget = owner; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
    }

    private void ShowMovePageToSection(PageActionTarget target)
    {
        if (!CanOrganize || ResolvePageActionTarget(ViewModel, target) is not { } page) return;
        var menu = new ContextMenu { PlacementTarget = FeatureActionAnchor(PageActionsButton), Placement = FeatureMenuPlacement };
        menu.Items.Add(CreateLocalizedMenuHeading("Features.MovePageHeading", page.Number));
        foreach (var section in ViewModel.Sections)
        {
            var item = new MenuItem { Header = new TextBlock { Text = section.Title, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis },
                IsEnabled = section.Id != target.SectionId, ToolTip = section.Title };
            var sectionId = section.Id;
            item.Click += (_, _) => RunPageAction(target, () => ChangeSectionView(() => ViewModel.MovePageToSection(sectionId)));
            menu.Items.Add(item);
        }
        ClearTouches(); CloseSettingsPopups(); menu.IsOpen = true;
    }

    private MenuItem CreateLocalizedMenuHeading(string key, params object?[] args)
    {
        var heading = CreateMenuHeading("");
        L.Bind((TextBlock)heading.Header, TextBlock.TextProperty, key, args);
        L.Bind(heading, FrameworkElement.ToolTipProperty, key, args);
        return heading;
    }

    private MenuItem CreateMenuHeading(string title) => new()
    {
        Header = new TextBlock { Text = title, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis },
        ToolTip = title,
        Style = (Style)FindResource("ContextMenuHeading")
    };

    private static TextBlock CreateMenuIcon(string glyph) => new()
    {
        Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"),
        FontSize = 16, FontWeight = FontWeights.Normal,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };

    private bool ActivatePageAction(PageActionTarget target)
    {
        if (AnyPenDown || ResolvePageActionTarget(ViewModel, target) is null) return false;
        CommitEditors();
        if (ResolvePageActionTarget(ViewModel, target) is not { } page) return false;
        var previous = _suppressPageSelection;
        _suppressPageSelection = true;
        try { ViewModel.SelectedPage = page; SyncPageTemplate(); UpdateTextToolbar(); }
        finally { _suppressPageSelection = previous; }
        return true;
    }

    private void RunPageAction(object sender, Action action)
    {
        if (sender is MenuItem { Tag: PageActionTarget target }) RunPageAction(target, action);
    }

    private void RunPageAction(PageActionTarget target, Action action)
    {
        if (_closing || !ActivatePageAction(target)) return;
        action(); ScrollToSelected();
    }
}
