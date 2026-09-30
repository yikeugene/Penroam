using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Moye.Controls;
using Moye.Models;

namespace Moye;

public partial class MainWindow
{
    private async Task PreserveDeletedAsync(NotebookDocument document, string reason)
    {
        if (!_workspaceLoaded || _localSafety is null) return;
        await _localSafety.PreserveAsync(document, reason);
    }

    private async Task RefreshRecoveryNoticeAsync()
    {
        if (_draftRecovery is null) return;
        var count = (await _draftRecovery.ReadAsync()).Count;
        RecoveryAlertButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecoveryAlertButton.Content = $"Recover drafts ({count})";
    }

    private async Task<List<NotebookDocument>> CaptureBackupDocumentsAsync(bool all, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        // Capture live/pending state before awaiting repository reads.
        var overlay = ViewModel.Autosave.PendingDocuments.ToDictionary(document => document.Id, document => document.Snapshot());
        if (ViewModel.Document is { } current) overlay[current.Id] = current.Snapshot();
        var documents = new List<NotebookDocument>();
        if (all)
        {
            var summaries = await ViewModel.Repository.ListAsync();
            for (var i = 0; i < summaries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Reading notebook {i + 1} of {summaries.Count}…");
                if (!overlay.ContainsKey(summaries[i].Id) && await ViewModel.Repository.LoadAsync(summaries[i].Id) is { } saved) documents.Add(saved);
            }
            documents.AddRange(overlay.Values);
        }
        else if (ViewModel.Document is { } selected && overlay.TryGetValue(selected.Id, out var snapshot)) documents.Add(snapshot);
        return documents;
    }

    private async Task TryScheduledBackupAsync(bool force = false)
    {
        if (!_workspaceLoaded || _scheduledBackups is null || _backupRunning || _closing || ViewModel.IsBusy || AnyPenDown ||
            (!force && (DateTimeOffset.UtcNow < _nextBackupAttempt || !Services.ScheduledBackupService.IsDue(_workspace, DateTimeOffset.UtcNow)))) return;
        if (string.IsNullOrWhiteSpace(_workspace.BackupDirectory)) return;
        _backupRunning = true;
        _automaticBackupCancellation = new();
        _automaticBackupCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = _automaticBackupCancellation.Token;
        try
        {
            CommitEditors();
            var documents = await CaptureBackupDocumentsAsync(true, cancellationToken: token);
            if (documents.Count == 0) return;
            await _scheduledBackups.CreateAsync(_workspace.BackupDirectory, documents, _workspace.BackupRetention, token);
            _workspace.LastBackupUtc = DateTimeOffset.UtcNow; _workspace.LastBackupError = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _workspace.LastBackupError = ex.Message;
            _nextBackupAttempt = DateTimeOffset.UtcNow.AddMinutes(15);
            ViewModel.Status = "Automatic backup failed · Open Backup & recovery";
        }
        finally
        {
            await SaveWorkspaceAsync();
            _automaticBackupCancellation.Dispose(); _automaticBackupCancellation = null;
            _automaticBackupCompletion.TrySetResult();
            _backupRunning = false;
        }
    }

    private async Task BackupWithProgressAsync(bool all)
    {
        if (!all && ViewModel.Document is null) return;
        CommitEditors();
        var dialog = new SaveFileDialog { Filter = "Penroam backups|*.moye", FileName = (all ? "All Penroam Notebooks" : SafeFileName(ViewModel.Title)) + $"-{DateTime.Now:yyyyMMdd}.moye" };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync("Creating editable backup…", async () =>
        {
            await SafetyProgressDialog.RunAsync(this, "Create backup", async (progress, token) =>
            {
                var documents = await CaptureBackupDocumentsAsync(all, progress, token);
                progress.Report($"Writing {documents.Count} notebook(s) and their attachments…");
                await ViewModel.Backup.ExportAsync(dialog.FileName, documents, token);
            });
            ViewModel.Status = "Backup saved · " + Path.GetFileName(dialog.FileName);
            OfferOutputFolder(dialog.FileName);
        });
    }

    private static void OfferOutputFolder(string path)
    {
        if (MessageBox.Show("The file is ready. Show it in its folder?", "Saved", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + Path.GetFullPath(path) + "\"") { UseShellExecute = true });
    }

    private async void RestoreSelectedClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Penroam backups|*.moye", Title = "Choose notebooks to restore as new copies" };
        if (picker.ShowDialog(this) != true) return;
        CommitEditors();
        await RunAsync("Inspecting backup…", async () =>
        {
            IReadOnlyList<NotebookDocument> documents = [];
            await SafetyProgressDialog.RunAsync(this, "Validate backup", async (progress, token) =>
            {
                progress.Report("Checking notebook data and attachment integrity…");
                documents = await ViewModel.Backup.ImportAsync(picker.FileName, token);
            });
            var selected = ChooseRestoreDocuments(documents);
            if (selected.Count == 0) return;
            await ViewModel.Autosave.FlushAsync();
            await SaveRestoredDocumentsAsync(selected);
            ViewModel.Status = $"Restored {selected.Count} notebook(s) as new copies";
        });
    }

    private IReadOnlyList<NotebookDocument> ChooseRestoreDocuments(IReadOnlyList<NotebookDocument> documents)
    {
        var window = WorkspaceDialogUi.Window(this, "Choose notebooks to restore", 640, 540);
        var layout = new DockPanel { Margin = new Thickness(24) };
        var instruction = WorkspaceDialogUi.Text("Select the notebooks to restore. Existing notebooks are kept; each selection becomes a new copy.");
        DockPanel.SetDock(instruction, Dock.Top); layout.Children.Add(instruction);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        var boxes = documents.Select(document => new CheckBox { Content = $"{document.Title} · {document.Pages.Count} pages", Tag = document, IsChecked = true, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center }).ToArray();
        actions.Children.Add(WorkspaceDialogUi.Button("Select all", (_, _) => { foreach (var box in boxes) box.IsChecked = true; }));
        actions.Children.Add(WorkspaceDialogUi.Button("Clear", (_, _) => { foreach (var box in boxes) box.IsChecked = false; }));
        actions.Children.Add(WorkspaceDialogUi.Button("Restore selected", (_, _) => window.DialogResult = true));
        var stack = new StackPanel(); foreach (var box in boxes) stack.Children.Add(box);
        layout.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = layout;
        return window.ShowDialog() == true ? boxes.Where(box => box.IsChecked == true).Select(box => (NotebookDocument)box.Tag).ToArray() : [];
    }

    private async Task SaveRestoredDocumentsAsync(IReadOnlyList<NotebookDocument> documents)
    {
        if (ViewModel.Repository is Services.SqliteNotebookRepository sqlite) await sqlite.SaveBatchAsync(documents);
        else foreach (var document in documents) await ViewModel.Repository.SaveAsync(document);
        await ViewModel.RefreshLibraryAsync();
    }

    private async void SafetyCenterClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || _draftRecovery is null || _localSafety is null) return;
        CommitEditors(); ClearTouches();
        var drafts = await _draftRecovery.ReadAsync();
        var deleted = await _localSafety.ListAsync();
        var window = WorkspaceDialogUi.Window(this, "Backup & recovery", 760, 660);
        var layout = new DockPanel { Margin = new Thickness(22) };
        var summary = WorkspaceDialogUi.Text($"Last automatic backup: {(_workspace.LastBackupUtc?.ToLocalTime().ToString("g") ?? "Never")}\n" +
            (_workspace.LastBackupError.Length > 0 ? "Backup error: " + _workspace.LastBackupError : _workspace.BackupEnabled ? "Automatic backups run while Penroam is open." : "Automatic backups are off.") +
            (_draftRecovery.LastError is { } error ? "\nDraft recovery: " + error : ""));
        DockPanel.SetDock(summary, Dock.Top); layout.Children.Add(summary);
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Top); layout.Children.Add(actions);
        actions.Children.Add(WorkspaceDialogUi.Button("Backup settings", (_, _) => { window.Close(); WorkspaceSettingsClick(this, new()); }));
        actions.Children.Add(WorkspaceDialogUi.Button("Back up all now", async (_, _) => { window.Close(); await BackupWithProgressAsync(true); }));
        actions.Children.Add(WorkspaceDialogUi.Button("Restore a backup…", (_, _) => { window.Close(); RestoreSelectedClick(this, new()); }));
        var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 0) }; layout.Children.Add(tabs);
        var draftPanel = new DockPanel { Margin = new Thickness(12) };
        var draftNote = WorkspaceDialogUi.Text("Pending snapshots may contain edits from an interrupted session. Recovering creates a new notebook so saved notes are never overwritten.");
        DockPanel.SetDock(draftNote, Dock.Top); draftPanel.Children.Add(draftNote);
        var draftList = new ListBox { ItemsSource = drafts.Select(file => new SafetyListItem($"{file.Draft.Document.Title} · {file.Draft.CapturedUtc.ToLocalTime():g}", file)).ToArray(), DisplayMemberPath = "Label" };
        var draftActions = new WrapPanel(); DockPanel.SetDock(draftActions, Dock.Bottom); draftPanel.Children.Add(draftActions);
        draftActions.Children.Add(WorkspaceDialogUi.Button("Recover selected as copy", async (_, _) =>
        {
            if (draftList.SelectedItem is not SafetyListItem { Value: Services.RecoveryDraftFile file }) return;
            window.Close();
            await RunAsync("Recovering draft…", async () =>
            {
                var copy = MakeRecoveryCopy(file.Draft.Document);
                foreach (var assetId in copy.Pages.SelectMany(page => page.Images.Select(image => image.AssetId).Concat(page.Pdf is { } pdf ? [pdf.AssetId] : Array.Empty<string>())).Distinct())
                    await ViewModel.Repository.GetAssetAsync(assetId);
                await ViewModel.Repository.SaveAsync(copy); await _draftRecovery.ForgetAsync(file);
                await ViewModel.RefreshLibraryAsync(); await RefreshRecoveryNoticeAsync(); ViewModel.Status = "Draft recovered as a new notebook";
            });
        }));
        draftActions.Children.Add(WorkspaceDialogUi.Button("Discard selected…", async (_, _) =>
        {
            if (draftList.SelectedItem is not SafetyListItem { Value: Services.RecoveryDraftFile file }) return;
            if (MessageBox.Show(window, "Permanently discard this recovery draft?", "Discard draft", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try { await _draftRecovery.ForgetAsync(file); window.Close(); SafetyCenterClick(this, new()); }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Draft kept"); }
        }));
        draftPanel.Children.Add(draftList); tabs.Items.Add(new TabItem { Header = $"Interrupted drafts ({drafts.Count})", Content = draftPanel, MinHeight = 44 });
        var deletedPanel = new DockPanel { Margin = new Thickness(12) };
        var deletedNote = WorkspaceDialogUi.Text("Before deleting a notebook, section or pages, Penroam keeps the complete notebook for 30 days. Restore it as a new copy, then move back any pages you need.");
        DockPanel.SetDock(deletedNote, Dock.Top); deletedPanel.Children.Add(deletedNote);
        var deletedList = new ListBox { ItemsSource = deleted, DisplayMemberPath = "Label" };
        var deletedActions = new WrapPanel(); DockPanel.SetDock(deletedActions, Dock.Bottom); deletedPanel.Children.Add(deletedActions);
        deletedActions.Children.Add(WorkspaceDialogUi.Button("Restore selected as copy", async (_, _) =>
        {
            if (deletedList.SelectedItem is not Services.DeletedSnapshot entry) return;
            window.Close(); await RunAsync("Restoring deleted content…", async () =>
            {
                var documents = await _localSafety.RestoreAsync(entry); await SaveRestoredDocumentsAsync(documents);
                ViewModel.Status = "Deleted content restored as a new notebook";
            });
        }));
        deletedActions.Children.Add(WorkspaceDialogUi.Button("Delete permanently…", (_, _) =>
        {
            if (deletedList.SelectedItem is not Services.DeletedSnapshot entry) return;
            if (MessageBox.Show(window, "Permanently remove this recovery copy?", "Delete recovery copy", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _localSafety.Delete(entry); window.Close(); SafetyCenterClick(this, new());
        }));
        deletedPanel.Children.Add(deletedList); tabs.Items.Add(new TabItem { Header = $"Recently deleted ({deleted.Count})", Content = deletedPanel, MinHeight = 44 });
        window.Content = layout; window.ShowDialog(); await RefreshRecoveryNoticeAsync();
    }

    private sealed record SafetyListItem(string Label, object Value);

    internal static NotebookDocument MakeRecoveryCopy(NotebookDocument document)
    {
        var copy = document.Snapshot(); copy.Id = Guid.NewGuid().ToString("N"); copy.Title += " (recovered copy)";
        copy.CreatedUtc = copy.ModifiedUtc = DateTimeOffset.UtcNow; copy.IsQuickInbox = false;
        NotebookStructure.Normalize(copy);
        var sectionIds = copy.Sections.ToDictionary(section => section.Id, _ => Guid.NewGuid().ToString("N"));
        copy.Sections = copy.Sections.Select(section => section with { Id = sectionIds[section.Id] }).ToList();
        foreach (var page in copy.Pages)
        {
            page.Id = Guid.NewGuid().ToString("N"); page.SectionId = sectionIds[page.SectionId];
            page.Texts = page.Texts.Select(text => text with { Id = Guid.NewGuid().ToString("N") }).ToList();
            page.Images = page.Images.Select(image => image with { Id = Guid.NewGuid().ToString("N") }).ToList();
        }
        return copy;
    }
}
