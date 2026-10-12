using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;
using Moye.Services;

namespace Moye;

public partial class MainWindow
{
    private async void AddStudyQuestionClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || ViewModel.IsLibraryVisible) return;
        if (sender is MenuItem { Tag: PageActionTarget target } && !ActivatePageAction(target)) return;
        CommitEditors(); ClearTouches(); CloseSettingsPopups();
        if (ViewModel.Document is not { } document || ViewModel.SelectedPage is not { } selected) return;
        var page = selected.Page;
        var bounds = CurrentEditor?.SelectedContentBounds;
        if (bounds is null)
        {
            BitmapSource? preview = null;
            await RunAsync("Preparing question source…", async () => preview = await new PagePreviewRenderer(ViewModel.Pdf, ViewModel.Repository.GetAssetAsync).RenderAsync(page, 900));
            if (preview is null || _closing || !ReferenceEquals(ViewModel.Document, document) || !document.Pages.Contains(page)) return;
            var region = new PageRegionDialog(this, page, preview, "Mark the question source");
            if (region.ShowDialog() != true) return;
            bounds = region.Region;
        }
        var anchor = ToStudyAnchor(page, bounds);
        var dialog = new StudyQuestionDialog(this, new StudyQuestion { Anchor = anchor }, true);
        if (dialog.ShowDialog() == true && ReferenceEquals(ViewModel.Document, document)
            && ViewModel.AddStudyQuestion(page.Id, dialog.Question))
            ViewModel.Status = "Question added. Open Question inbox to keep its explanation and next attempt.";
    }

    private void SetResumeHintClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || ViewModel.IsLibraryVisible) return;
        if (sender is MenuItem { Tag: PageActionTarget target } && !ActivatePageAction(target)) return;
        CommitEditors(); ClearTouches(); CloseSettingsPopups();
        if (ViewModel.SelectedPage is not { } selected) return;
        var page = selected.Page;
        var dialog = new StudyResumeDialog(this, page.ResumeHint, ToStudyAnchor(page, CurrentEditor?.SelectedContentBounds));
        if (dialog.ShowDialog() == true && ViewModel.SetResumeHint(page.Id, dialog.Remove ? null : dialog.Hint))
            ViewModel.Status = dialog.Remove ? "Resume note cleared." : "Next step saved. Open Resume notes when you return.";
    }

    private void StudyInboxClick(object sender, RoutedEventArgs e) => ShowStudyInbox(false);
    private void ResumeStudyClick(object sender, RoutedEventArgs e) => ShowStudyInbox(true);
    private async void ShowStudyInbox(bool resumeOnly)
    {
        if (!CanOrganize || ViewModel.IsLibraryVisible || ViewModel.Document is not { } document) return;
        CommitEditors(); ClearTouches(); CloseSettingsPopups();
        var dialog = new StudyInboxDialog(this, ViewModel, resumeOnly);
        if (dialog.ShowDialog() != true || dialog.Result is not { } item) return;
        RememberOrganizationVisit();
        if (!await NavigateOrganizationAsync(document.Id, item.SectionId, item.PageId)) return;
        ViewModel.Status = item.Question is not null ? item.Detail + " · " + item.Heading : "Continue: " + item.Heading;
        if (item.Anchor is not { } anchor) return;
        await Dispatcher.InvokeAsync(() =>
        {
            if (ViewModel.SelectedPage is not { } page || page.Page.Id != item.PageId || CurrentEditor is not { } editor) return;
            RestorePageZoomAnchor(new(page, new Point(anchor.X + anchor.Width / 2, anchor.Y + anchor.Height / 2),
                new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2)));
            StudyAnchorAdorner.Show(editor, anchor);
        }, DispatcherPriority.Loaded);
    }

    private static StudyAnchor? ToStudyAnchor(NotePage page, Rect? bounds)
    {
        if (bounds is not { IsEmpty: false } rect) return null;
        rect.Intersect(new Rect(0, 0, page.Width, page.Height));
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return null;
        var anchor = new StudyAnchor { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height };
        return StudyWorkflowService.IsValidAnchor(page, anchor) ? anchor : null;
    }
}
