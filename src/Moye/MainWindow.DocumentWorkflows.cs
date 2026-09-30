using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Moye.Controls;
using Moye.Models;
using Moye.Services;

namespace Moye;

public partial class MainWindow
{
    private ContentControl? _referenceHost;
    private ReferencePane? _referencePane;

    private void InitializeDocumentWorkflows(ContentControl referenceHost)
    {
        _referenceHost = referenceHost;
        Closed += (_, _) => _referencePane?.Dispose();
    }

    private async Task<bool> RunDocumentOperationAsync(string title, Func<CancellationToken, IProgress<DocumentProgress>, Task> operation)
    {
        if (ViewModel.IsBusy) return false;
        CommitEditors(); CloseSettingsPopups(); ClearTouches();
        ViewModel.IsBusy = true; ViewModel.Operation = title;
        try
        {
            var completed = await DocumentOperationDialog.RunAsync(this, title, operation);
            if (!completed) ViewModel.Status = "Operation canceled · Your notebook is unchanged";
            return completed;
        }
        catch (Exception ex) { MessageBox.Show(this, _errors.Report(ex), "Unable to complete the operation", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        finally { ViewModel.IsBusy = false; }
    }

    private async void ImportDocumentWorkflowClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        var file = new OpenFileDialog { Filter = DocumentImportService.FileFilter, Title = "Choose a document to import" };
        if (file.ShowDialog(this) != true) return;
        CommitEditors();
        using var prepared = new PreparedDocumentImport();
        if (!await RunDocumentOperationAsync("Preparing document preview", (token, progress) => prepared.PrepareAsync(file.FileName, progress, token))) return;
        var renderer = new PagePreviewRenderer(prepared.Pdf, prepared.LoadAsset);
        var target = ViewModel.IsLibraryVisible ? null : ViewModel.Document;
        var dialog = new DocumentSelectionDialog(this, "Import document pages", prepared.Pages, renderer, target,
            ViewModel.SelectedPage?.Page.Id, ViewModel.SelectedSection?.Id, true, Path.GetFileNameWithoutExtension(file.FileName));
        if (dialog.ShowDialog() != true) return;
        var destination = dialog.Destination!;
        var pages = dialog.SelectedPages;
        var afterId = dialog.InsertAfterCurrent ? ViewModel.SelectedPage?.Page.Id : null;
        string? selectedId = null, sectionId = null;
        var succeeded = await RunDocumentOperationAsync("Importing selected pages", async (token, progress) =>
        {
            await prepared.CommitAssetsAsync(ViewModel.Repository, pages, progress, token);
            token.ThrowIfCancellationRequested();
            // Past this point the import is committed as one edit. Cancellation must
            // not report unchanged after pages have been accepted into the notebook.
            progress.Report(new("Saving imported pages…", CanCancel: false));
            if (destination.Kind == ImportDestinationKind.NewNotebook)
            {
                await ViewModel.Autosave.FlushAsync();
                var section = new NoteSection { Title = "General" };
                var document = new NotebookDocument { Title = dialog.NewTitle, Folder = target?.Folder ?? "My Notes", Sections = [section] };
                var inserted = DocumentWorkflows.InsertPages(document, pages, section.Id);
                await ViewModel.Repository.SaveAsync(document); await ViewModel.RefreshLibraryAsync();
                await ViewModel.OpenAsync(document.Id); selectedId = inserted.FirstOrDefault()?.Id; sectionId = section.Id;
                await PrepareNotebookViewAsync();
            }
            else
            {
                if (target is null || !ReferenceEquals(target, ViewModel.Document)) throw new InvalidOperationException("The destination notebook changed. Please import again.");
                sectionId = destination.SectionId;
                if (destination.Kind == ImportDestinationKind.NewSection)
                {
                    var section = new NoteSection { Title = dialog.NewTitle }; target.Sections.Add(section); sectionId = section.Id;
                }
                var inserted = DocumentWorkflows.InsertPages(target, pages, sectionId!, afterId);
                selectedId = inserted.FirstOrDefault()?.Id;
                ViewModel.Changed(true); await ViewModel.Autosave.FlushAsync();
            }
        });
        if (!succeeded) return;
        if (sectionId is not null) ViewModel.SelectedSection = ViewModel.Sections.FirstOrDefault(section => section.Id == sectionId);
        ViewModel.SelectedPage = ViewModel.Pages.FirstOrDefault(page => page.Page.Id == selectedId) ?? ViewModel.SelectedPage;
        SelectLibraryCurrent(); ScrollToSelected();
        ViewModel.Status = $"Imported {pages.Count} {(pages.Count == 1 ? "page" : "pages")}";
    }

    private async void ExportDocumentWorkflowClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || ViewModel.Document is null) return;
        CommitEditors();
        var source = ViewModel.Document.Snapshot();
        var dialog = new DocumentSelectionDialog(this, "Export PDF pages", source.Pages,
            new PagePreviewRenderer(ViewModel.Pdf, ViewModel.Repository.GetAssetAsync), source,
            ViewModel.SelectedPage?.Page.Id, ViewModel.SelectedSection?.Id, false);
        if (dialog.ShowDialog() != true) return;
        var selected = dialog.SelectedPages;
        var selectedSections = selected.Select(page => page.SectionId).Distinct().ToArray();
        var title = selectedSections.Length == 1 ? source.Title + " - " + source.Sections.First(section => section.Id == selectedSections[0]).Title : source.Title;
        var snapshot = DocumentWorkflows.ExportSnapshot(source, selected.Select(page => page.Id), title);
        var overflow = TextFlow.FindOverflow(snapshot.Pages);
        if (overflow.Count > 0)
        {
            var warning = new TextOverflowDialog(this, snapshot, overflow); warning.ShowDialog();
            if (warning.JumpTo is { } issue) { await JumpToOverflowTextAsync(issue.PageId, issue.TextId); return; }
            if (!warning.ContinueExport) return;
        }
        var file = new SaveFileDialog { Filter = "PDF documents|*.pdf", FileName = SafeFileName(snapshot.Title) + ".pdf", Title = $"Export {snapshot.Pages.Count} pages as PDF" };
        if (file.ShowDialog(this) != true) return;
        var completed = await RunDocumentOperationAsync("Exporting PDF", async (token, progress) =>
        {
            if (ViewModel.Pdf is PdfService pdf) await pdf.ExportAsync(file.FileName, snapshot, progress, token);
            else await ViewModel.Pdf.ExportAsync(file.FileName, snapshot, token);
        });
        if (!completed) return;
        ViewModel.Status = $"PDF exported · {snapshot.Pages.Count} pages · {Path.GetFileName(file.FileName)}";
        new DocumentCompletionDialog(this, file.FileName, snapshot.Pages.Count).ShowDialog();
    }

    private async Task JumpToOverflowTextAsync(string pageId, string textId)
    {
        var page = ViewModel.Document?.Pages.FirstOrDefault(page => page.Id == pageId);
        if (page is null) return;
        CommitEditors();
        ViewModel.SelectedSection = ViewModel.Sections.FirstOrDefault(section => section.Id == page.SectionId);
        ViewModel.SelectedPage = ViewModel.Pages.FirstOrDefault(item => item.Page.Id == pageId);
        SetTool(InkTool.Text); ScrollToSelected(); PageList.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        CurrentEditor?.BeginTypingText(textId); UpdateTextToolbar();
    }

    private async void ShowReferenceClick(object sender, RoutedEventArgs e)
    {
        if (_referenceHost is null || ViewModel.IsBusy) return;
        CommitEditors();
        if (_referencePane is null)
        {
            _referencePane = new ReferencePane(ViewModel.Repository, ViewModel.Pdf, () => ViewModel.Document);
            _referencePane.CloseRequested += (_, _) => { _referenceHost.Visibility = Visibility.Collapsed; PageList.Focus(); };
            _referencePane.ReturnToEditor += (_, _) => { PageList.Focus(); CurrentEditor?.Focus(); };
            _referenceHost.Content = _referencePane; _referenceHost.Visibility = Visibility.Visible;
            await _referencePane.InitializeAsync();
        }
        else _referenceHost.Visibility = _referenceHost.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }
}
