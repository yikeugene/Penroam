using L = Moye.Localization.LocalizationService;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Moye.Controls;
using Moye.Models;
using Moye.Services;

namespace Moye;

public partial class MainWindow
{
    private PagePreviewRenderer InsightRenderer() => new(ViewModel.Pdf, ViewModel.Repository.GetAssetAsync);

    private async void VisualSearchClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || ViewModel.Document is not { } document) return;
        CommitEditors(); ClearTouches();
        var dialog = new VisualNoteSearchDialog(this, document, InsightRenderer());
        if (dialog.ShowDialog() == true && dialog.Result is { } result)
        {
            RememberOrganizationVisit();
            await NavigateOrganizationAsync(document.Id, result.SectionId, result.PageId);
        }
    }

    private async void RestoreRegionClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || ViewModel.Document is not { } document || OrganizationPageTarget(sender) is not { } page) return;
        CommitEditors(); ClearTouches();
        var versions = ViewModel.RegionHistory(page.Id);
        if (versions.Count == 0) { ViewModel.SetStatus("Shell.NoCompatibleRegionHistory"); return; }
        var picker = new RegionHistoryDialog(this, versions, InsightRenderer());
        if (picker.ShowDialog() != true || picker.Result is not { } previous) return;
        BitmapSource? preview = null;
        await RunAsync(L.M("Shell.PreparingEarlierPage"), async () => preview = await InsightRenderer().RenderAsync(previous, 900));
        if (preview is null || !ReferenceEquals(document, ViewModel.Document)) return;
        var region = new PageRegionDialog(this, previous, preview, L.Text("Shell.ChooseRestoreRegion"));
        if (region.ShowDialog() != true || !ReferenceEquals(document, ViewModel.Document)) return;
        try
        {
            ViewModel.SetStatus(ViewModel.RestoreRegion(page.Id, previous, region.Region) ? "Shell.RegionRestored" : "Shell.RegionUnchanged");
            ScrollToSelected();
        }
        catch (Exception ex) { ViewModel.SetStatus("Shell.RegionRestoreFailed", ex.Message); }
    }

    private async void NewPracticeClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || ViewModel.Document is not { } document || OrganizationPageTarget(sender) is not { } source) return;
        CommitEditors(); ClearTouches();
        if (source.Practice is { } existing)
        {
            await RepeatPracticeAsync(document, source, existing); return;
        }
        BitmapSource? preview = null;
        await RunAsync(L.M("Shell.PreparingPracticeQuestion"), async () => preview = await InsightRenderer().RenderAsync(source, 1600));
        if (preview is null || !ReferenceEquals(document, ViewModel.Document)) return;
        var region = new PageRegionDialog(this, source, preview, L.Text("Shell.SelectPracticeQuestion"), CurrentEditor?.Page.Id == source.Id ? CurrentEditor.SelectedContentBounds : null);
        if (region.ShowDialog() != true) return;
        var name = new InputDialog(this, L.Text("Shell.NamePracticeQuestion"), (L.Text("Shell.Question"), source.Title.Length > 0 ? source.Title : "Practice question"));
        if (name.ShowDialog() != true || !ReferenceEquals(document, ViewModel.Document)) return;
        var title = name.Values[0].Trim(); if (title.Length == 0) title = "Practice question"; if (title.Length > 1000) title = title[..1000];
        var crop = region.Region;
        int left = Math.Clamp((int)Math.Floor(crop.X / source.Width * preview.PixelWidth), 0, preview.PixelWidth - 1);
        int top = Math.Clamp((int)Math.Floor(crop.Y / source.Height * preview.PixelHeight), 0, preview.PixelHeight - 1);
        int right = Math.Clamp((int)Math.Ceiling(crop.Right / source.Width * preview.PixelWidth), left + 1, preview.PixelWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(crop.Bottom / source.Height * preview.PixelHeight), top + 1, preview.PixelHeight);
        var bitmap = new CroppedBitmap(preview, new Int32Rect(left, top, right - left, bottom - top)); bitmap.Freeze();
        using var output = new MemoryStream(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(output);
        AssetData? asset = null;
        await RunAsync(L.M("Shell.SavingPracticeQuestion"), async () => asset = await ViewModel.Repository.PutAssetAsync("practice-question.png", "image/png", output.ToArray()));
        if (asset is not null && ReferenceEquals(document, ViewModel.Document) && ViewModel.AddPracticeAttempt(source.Id, asset.Id, crop.Width, crop.Height, title))
        { ScrollToSelected(); SetTool(InkTool.Pen); ViewModel.SetStatus("Shell.PracticeReady"); }
    }

    private async Task RepeatPracticeAsync(NotebookDocument document, NotePage source, PracticeAttempt attempt)
    {
        AssetData? asset = null; double width = 0, height = 0;
        await RunAsync(L.M("Shell.OpeningPracticeQuestion"), async () =>
        {
            asset = await ViewModel.Repository.GetAssetAsync(attempt.QuestionAssetId);
            using var stream = new MemoryStream(asset.Bytes, false);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            width = attempt.QuestionWidth; height = attempt.QuestionHeight;
        });
        if (asset is not null && ReferenceEquals(document, ViewModel.Document) && ViewModel.AddPracticeAttempt(source.Id, asset.Id, width, height, attempt.Question, attempt.GroupId))
        { ScrollToSelected(); SetTool(InkTool.Pen); ViewModel.SetStatus("Shell.RepeatPracticeReady"); }
    }

    private async void PracticeAttemptsClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || ViewModel.Document is not { } document) return;
        CommitEditors(); ClearTouches();
        if (!document.Pages.Any(p => p.Practice is not null)) { ViewModel.SetStatus("Shell.PracticeEmptyHint"); return; }
        var dialog = new PracticeAttemptsDialog(this, document.Pages, ViewModel.SelectedPage?.Page.Id);
        if (dialog.ShowDialog() != true || dialog.SelectedPage is not { Practice: { } attempt } selected || !ReferenceEquals(document, ViewModel.Document)) return;
        foreach (var change in dialog.Changes) ViewModel.SetPracticeReflection(change.Key, change.Value.Reflection, change.Value.Reviewed);
        if (dialog.Action == "new") { await RepeatPracticeAsync(document, selected, attempt); return; }
        var target = dialog.Action == "source" ? document.Pages.FirstOrDefault(p => p.Id == attempt.SourcePageId) : selected;
        if (target is null) { ViewModel.SetStatus("Shell.PracticeSourceMissing"); return; }
        RememberOrganizationVisit(); await NavigateOrganizationAsync(document.Id, target.SectionId, target.Id);
    }
}
