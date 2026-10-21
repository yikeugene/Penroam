using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;
using Moye.Services;
using L = Moye.Localization.LocalizationService;

namespace Moye;

public partial class MainWindow
{
    private WorkspacePreferences _workspace = new();
    private WorkspacePreferencesStore? _workspaceStore;
    private bool _workspacePreloaded;
    private DraftRecoveryStore? _draftRecovery;
    private LocalSafetyService? _localSafety;
    private ScheduledBackupService? _scheduledBackups;
    private readonly DispatcherTimer _workspaceTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _backupTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _workspaceLoaded, _restoringReadingPosition, _backupRunning;
    private DateTimeOffset _nextBackupAttempt;
    private CancellationTokenSource? _automaticBackupCancellation;
    private TaskCompletionSource? _automaticBackupCompletion;

    private void InitializeWorkspace(WorkspacePreferencesStore? workspaceStore = null, WorkspacePreferences? workspace = null)
    {
        var directory = Path.GetDirectoryName(_preferencesStore.FilePath)!;
        _workspaceStore = workspaceStore ?? new(Path.Combine(directory, "workspace-preferences.json"));
        if (workspace is not null)
        {
            if (workspaceStore is null) throw new ArgumentException("Preloaded workspace requires its original store.", nameof(workspaceStore));
            _workspace = workspace; _workspacePreloaded = true;
        }
        _draftRecovery = new(Path.Combine(directory, "recovery", "drafts"));
        _localSafety = new(Path.Combine(directory, "recovery", "deleted"), ViewModel.Backup);
        _scheduledBackups = new(directory, ViewModel.Backup);
        PreserveOrganizationDeletionAsync = PreserveDeletedAsync;
        _workspaceTimer.Tick += async (_, _) => { _workspaceTimer.Stop(); CaptureReadingPosition(); await SaveWorkspaceAsync(); };
        _backupTimer.Tick += async (_, _) => await TryScheduledBackupAsync();
        PageList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => QueueWorkspaceSave()));
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModel.SelectedPage) or nameof(ViewModel.Zoom)) QueueWorkspaceSave();
        };
    }

    private async Task LoadWorkspaceAsync()
    {
        if (_workspaceStore is null) return;
        if (!_workspacePreloaded) _workspace = await _workspaceStore.LoadAsync();
        _workspacePreloaded = false;
        L.SetLanguage(_workspace.UiLanguage);
        SynchronizeLanguageSelection();
        ApplyInitialPageLayout(); _workspaceLoaded = true;
        ViewModel.Autosave.RecoveryJournal = _draftRecovery;
        ApplyWorkspaceOptions();
        await RefreshRecoveryNoticeAsync();
        if (_workspaceStore.WarningMessage is { } warning) ViewModel.SetStatus(warning.Key, warning.Arguments);
        _backupTimer.Start();
        _ = Dispatcher.BeginInvoke(new Action(async () => await TryScheduledBackupAsync()), DispatcherPriority.ContextIdle);
    }

    private void QueueWorkspaceSave()
    {
        if (!_workspaceLoaded || _restoringReadingPosition || _closing || !ViewModel.IsEditorVisible) return;
        _workspaceTimer.Stop(); _workspaceTimer.Start();
    }

    private void CaptureReadingPosition()
    {
        if (!_workspaceLoaded || _restoringReadingPosition || ViewModel.IsLibraryVisible || ViewModel.Document is not { } document ||
            ViewModel.SelectedPage is not { } selected) return;
        var viewportPoint = new Point(Viewport.ActualWidth / 2, Math.Min(80, Viewport.ActualHeight / 4));
        // Reading state follows the active visible page, not the equally near
        // left edge of a spread's gutter. Pointer zoom keeps its physical anchor.
        var anchor = CaptureSelectedPageAnchor(viewportPoint) ?? CapturePageZoomAnchor(viewportPoint);
        var page = anchor?.Page.Page ?? selected.Page;
        var point = anchor?.PagePoint ?? new Point();
        var view = anchor?.ViewportPoint ?? viewportPoint;
        _workspace.ReadingPositions[document.Id] = new(page.SectionId, page.Id,
            point.X / page.Width, point.Y / page.Height,
            Viewport.ActualWidth > 0 ? view.X / Viewport.ActualWidth : .5,
            Viewport.ActualHeight > 0 ? view.Y / Viewport.ActualHeight : 0,
            ViewModel.Zoom, _fitWidthActive, DateTimeOffset.UtcNow);
        _workspace.LastNotebookId = document.Id;
        // Bound device preferences independently of notebook content.
        foreach (var stale in _workspace.ReadingPositions.OrderByDescending(item => item.Value.ViewedUtc).Skip(500).Select(item => item.Key).ToArray())
            _workspace.ReadingPositions.Remove(stale);
        RefreshContinueButton();
    }

    private async Task SaveWorkspaceAsync(bool throwOnError = false)
    {
        if (!_workspaceLoaded || _workspaceStore is null) return;
        try { await _workspaceStore.SaveAsync(_workspace); }
        catch (Exception ex) { ViewModel.SetStatus("Core.WorkspaceSaveFailed", ex.Message); if (throwOnError) throw; }
    }

    private async Task<bool> RestoreReadingPositionAsync()
    {
        if (ViewModel.Document is not { } document || !_workspace.ReadingPositions.TryGetValue(document.Id, out var position)) return false;
        var savedPage = document.Pages.FirstOrDefault(page => page.Id == position.PageId);
        if (savedPage is null) return false;
        _restoringReadingPosition = true;
        try
        {
            ViewModel.SelectedSection = ViewModel.Sections.FirstOrDefault(section => section.Id == savedPage.SectionId);
            ViewModel.SelectedPage = ViewModel.Pages.FirstOrDefault(page => page.Page.Id == savedPage.Id);
            if (ViewModel.SelectedPage is not { } page) return false;
            SelectLibraryCurrent(); ShowSidebarTab(false); ScrollToSelected();
            ViewModel.Zoom = double.IsFinite(position.Zoom) ? position.Zoom : .85;
            await Dispatcher.InvokeAsync(() => PageList.UpdateLayout(), DispatcherPriority.Loaded);
            if (position.FitWidth) FitWidth();
            await Dispatcher.InvokeAsync(() => PageList.UpdateLayout(), DispatcherPriority.Loaded);
            double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            RestorePageZoomAnchor(new(page, new(Unit(position.X) * page.Page.Width, Unit(position.Y) * page.Page.Height),
                new(Unit(position.ViewX) * Viewport.ActualWidth, Unit(position.ViewY) * Viewport.ActualHeight)));
            return true;
        }
        finally { _restoringReadingPosition = false; }
    }

    private void RefreshContinueButton()
    {
        if (ContinueLastButton is null) return;
        ContinueLastButton.IsEnabled = !string.IsNullOrWhiteSpace(_workspace.LastNotebookId);
        L.Bind(ContinueLastButton, ToolTipProperty, "Core.ContinueHint");
    }

    private async void ContinueLastClick(object sender, RoutedEventArgs e)
    {
        var id = _workspace.LastNotebookId;
        if (string.IsNullOrWhiteSpace(id)) return;
        await OpenNotebookAsync(id);
    }

    private void ApplyWorkspaceOptions()
    {
        _touchNavigation.RequireTwoFingers = _workspace.TwoFingerNavigationOnly;
        _touchNavigation.ZoomLocked = _workspace.LockZoom;
        FavouriteToolbar.Visibility = _workspace.CompactToolbar ? Visibility.Collapsed : Visibility.Visible;
        FocusToolbar.HorizontalAlignment = _workspace.FocusToolsOnRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        UpdateWorkspaceLabels();
        UpdateTextToolbar(); RefreshFocusPresets(); RefreshContinueButton(); ApplyFocusChrome();
    }

    private async void WorkspaceSettingsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        ClearTouches(true); CommitEditors();
        if (WorkspaceOptionsDialog.Show(this, _workspace))
        {
            ApplyWorkspaceOptions(); await SaveWorkspaceAsync(); await TryScheduledBackupAsync();
        }
    }

    private void RefreshFocusPresets()
    {
        if (FocusPresetPanel is null) return;
        FocusPresetPanel.Children.Clear();
        foreach (var preset in _preferences.Presets.Where(preset => preset.IsFavorite).Take(3))
        {
            var button = new Button
            {
                Content = new System.Windows.Shapes.Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.Color)), Stroke = Brushes.Gray, StrokeThickness = .5 },
                Style = (Style)FindResource("FocusToolButton"), ToolTip = preset.Name
            };
            System.Windows.Automation.AutomationProperties.SetName(button, preset.Name);
            button.Click += (_, _) => ApplyPreset(preset, true); FocusPresetPanel.Children.Add(button);
        }
        if (_focusMode) ApplyFocusChrome();
    }

    private void ShowCommandsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        ClearTouches(); CommitEditors();
        var commands = new List<WorkspaceCommand>
        {
            new(L.Text("Core.CommandQuickNote"), L.Text("Core.CommandQuickNoteHint"), () => QuickNoteClick(this, new())),
            new(L.Text("Core.CommandSearch"), L.Text("Core.CommandSearchHint"), () => SearchContentsClick(this, new())),
            new(L.Text("Core.CommandWorkspaceSettings"), L.Text("Core.CommandWorkspaceSettingsHint"), () => WorkspaceSettingsClick(this, new())),
            new(L.Text("Core.CommandCheckUpdates"), L.Text("Core.CommandCheckUpdatesHint"), () => CheckForUpdatesClick(this, new())),
            new(L.Text("Core.CommandRecovery"), L.Text("Core.CommandRecoveryHint"), () => SafetyCenterClick(this, new())),
            new(L.Text("Core.CommandNewNotebook"), L.Text("Core.CommandNewNotebookHint"), () => NewNoteClick(this, new())),
            new(L.Text("Core.CommandContinue"), L.Text("Core.CommandContinueHint"), () => ContinueLastClick(this, new()))
        };
        if (ViewModel.IsLibraryVisible)
            commands.Add(new(L.Text("Core.CommandImportFolder"), L.Text("Core.CommandImportFolderHint"), () => ImportFolderClick(this, new())));
        if (ViewModel.IsEditorVisible)
        {
            commands.AddRange([
                new(_twoPageView ? L.Text("Core.SinglePage") : L.Text("Core.SinglePageCurrent"), L.Text("Core.SinglePageHint"), () => ChangePageLayout(false)),
                new(_twoPageView ? L.Text("Core.TwoPagesCurrent") : L.Text("Core.TwoPages"), L.Text("Core.TwoPagesHint"), () => ChangePageLayout(true)),
                new(_twoPageView ? L.Text("Core.FitSpread") : L.Text("Core.FitPage"), L.Text("Core.FitPageHint"), () => FitPage()),
                new(L.Text("Core.CommandMarkQuestion"), L.Text("Core.CommandMarkQuestionHint"), () => AddStudyQuestionClick(this, new())),
                new(L.Text("Core.CommandQuestionInbox"), L.Text("Core.CommandQuestionInboxHint"), () => StudyInboxClick(this, new())),
                new(L.Text("Core.CommandLeaveHint"), L.Text("Core.CommandLeaveHintHint"), () => SetResumeHintClick(this, new())),
                new(L.Text("Core.CommandResume"), L.Text("Core.CommandResumeHint"), () => ResumeStudyClick(this, new())),
                new(L.Text("Core.CommandAppearance"), L.Text("Core.CommandAppearanceHint"), () => VisualSearchClick(this, new())),
                new(L.Text("Core.CommandPractice"), L.Text("Core.CommandPracticeHint"), () => NewPracticeClick(this, new())),
                new(L.Text("Core.CommandPracticeAttempts"), L.Text("Core.CommandPracticeAttemptsHint"), () => PracticeAttemptsClick(this, new())),
                new(L.Text("Core.CommandRestoreRegion"), L.Text("Core.CommandRestoreRegionHint"), () => RestoreRegionClick(this, new())),
                new(L.Text("Core.CommandManagePages"), L.Text("Core.CommandManagePagesHint"), () => ManagePagesClick(this, new())),
                new(L.Text("Core.CommandExtendPage"), L.Text("Core.CommandExtendPageHint"), () => ExtendPageClick(this, new())),
                new(L.Text("Core.CommandReference"), L.Text("Core.CommandReferenceHint"), () => ShowReferenceClick(this, new())),
                new(L.Text("Core.CommandImportDocument"), L.Text("Core.CommandImportDocumentHint"), () => ImportDocumentWorkflowClick(this, new())),
                new(L.Text("Core.CommandExportPdf"), L.Text("Core.CommandExportPdfHint"), () => ExportDocumentWorkflowClick(this, new())),
                new(L.Text("Core.CommandContinueText"), L.Text("Core.CommandContinueTextHint"), () => ContinueTextOnNextPageClick(this, new())),
                new(L.Text("Core.CommandGoTo"), L.Text("Core.CommandGoToHint"), () => GoToPageClick(this, new())),
                new(L.Text("Core.CommandFocusMode"), L.Text("Core.CommandFocusModeHint"), () => ToggleFocus()),
                new(L.Text("Core.CommandPen"), L.Text("Core.CommandPenHint"), () => SetTool(InkTool.Pen)),
                new(L.Text("Core.CommandHighlighter"), L.Text("Core.CommandHighlighterHint"), () => SetTool(InkTool.Highlighter)),
                new(L.Text("Core.CommandEraser"), L.Text("Core.CommandEraserHint"), () => SetTool(_eraserTool)),
                new(L.Text("Core.CommandLasso"), L.Text("Core.CommandLassoHint"), () => SetTool(InkTool.Lasso)),
                new(L.Text("Core.CommandType"), L.Text("Core.CommandTypeHint"), () => TypeClick(this, new())),
                new(L.Text("Core.CommandUndo"), L.Text("Core.CommandUndoHint"), () => UndoClick(this, new())),
                new(L.Text("Core.CommandFitWidth"), L.Text("Core.CommandFitWidthHint"), () => FitWidth())
            ]);
        }
        WorkspaceCommandDialog.Show(this, commands);
    }

    private void GoToPageClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsEditorVisible || ViewModel.Pages.Count == 0 || ViewModel.IsBusy) return;
        var dialog = new InputDialog(this, L.Text("Core.GoToPage"), (L.Text("Core.PageNumber", ViewModel.Pages.Count), ViewModel.SelectedPage?.Number.ToString() ?? "1"));
        if (dialog.ShowDialog() != true) return;
        if (!int.TryParse(dialog.Values[0], out var number) || number < 1 || number > ViewModel.Pages.Count)
        { MessageBox.Show(this, L.Text("Core.InvalidPage"), L.Text("Core.GoToPage")); return; }
        CommitEditors(); ClearTouches(); ViewModel.SelectedPage = ViewModel.Pages[number - 1]; ScrollToSelected();
    }
}
