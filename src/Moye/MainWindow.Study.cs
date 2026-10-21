using L = Moye.Localization.LocalizationService;
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
    private void AddStudyQuestionClick(object sender, RoutedEventArgs e)
    {
        if (CapturePageActionTarget(sender) is { } target) AddStudyQuestion(target);
    }

    private async void AddStudyQuestion(PageActionTarget target)
    {
        if (!CanOrganize || !ActivatePageAction(target)) return;
        CommitEditors(); ClearTouches(); CloseSettingsPopups();
        if (ViewModel.Document is not { } document || ViewModel.SelectedPage is not { } selected) return;
        var page = selected.Page;
        var bounds = CurrentEditor?.SelectedContentBounds;
        if (bounds is null)
        {
            BitmapSource? preview = null;
            await RunAsync(L.M("Shell.PreparingQuestionSource"), async () => preview = await new PagePreviewRenderer(ViewModel.Pdf, ViewModel.Repository.GetAssetAsync).RenderAsync(page, 900));
            if (preview is null || _closing || !ReferenceEquals(ViewModel.Document, document) || !document.Pages.Contains(page)) return;
            var region = new PageRegionDialog(this, page, preview, L.Text("Shell.MarkQuestionSource"));
            if (region.ShowDialog() != true) return;
            bounds = region.Region;
        }
        var anchor = ToStudyAnchor(page, bounds);
        var dialog = new StudyQuestionDialog(this, new StudyQuestion { Anchor = anchor }, true);
        if (dialog.ShowDialog() == true && ResolvePageActionTarget(ViewModel, target) is not null
            && ViewModel.AddStudyQuestion(page.Id, dialog.Question))
            ViewModel.SetStatus("Shell.QuestionAdded");
    }

    private void SetResumeHintClick(object sender, RoutedEventArgs e)
    {
        if (CapturePageActionTarget(sender) is { } target) SetResumeHint(target);
    }

    private void SetResumeHint(PageActionTarget target)
    {
        if (!CanOrganize || !ActivatePageAction(target)) return;
        CommitEditors(); ClearTouches(); CloseSettingsPopups();
        if (ResolvePageActionTarget(ViewModel, target)?.Page is not { } page) return;
        var dialog = new StudyResumeDialog(this, page.ResumeHint, ToStudyAnchor(page, CurrentEditor?.SelectedContentBounds));
        if (dialog.ShowDialog() == true && ResolvePageActionTarget(ViewModel, target) is not null &&
            ViewModel.SetResumeHint(page.Id, dialog.Remove ? null : dialog.Hint))
            ViewModel.SetStatus(dialog.Remove ? "Shell.ResumeNoteCleared" : "Shell.ResumeNoteSaved");
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
        if (item.Question is not null) ViewModel.SetStatus("Shell.StudyQuestionStatus", (object?)item.LocalizedDetail ?? item.Detail, item.Heading);
        else ViewModel.SetStatus("Shell.ContinueStudy", item.Heading);
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
