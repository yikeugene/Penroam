using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;
using Moye.ViewModels;

namespace Moye;

public partial class MainWindow
{
    internal Func<NotebookDocument, string, Task>? PreserveOrganizationDeletionAsync { get; set; }
    private NotebookTransferReceipt? _lastPageTransfer;
    private readonly List<OrganizationVisit> _organizationBack = [];
    private sealed record OrganizationVisit(string NotebookId, string SectionId, string PageId, double Zoom, Point PageFraction, Point ViewportFraction);

    private bool CanOrganize => !_closing && !AnyPenDown && !ViewModel.IsBusy;

    private async void QuickNoteClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize) return;
        CommitEditors(); ClearTouches(); RememberOrganizationVisit();
        await RunAsync("Creating a quick note…", async () => { await ViewModel.CreateQuickNoteAsync(); await PrepareNotebookViewAsync(false); SetTool(InkTool.Pen); });
    }

    private void SearchContentsClick(object sender, RoutedEventArgs e) => ShowOrganizationSearch(false);
    private void ShowBookmarksClick(object sender, RoutedEventArgs e) => ShowOrganizationSearch(true);
    private async void ShowOrganizationSearch(bool bookmarksOnly)
    {
        if (!CanOrganize) return;
        CommitEditors(); CloseSettingsPopups();
        var dialog = new NotebookSearchDialog(this, ViewModel, bookmarksOnly);
        if (dialog.ShowDialog() == true && dialog.Result is { } result)
        {
            RememberOrganizationVisit();
            await NavigateOrganizationAsync(result.NotebookId, result.SectionId, result.PageId);
        }
    }

    private void RememberOrganizationVisit()
    {
        CaptureReadingPosition();
        if (ViewModel.IsLibraryVisible || ViewModel.Document is null || ViewModel.SelectedSection is null || ViewModel.SelectedPage is null) return;
        var at = new Point(Viewport.ActualWidth / 2, Math.Min(80, Viewport.ActualHeight / 4));
        var anchor = CapturePageZoomAnchor(at);
        var page = anchor?.Page.Page ?? ViewModel.SelectedPage.Page;
        var point = anchor?.PagePoint ?? new Point(); var view = anchor?.ViewportPoint ?? at;
        var visit = new OrganizationVisit(ViewModel.Document.Id, page.SectionId, page.Id, ViewModel.Zoom,
            new Point(point.X / page.Width, point.Y / page.Height),
            new Point(Viewport.ActualWidth > 0 ? view.X / Viewport.ActualWidth : .5, Viewport.ActualHeight > 0 ? view.Y / Viewport.ActualHeight : 0));
        if (_organizationBack.LastOrDefault() == visit) return;
        _organizationBack.Add(visit); if (_organizationBack.Count > 50) _organizationBack.RemoveAt(0);
    }

    private async Task<bool> NavigateOrganizationAsync(string notebookId, string sectionId, string pageId, OrganizationVisit? restore = null)
    {
        var completed = false;
        await RunAsync("Opening page…", async () =>
        {
            CommitEditors(); ClearTouches();
            if (ViewModel.Document?.Id != notebookId || ViewModel.IsLibraryVisible)
            { await ViewModel.OpenAsync(notebookId); await PrepareNotebookViewAsync(); }
            var page = ViewModel.Document?.Pages.FirstOrDefault(page => page.Id == pageId);
            if (page is null) throw new IOException("This page is no longer available. Search again to see current results.");
            var section = ViewModel.Sections.First(section => section.Id == page.SectionId);
            // Navigation inside RunAsync is busy by design; assign the VM directly after committing editors.
            ViewModel.SelectedSection = section;
            ViewModel.SelectedPage = ViewModel.Pages.First(page => page.Page.Id == pageId);
            if (restore is not null) { _fitWidthActive = false; ViewModel.Zoom = restore.Zoom; }
            ShowSidebarTab(false); ScrollToSelected();
            await Dispatcher.InvokeAsync(() =>
            {
                ScrollToSelected();
                if (restore is not null && ViewModel.SelectedPage is { } selected)
                    RestorePageZoomAnchor(new(selected,
                        new Point(restore.PageFraction.X * selected.Page.Width, restore.PageFraction.Y * selected.Page.Height),
                        new Point(restore.ViewportFraction.X * Viewport.ActualWidth, restore.ViewportFraction.Y * Viewport.ActualHeight)));
            }, DispatcherPriority.Loaded);
            completed = true;
        });
        return completed;
    }

    private async void NavigateBackClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize) return;
        if (_organizationBack.Count == 0) { ViewModel.Status = "No earlier search or quick-note location."; return; }
        var visit = _organizationBack[^1];
        if (await NavigateOrganizationAsync(visit.NotebookId, visit.SectionId, visit.PageId, visit)) _organizationBack.RemoveAt(_organizationBack.Count - 1);
    }

    private NotePage? OrganizationPageTarget(object sender)
    {
        if (sender is MenuItem { Tag: PageActionTarget target }) return ResolvePageActionTarget(ViewModel, target)?.Page;
        if (sender is FrameworkElement { DataContext: PageViewModel page } && ViewModel.Document?.Pages.Any(candidate => candidate.Id == page.Page.Id) == true) return page.Page;
        return ViewModel.SelectedPage?.Page;
    }

    private void RenamePageClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || OrganizationPageTarget(sender) is not { } page) return;
        CommitEditors();
        var dialog = new PageOrganizationDetailsDialog(this, page);
        if (dialog.ShowDialog() == true) { ViewModel.SetPageDetails(page.Id, dialog.PageTitle, dialog.IsBookmarked); ScrollToSelected(); }
    }
    private void TogglePageBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || OrganizationPageTarget(sender) is not { } page) return;
        CommitEditors(); ViewModel.SetPageDetails(page.Id, page.Title, !page.IsBookmarked);
    }

    private NotebookSummary? OrganizationNotebookTarget(object sender)
    {
        if (sender is FrameworkElement { DataContext: NotebookSummary summary }) return summary;
        return ViewModel.Document is { } document ? new NotebookSummary { Id = document.Id, Title = document.Title, Folder = document.Folder,
            IsPinned = document.IsPinned, CoverColor = document.CoverColor } : null;
    }
    private async void EditNotebookAppearanceClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || OrganizationNotebookTarget(sender) is not { } summary) return;
        var dialog = new NotebookOrganizationDialog(this, summary);
        if (dialog.ShowDialog() == true)
        { CommitEditors(); await RunAsync("Updating notebook appearance…", () => ViewModel.SetNotebookAppearanceAsync(summary.Id, dialog.IsPinned, dialog.CoverColor)); }
    }
    private async void TogglePinNotebookClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || OrganizationNotebookTarget(sender) is not { } summary) return;
        CommitEditors(); await RunAsync("Updating pinned notebooks…", () => ViewModel.SetNotebookAppearanceAsync(summary.Id, !summary.IsPinned, summary.CoverColor));
    }
    private void LibraryOrganizationClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize) return;
        var dialog = new LibraryOrganizationDialog(this, ViewModel);
        if (dialog.ShowDialog() == true) { ViewModel.CategoryFilter = dialog.Category; ViewModel.SortNotebooksByName = dialog.SortByName; }
    }

    private async void ManagePagesClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || ViewModel.Document is null) return;
        CommitEditors(); CloseSettingsPopups();
        var before = ViewModel.Document.Snapshot();
        var thumbnails = ViewModel.Pages.Where(page => page.Thumbnail is not null).ToDictionary(page => page.Page.Id, page => page.Thumbnail!);
        var dialog = new PageManagerDialog(this, before, thumbnails, RenderOrganizationThumbnailAsync);
        if (dialog.ShowDialog() != true) return;
        PageOrganizationTransferDialog? transfer = null;
        if (dialog.TransferRequested)
        {
            transfer = new PageOrganizationTransferDialog(this, ViewModel);
            if (transfer.ShowDialog() != true) return;
        }
        await RunAsync("Organizing pages…", async () =>
        {
            var remainingIds = dialog.EditedDocument.Pages.Select(page => page.Id).ToHashSet();
            if (before.Pages.Any(page => !remainingIds.Contains(page.Id)) && PreserveOrganizationDeletionAsync is { } preserve)
                await preserve(before, "Pages deleted in organizer");
            if (transfer is not null && !transfer.Copy && PreserveOrganizationDeletionAsync is { } preserveTransfer)
                await preserveTransfer(before, "Pages moved to another notebook");
            ViewModel.ApplyOrganization(dialog.EditedDocument);
            if (transfer is not null)
            {
                _lastPageTransfer = await ViewModel.TransferPagesAsync(dialog.TransferPageIds, transfer.NotebookId, transfer.SectionId, transfer.Copy);
                ViewModel.Status = "Pages transferred · Undo Last Page Transfer is available in More.";
            }
            else await ViewModel.Autosave.FlushAsync();
            ScrollToSelected();
        });
    }

    private async Task<BitmapSource?> RenderOrganizationThumbnailAsync(NotePage page)
    {
        var loads = new List<Task<AssetData>>();
        var editor = new PageEditor(page, id => { var task = ViewModel.Repository.GetAssetAsync(id); loads.Add(task); return task; });
        if (page.Pdf is not null) editor.SetPdfBackground(await ViewModel.Pdf.RenderAsync(page, .2));
        if (loads.Count > 0) await Task.WhenAll(loads);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        return DecodeBitmap(editor.CreateThumbnail(150));
    }

    private async void UndoPageTransferClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize) return;
        if (_lastPageTransfer is not { } receipt) { ViewModel.Status = "No page transfer is available to undo in this session."; return; }
        CommitEditors();
        await RunAsync("Undoing page transfer…", async () => { await ViewModel.UndoTransferAsync(receipt); _lastPageTransfer = null; ScrollToSelected(); });
    }
}
