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
            var picker = new OpenFolderDialog { Title = "Import a folder of PDF and PowerPoint files", Multiselect = false };
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
        ViewModel.IsBusy = true; ViewModel.Operation = "Importing folder…";
        PreparedFolderImport? prepared = null;
        try
        {
            CommitEditors(); CloseSettingsPopups(); ClearTouches();
            var folder = selectFolder();
            if (folder is null) return;
            // Keep the feature guard and busy state across picker, progress, summary and retries.
            // The single-file helper releases busy and reports "unchanged", which is unsafe after our save boundary.
            var completed = await DocumentOperationDialog.RunAsync(this, "Preparing folder import", async (token, progress) =>
            {
                prepared = await new FolderImportService().PrepareAsync(folder, progress, token);
            });
            if (!completed || prepared is null)
            {
                ViewModel.Status = "Folder import canceled · No notebook was created";
                return;
            }
            if (prepared.Summary.ReadyCount == 0)
            {
                new FolderImportSummaryDialog(this, prepared.Summary).ShowDialog();
                ViewModel.Status = "No documents imported · No notebook was created";
                return;
            }
            if (prepared.Summary.RequiresConfirmation)
            {
                var summary = new FolderImportSummaryDialog(this, prepared.Summary);
                if (summary.ShowDialog() != true || !summary.ImportSuccessfulFiles)
                {
                    ViewModel.Status = "Folder import canceled · No notebook was created";
                    return;
                }
                prepared.AcceptSuccessfulFiles();
            }
            while (!prepared.HasSaved)
            {
                try
                {
                    completed = await DocumentOperationDialog.RunAsync(this, "Saving imported notebook", (token, progress) =>
                        prepared.SaveAsync(ViewModel.Repository, async () =>
                        {
                            CommitEditors();
                            await ViewModel.Autosave.FlushAsync();
                        }, progress, token));
                    if (!completed && !prepared.HasSaved)
                    {
                        ViewModel.Status = "Folder import canceled · No notebook was created";
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (ConfirmFolderImportRetry(_errors.Report(ex), false)) continue;
                    ViewModel.Status = "Folder import not saved · No notebook was created";
                    return;
                }
            }
            // From here on, retries only refresh/open the already saved ID, never reimport or resave.
            while (true)
            {
                try
                {
                    ViewModel.Operation = "Opening imported notebook…";
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
                    ViewModel.Status = $"\"{prepared.Notebook.Title}\" was saved · Could not finish opening it";
                    if (!ConfirmFolderImportRetry(_errors.Report(ex), true)) return;
                }
            }
            var result = prepared.Summary;
            ViewModel.Status = $"Imported {result.ReadyCount} {(result.ReadyCount == 1 ? "file" : "files")} · {result.PageCount} {(result.PageCount == 1 ? "page" : "pages")}"
                + (result.FailedCount > 0 ? $" · {result.FailedCount} failed" : "")
                + (result.SkippedCount > 0 ? $" · {result.SkippedCount} skipped" : "");
            if (result.SkippedCount > 0 && !result.RequiresConfirmation)
                new FolderImportSummaryDialog(this, result).ShowDialog();
        }
        catch (OperationCanceledException)
        {
            ViewModel.Status = prepared?.HasSaved == true
                ? "The imported notebook was saved · Opening was interrupted"
                : "Folder import canceled · No notebook was created";
        }
        catch (Exception ex)
        {
            var saved = prepared?.HasSaved == true;
            ViewModel.Status = saved ? "The imported notebook was saved · Could not finish opening it" : "Folder import failed · No notebook was created";
            MessageBox.Show(this, (saved ? "The imported notebook is already saved in your library. Do not import the folder again to retry opening it.\n\n" : "No notebook was created.\n\n") + _errors.Report(ex),
                saved ? "Notebook saved" : "Unable to import folder", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            Owner = this, Title = alreadySaved ? "Notebook saved" : "Unable to save imported notebook",
            Width = 560, MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 24),
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        var panel = new DockPanel { Margin = new Thickness(24) }; dialog.Content = panel;
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var cancel = new Button
        {
            Content = alreadySaved ? "Done" : "Cancel", IsCancel = true, MinHeight = 44, MinWidth = 100,
            Style = TryFindResource("SecondaryButton") as Style
        };
        AutomationProperties.SetName(cancel, alreadySaved ? "Keep saved notebook and close" : "Cancel folder import");
        actions.Children.Add(cancel);
        var retry = new Button
        {
            Content = alreadySaved ? "Retry Open" : "Retry Save", IsDefault = true, MinHeight = 44, MinWidth = 100,
            Margin = new Thickness(8, 0, 0, 0), Style = TryFindResource("PrimaryButton") as Style
        };
        AutomationProperties.SetName(retry, alreadySaved ? "Retry opening saved notebook" : "Retry saving imported notebook");
        retry.Click += (_, _) => dialog.DialogResult = true; actions.Children.Add(retry);
        var message = new TextBlock
        {
            Text = (alreadySaved
                ? "Your imported notebook is already saved in the library, but it could not be opened. Retry Open uses that same notebook; it does not import another copy."
                : "The imported notebook has not been saved. Your existing notebook and any pending changes are kept. Retry Save uses the prepared files; Cancel discards this import.")
                + "\n\n" + error,
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(new ScrollViewer { Content = message, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 340 });
        return dialog.ShowDialog() == true;
    }
}
