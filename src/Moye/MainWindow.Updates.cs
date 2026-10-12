using System.IO;
using System.Windows;
using Moye.Services;

namespace Moye;

public partial class MainWindow
{
    private IAppUpdateService? _updateService;
    private IUpdateInstaller _updateInstaller = new UpdateInstaller();
    private readonly CancellationTokenSource _updateLifetime = new();
    private CancellationTokenSource? _updateDownloadCancellation;
    private AppUpdate? _availableUpdate;
    private string? _downloadedUpdatePath;
    private string? _installOnClosePath;
    private bool _checkingUpdates;
    private bool _showUpdateCheckResult;
    private bool _updateNoticeDismissed;
    private bool _retryUpdateCheck;
    private bool _updateShutdownRequested;

    private void InitializeUpdates(IAppUpdateService? service, IUpdateInstaller? installer)
    {
        _updateService = service;
        if (installer is not null) _updateInstaller = installer;
        UpdateNotice.UpdateRequested += UpdateRequested;
        UpdateNotice.DismissRequested += (_, _) => { _updateNoticeDismissed = true; UpdateNotice.Visibility = Visibility.Collapsed; };
        UpdateNotice.CancelRequested += (_, _) => _updateDownloadCancellation?.Cancel();
        Closed += (_, _) =>
        {
            _updateShutdownRequested = true;
            _updateLifetime.Cancel();
            _updateDownloadCancellation?.Cancel();
        };
    }

    private async void CheckForUpdatesClick(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_closing || _updateShutdownRequested) return;
        if (_updateDownloadCancellation is not null) return;
        if (_updateService is null)
        {
            if (manual) UpdateNotice.ShowError("Open Penroam from its normal shortcut to check for updates. Isolated test libraries do not run the updater.");
            return;
        }
        if (manual) { _updateNoticeDismissed = false; _showUpdateCheckResult = true; UpdateNotice.ShowChecking(); }
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        _retryUpdateCheck = true;
        try
        {
            var update = await _updateService.CheckAsync(_updateLifetime.Token);
            if (_closing || _updateShutdownRequested) return;
            if (_availableUpdate?.Version != update?.Version || _availableUpdate?.Sha256 != update?.Sha256)
                _downloadedUpdatePath = null;
            _availableUpdate = update;
            _retryUpdateCheck = false;
            if (_updateNoticeDismissed) return;
            if (update is not null) UpdateNotice.ShowAvailable(update.Version.ToString(3), update.Notes);
            else if (_showUpdateCheckResult) UpdateNotice.ShowCurrent(GitHubUpdateService.CurrentVersion.ToString(3));
        }
        catch (OperationCanceledException) when (_updateLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // No network is needed to write notes. Startup failures stay quiet;
            // an explicit check reports the failure and offers a retry.
            if (_showUpdateCheckResult && !_updateNoticeDismissed && !_closing && !_updateShutdownRequested)
                UpdateNotice.ShowError("Could not check GitHub for updates. " + ex.Message);
        }
        finally { _checkingUpdates = false; _showUpdateCheckResult = false; }
    }

    private async void UpdateRequested(object? sender, EventArgs e)
    {
        if (!_ready || _closing || _updateShutdownRequested || _checkingUpdates || _updateDownloadCancellation is not null) return;
        if (UpdateMustWaitForWork)
        {
            UpdateNotice.ShowError("Please finish the current operation or input, then try updating again.");
            return;
        }
        if (_retryUpdateCheck || _availableUpdate is not { } update || _updateService is null)
        { await CheckForUpdatesAsync(true); return; }
        if (_downloadedUpdatePath is not null && File.Exists(_downloadedUpdatePath))
        { BeginUpdateInstall(_downloadedUpdatePath); return; }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
        _updateDownloadCancellation = cancellation;
        UpdateNotice.ShowDownloading(0);
        try
        {
            var progress = new Progress<double>(value =>
            {
                if (ReferenceEquals(_updateDownloadCancellation, cancellation) && !cancellation.IsCancellationRequested && !_updateShutdownRequested)
                    UpdateNotice.ShowDownloading(value);
            });
            _downloadedUpdatePath = await _updateService.DownloadAsync(update, progress, cancellation.Token);
            if (cancellation.IsCancellationRequested || _closing || _updateShutdownRequested) return;
            _updateDownloadCancellation = null;
            if (UpdateMustWaitForWork)
                UpdateNotice.ShowReady(update.Version.ToString(3));
            else BeginUpdateInstall(_downloadedUpdatePath);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_closing && !_updateShutdownRequested) UpdateNotice.ShowAvailable(update.Version.ToString(3), update.Notes);
        }
        catch (Exception ex)
        {
            _downloadedUpdatePath = null;
            if (!_closing && !_updateShutdownRequested) UpdateNotice.ShowError(ex is OperationCanceledException ? "The update download timed out. Please try again when your connection is stable." : "Could not download the update. " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_updateDownloadCancellation, cancellation)) _updateDownloadCancellation = null;
        }
    }

    private bool UpdateMustWaitForWork => ViewModel.IsBusy || AnyPenDown
        || PenSettingsPopup.IsOpen || PaperSettingsPopup.IsOpen || EraserSettingsPopup.IsOpen
        || TextFontPicker.IsDropDownOpen || TextSizePicker.IsDropDownOpen || TextAlignmentPicker.IsDropDownOpen
        || OwnedWindows.Cast<Window>().Any(window => window.IsVisible)
        || _editors.Values.Any(editor => editor.IsTextComposing);

    private void BeginUpdateInstall(string path)
    {
        _installOnClosePath = path;
        UpdateNotice.ShowPreparing();
        Close();
    }

    private async Task SaveBeforeExitAsync()
    {
        _automaticBackupCancellation?.Cancel();
        if (_automaticBackupCompletion is { } backupCompletion) await backupCompletion.Task;
        EndTemporaryPan(); ClearTouches(); CommitEditors(); CaptureReadingPosition();
        await SaveWorkspaceAsync(true);
        await ViewModel.Autosave.FlushAsync();
        await SavePreferencesAsync(true);
        if (_draftRecovery is not null) await _draftRecovery.DrainAsync();
    }
}
