using L = Moye.Localization.LocalizationService;
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
        L.Bind(RecoveryAlertButton, ContentControl.ContentProperty, "Shell.RecoverDraftCount", count);
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
                progress?.Report(L.Text("Shell.ReadingBackupNotebook", i + 1, summaries.Count));
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
            ViewModel.SetStatus("Shell.AutomaticBackupFailed");
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
        var dialog = new SaveFileDialog { Filter = L.Text("Shell.BackupFileFilter"), FileName = (all ? "All Penroam Notebooks" : SafeFileName(ViewModel.Title)) + $"-{DateTime.Now:yyyyMMdd}.moye" };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync(L.M("Shell.CreatingEditableBackup"), async () =>
        {
            await SafetyProgressDialog.RunAsync(this, L.Text("Shell.CreateBackup"), async (progress, token) =>
            {
                var documents = await CaptureBackupDocumentsAsync(all, progress, token);
                progress.Report(L.Text("Shell.WritingBackupNotebooks", documents.Count));
                await ViewModel.Backup.ExportAsync(dialog.FileName, documents, token);
            });
            ViewModel.SetStatus("Shell.BackupSaved", Path.GetFileName(dialog.FileName));
            OfferOutputFolder(dialog.FileName);
        });
    }

    private static void OfferOutputFolder(string path)
    {
        if (MessageBox.Show(L.Text("Shell.ShowOutputFolderPrompt"), L.Text("Shell.Saved"), MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + Path.GetFullPath(path) + "\"") { UseShellExecute = true });
    }

    private async void RestoreSelectedClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = L.Text("Shell.BackupFileFilter"), Title = L.Text("Shell.ChooseBackupToRestore") };
        if (picker.ShowDialog(this) != true) return;
        CommitEditors();
        await RunAsync(L.M("Shell.InspectingBackup"), async () =>
        {
            IReadOnlyList<NotebookDocument> documents = [];
            await SafetyProgressDialog.RunAsync(this, L.Text("Shell.ValidateBackup"), async (progress, token) =>
            {
                progress.Report(L.Text("Shell.CheckingBackupIntegrity"));
                documents = await ViewModel.Backup.ImportAsync(picker.FileName, token);
            });
            var selected = ChooseRestoreDocuments(documents);
            if (selected.Count == 0) return;
            await ViewModel.Autosave.FlushAsync();
            await SaveRestoredDocumentsAsync(selected);
            ViewModel.SetStatus("Shell.NotebooksRestored", selected.Count);
        });
    }

    private IReadOnlyList<NotebookDocument> ChooseRestoreDocuments(IReadOnlyList<NotebookDocument> documents)
    {
        var window = CreateLocalizedSafetyWindow("Shell.ChooseRestoreNotebooks", 640, 540);
        var layout = new DockPanel { Margin = new Thickness(24) };
        var instruction = CreateLocalizedSafetyText("Shell.ChooseRestoreDescription");
        DockPanel.SetDock(instruction, Dock.Top); layout.Children.Add(instruction);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        var boxes = documents.Select(document =>
        {
            var box = new CheckBox { Tag = document, IsChecked = true, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
            L.Bind(box, ContentControl.ContentProperty, document.Pages.Count == 1 ? "Shell.RestoreNotebookChoiceOne" : "Shell.RestoreNotebookChoice", document.Title, document.Pages.Count);
            return box;
        }).ToArray();
        actions.Children.Add(CreateLocalizedSafetyButton("Shell.SelectAll", (_, _) => { foreach (var box in boxes) box.IsChecked = true; }));
        actions.Children.Add(CreateLocalizedSafetyButton("Shell.Clear", (_, _) => { foreach (var box in boxes) box.IsChecked = false; }));
        actions.Children.Add(CreateLocalizedSafetyButton("Shell.RestoreSelected", (_, _) => window.DialogResult = true));
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
        var window = CreateLocalizedSafetyWindow("Shell.SafetyCenter", 760, 660);
        var layout = new DockPanel { Margin = new Thickness(22) };
        var summary = CreateLocalizedSafetyText("Shell.SafetySummary",
            L.M("Shell.LastAutomaticBackup", _workspace.LastBackupUtc is { } lastBackup ? (object)lastBackup.ToLocalTime().ToString("g") : L.M("Shell.NeverBackedUp")),
            _workspace.LastBackupError.Length > 0 ? L.M("Shell.BackupError", _workspace.LastBackupError) : L.M(_workspace.BackupEnabled ? "Shell.AutomaticBackupsEnabled" : "Shell.AutomaticBackupsDisabled"),
            _draftRecovery.LastError is { } error ? (object)L.M("Shell.DraftRecoveryError", error) : "");
        DockPanel.SetDock(summary, Dock.Top); layout.Children.Add(summary);
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Top); layout.Children.Add(actions);
        actions.Children.Add(CreateLocalizedSafetyButton("Shell.BackupSettings", (_, _) => { window.Close(); WorkspaceSettingsClick(this, new()); }));
        actions.Children.Add(CreateLocalizedSafetyButton("Shell.BackupAllNow", async (_, _) => { window.Close(); await BackupWithProgressAsync(true); }));
        actions.Children.Add(CreateLocalizedSafetyButton("Shell.RestoreBackupAction", (_, _) => { window.Close(); RestoreSelectedClick(this, new()); }));
        var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 0) }; layout.Children.Add(tabs);
        var draftPanel = new DockPanel { Margin = new Thickness(12) };
        var draftNote = CreateLocalizedSafetyText("Shell.DraftRecoveryDescription");
        DockPanel.SetDock(draftNote, Dock.Top); draftPanel.Children.Add(draftNote);
        var draftList = CreateSafetyList(drafts.Select(file => new SafetyListItem(L.M("Shell.RecoveryDraftItem", file.Draft.Document.Title, file.Draft.CapturedUtc.ToLocalTime()), file)).ToArray());
        var draftActions = new WrapPanel(); DockPanel.SetDock(draftActions, Dock.Bottom); draftPanel.Children.Add(draftActions);
        draftActions.Children.Add(CreateLocalizedSafetyButton("Shell.RecoverDraftAsCopy", async (_, _) =>
        {
            if (draftList.SelectedItem is not SafetyListItem { Value: Services.RecoveryDraftFile file }) return;
            window.Close();
            await RunAsync(L.M("Shell.RecoveringDraft"), async () =>
            {
                var copy = MakeRecoveryCopy(file.Draft.Document);
                foreach (var assetId in copy.Pages.SelectMany(page => page.Images.Select(image => image.AssetId).Concat(page.Pdf is { } pdf ? [pdf.AssetId] : Array.Empty<string>())).Distinct())
                    await ViewModel.Repository.GetAssetAsync(assetId);
                await ViewModel.Repository.SaveAsync(copy); await _draftRecovery.ForgetAsync(file);
                await ViewModel.RefreshLibraryAsync(); await RefreshRecoveryNoticeAsync(); ViewModel.SetStatus("Shell.DraftRecovered");
            });
        }));
        draftActions.Children.Add(CreateLocalizedSafetyButton("Shell.DiscardSelectedDraft", async (_, _) =>
        {
            if (draftList.SelectedItem is not SafetyListItem { Value: Services.RecoveryDraftFile file }) return;
            if (MessageBox.Show(window, L.Text("Shell.DiscardDraftConfirm"), L.Text("Shell.DiscardDraftTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try { await _draftRecovery.ForgetAsync(file); window.Close(); SafetyCenterClick(this, new()); }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, L.Text("Shell.DraftKept")); }
        }));
        draftPanel.Children.Add(draftList);
        var draftTab = new TabItem { Content = draftPanel, MinHeight = 44 };
        L.Bind(draftTab, HeaderedContentControl.HeaderProperty, "Shell.InterruptedDrafts", drafts.Count);
        tabs.Items.Add(draftTab);
        var deletedPanel = new DockPanel { Margin = new Thickness(12) };
        var deletedNote = CreateLocalizedSafetyText("Shell.DeletedRecoveryDescription");
        DockPanel.SetDock(deletedNote, Dock.Top); deletedPanel.Children.Add(deletedNote);
        var deletedList = CreateSafetyList(deleted.Select(entry => new SafetyListItem(L.M(entry.PageCount == 1 ? "Shell.DeletedSnapshotItemOne" : "Shell.DeletedSnapshotItem", entry.Title, LocalizedDeletionReason(entry.Reason), entry.PageCount, entry.DeletedUtc.ToLocalTime()), entry)).ToArray());
        var deletedActions = new WrapPanel(); DockPanel.SetDock(deletedActions, Dock.Bottom); deletedPanel.Children.Add(deletedActions);
        deletedActions.Children.Add(CreateLocalizedSafetyButton("Shell.RestoreDeletedAsCopy", async (_, _) =>
        {
            if (deletedList.SelectedItem is not SafetyListItem { Value: Services.DeletedSnapshot entry }) return;
            window.Close(); await RunAsync(L.M("Shell.RestoringDeletedContent"), async () =>
            {
                var documents = await _localSafety.RestoreAsync(entry); await SaveRestoredDocumentsAsync(documents);
                ViewModel.SetStatus("Shell.DeletedContentRestored");
            });
        }));
        deletedActions.Children.Add(CreateLocalizedSafetyButton("Shell.DeletePermanently", (_, _) =>
        {
            if (deletedList.SelectedItem is not SafetyListItem { Value: Services.DeletedSnapshot entry }) return;
            if (MessageBox.Show(window, L.Text("Shell.DeleteRecoveryCopyConfirm"), L.Text("Shell.DeleteRecoveryCopyTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _localSafety.Delete(entry); window.Close(); SafetyCenterClick(this, new());
        }));
        deletedPanel.Children.Add(deletedList);
        var deletedTab = new TabItem { Content = deletedPanel, MinHeight = 44 };
        L.Bind(deletedTab, HeaderedContentControl.HeaderProperty, "Shell.RecentlyDeleted", deleted.Count);
        tabs.Items.Add(deletedTab);
        window.Content = layout; window.ShowDialog(); await RefreshRecoveryNoticeAsync();
    }

    private Window CreateLocalizedSafetyWindow(string key, double width, double height)
    {
        var window = WorkspaceDialogUi.Window(this, "", width, height);
        L.Bind(window, Window.TitleProperty, key);
        return window;
    }

    private static TextBlock CreateLocalizedSafetyText(string key, params object?[] args)
    {
        var text = WorkspaceDialogUi.Text("");
        L.Bind(text, TextBlock.TextProperty, key, args);
        return text;
    }

    private static Button CreateLocalizedSafetyButton(string key, RoutedEventHandler action)
    {
        var button = WorkspaceDialogUi.Button("", action);
        L.Bind(button, ContentControl.ContentProperty, key);
        return button;
    }

    private static ListBox CreateSafetyList(IEnumerable<SafetyListItem> items)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, L.FormatBinding("Shell.SafetyItemLabel", new System.Windows.Data.Binding(nameof(SafetyListItem.Label))));
        return new ListBox { ItemsSource = items, ItemTemplate = new DataTemplate { VisualTree = text } };
    }

    private static object LocalizedDeletionReason(string reason) => reason switch
    {
        // Legacy reason values remain unchanged on disk; only their presentation is localized.
        "Notebook deleted" => L.M("Shell.DeletionNotebook"),
        "Page deleted" => L.M("Shell.DeletionPage"),
        "Section deleted" => L.M("Shell.DeletionSection"),
        "Pages deleted in organizer" => L.M("Shell.DeletionOrganizer"),
        "Pages moved to another notebook" => L.M("Shell.DeletionTransfer"),
        _ => reason
    };

    private sealed record SafetyListItem(Moye.Localization.LocalizedMessage Label, object Value);

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
