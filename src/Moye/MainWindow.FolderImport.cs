using L = Moye.Localization.LocalizationService;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using Moye.Controls;
using Moye.Services;

namespace Moye;

public partial class MainWindow
{
    private bool _folderImportActive;
    private bool CanImportFolder => !_closing && !_folderImportActive && !ViewModel.IsBusy && ViewModel.IsLibraryVisible;

    private async void ImportFolderClick(object sender, RoutedEventArgs e)
    {
        await ImportFolderAsync(() =>
        {
            var picker = new OpenFolderDialog { Title = L.Text("Shell.ChooseImportFolder"), Multiselect = false };
            return picker.ShowDialog(this) == true ? picker.FolderName : null;
        });
    }

    private void LibraryFolderDragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanImportFolder && (e.AllowedEffects & DragDropEffects.Copy) != 0 && TryGetSingleImportFolder(e, out _)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void LibraryFolderDrop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None; e.Handled = true;
        if (!CanImportFolder || (e.AllowedEffects & DragDropEffects.Copy) == 0 || !TryGetSingleImportFolder(e, out var folder)) return;
        e.Effects = DragDropEffects.Copy;
        await ImportFolderAsync(() => folder);
    }

    private static bool TryGetSingleImportFolder(DragEventArgs e, out string folder)
    {
        folder = "";
        try
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop, false) ||
                e.Data.GetData(DataFormats.FileDrop, false) is not string[] { Length: 1 } paths || !Directory.Exists(paths[0])) return false;
            folder = paths[0]; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        { return false; }
    }

    private async Task ImportFolderAsync(Func<string?> selectFolder)
    {
        if (!CanImportFolder) return;
        _folderImportActive = true;
        ViewModel.IsBusy = true; ViewModel.SetOperation("Shell.ImportingFolder");
        PreparedFolderImport? prepared = null;
        try
        {
            CommitEditors(); CloseSettingsPopups(); ClearTouches();
            var folder = selectFolder();
            if (folder is null) return;
            // Keep the feature guard and busy state across picker, progress, summary and retries.
            // The single-file helper releases busy and reports "unchanged", which is unsafe after our save boundary.
            var completed = await DocumentOperationDialog.RunAsync(this, L.Text("Shell.PreparingFolderImport"), async (token, progress) =>
            {
                prepared = await new FolderImportService().PrepareAsync(folder, progress, token);
            });
            if (!completed || prepared is null)
            {
                ViewModel.SetStatus("Shell.FolderImportCanceled");
                return;
            }
            if (prepared.Summary.ReadyCount == 0)
            {
                new FolderImportSummaryDialog(this, prepared.Summary).ShowDialog();
                ViewModel.SetStatus("Shell.FolderImportEmpty");
                return;
            }
            if (prepared.Summary.RequiresConfirmation)
            {
                var summary = new FolderImportSummaryDialog(this, prepared.Summary);
                if (summary.ShowDialog() != true || !summary.ImportSuccessfulFiles)
                {
                    ViewModel.SetStatus("Shell.FolderImportCanceled");
                    return;
                }
                prepared.AcceptSuccessfulFiles();
            }
            while (!prepared.HasSaved)
            {
                try
                {
                    completed = await DocumentOperationDialog.RunAsync(this, L.Text("Shell.SavingImportedNotebook"), (token, progress) =>
                        prepared.SaveAsync(ViewModel.Repository, async () =>
                        {
                            CommitEditors();
                            await ViewModel.Autosave.FlushAsync();
                        }, progress, token));
                    if (!completed && !prepared.HasSaved)
                    {
                        ViewModel.SetStatus("Shell.FolderImportCanceled");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (ConfirmFolderImportRetry(_errors.Report(ex), false)) continue;
                    ViewModel.SetStatus("Shell.FolderImportNotSaved");
                    return;
                }
            }
            // From here on, retries only refresh/open the already saved ID, never reimport or resave.
            while (true)
            {
                try
                {
                    ViewModel.SetOperation("Shell.OpeningImportedNotebook");
                    await prepared.OpenSavedAsync(async id =>
                    {
                        ViewModel.Search = ""; ViewModel.CategoryFilter = "";
                        await ViewModel.RefreshLibraryAsync();
                        await ViewModel.OpenAsync(id);
                        ViewModel.SelectedSection = ViewModel.Sections.FirstOrDefault();
                        ViewModel.SelectedPage = ViewModel.Pages.FirstOrDefault();
                        await PrepareNotebookViewAsync(false);
                    });
                    break;
                }
                catch (Exception ex)
                {
                    ViewModel.SetStatus("Shell.NamedImportedNotebookSaved", prepared.Notebook.Title);
                    if (!ConfirmFolderImportRetry(_errors.Report(ex), true)) return;
                }
            }
            var result = prepared.Summary;
            ViewModel.SetStatus(result.FailedCount > 0
                ? result.SkippedCount > 0 ? "Shell.FolderImportCompletePartial" : "Shell.FolderImportCompleteFailed"
                : result.SkippedCount > 0 ? "Shell.FolderImportCompleteSkipped" : "Shell.FolderImportComplete",
                result.ReadyCount, result.PageCount, result.FailedCount, result.SkippedCount);
            if (result.SkippedCount > 0 && !result.RequiresConfirmation)
                new FolderImportSummaryDialog(this, result).ShowDialog();
        }
        catch (OperationCanceledException)
        {
            ViewModel.SetStatus(prepared?.HasSaved == true ? "Shell.ImportedOpenInterrupted" : "Shell.FolderImportCanceled");
        }
        catch (Exception ex)
        {
            var saved = prepared?.HasSaved == true;
            ViewModel.SetStatus(saved ? "Shell.ImportedOpenFailed" : "Shell.FolderImportFailed");
            MessageBox.Show(this, L.Text(saved ? "Shell.FolderImportSavedError" : "Shell.FolderImportUnsavedError", _errors.Report(ex)),
                saved ? L.Text("Shell.NotebookSaved") : L.Text("Shell.FolderImportErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            prepared?.Dispose();
            ViewModel.IsBusy = false; _folderImportActive = false;
            if (IsActive)
            {
                if (ViewModel.IsLibraryVisible) HomeSearch.Focus();
                else PageList.Focus();
            }
        }
    }

    private bool ConfirmFolderImportRetry(string error, bool alreadySaved)
    {
        var dialog = new Window
        {
            Owner = this,
            Width = 560, MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 24),
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        L.Bind(dialog, Window.TitleProperty, alreadySaved ? "Shell.NotebookSaved" : "Shell.FolderImportSaveErrorTitle");
        var panel = new DockPanel { Margin = new Thickness(24) }; dialog.Content = panel;
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var cancel = new Button
        {
            IsCancel = true, MinHeight = 44, MinWidth = 100,
            Style = TryFindResource("SecondaryButton") as Style
        };
        L.Bind(cancel, ContentControl.ContentProperty, alreadySaved ? "Shell.Done" : "Shell.Cancel");
        L.Bind(cancel, AutomationProperties.NameProperty, alreadySaved ? "Shell.KeepSavedNotebookClose" : "Shell.CancelFolderImport");
        actions.Children.Add(cancel);
        var retry = new Button
        {
            IsDefault = true, MinHeight = 44, MinWidth = 100,
            Margin = new Thickness(8, 0, 0, 0), Style = TryFindResource("PrimaryButton") as Style
        };
        L.Bind(retry, ContentControl.ContentProperty, alreadySaved ? "Shell.RetryOpen" : "Shell.RetrySave");
        L.Bind(retry, AutomationProperties.NameProperty, alreadySaved ? "Shell.RetryOpenSavedNotebook" : "Shell.RetrySaveImportedNotebook");
        retry.Click += (_, _) => dialog.DialogResult = true; actions.Children.Add(retry);
        var message = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };
        L.Bind(message, TextBlock.TextProperty, alreadySaved ? "Shell.RetryFolderOpenDescription" : "Shell.RetryFolderSaveDescription", error);
        panel.Children.Add(new ScrollViewer { Content = message, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 340 });
        return dialog.ShowDialog() == true;
    }
}
